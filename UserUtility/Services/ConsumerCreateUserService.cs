using UserUtility.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Threading;
using System.Reflection;

namespace UserUtility.Services
{


    public class ConsumerCreateUserService : IConsumerService
    {

        private readonly ILogger<IConsumerService> _logger;
 
        private readonly IConfiguration _config;
        //private readonly BlockingCollection<CustomOktaUser> _userQueue;
        private readonly UserQueue<CustomOktaUser> _inputQueue;
        private IOutputService _outputFiles;
        private int _queueWaitms;
        private int _throttleMs;
        private string _apiUrl;
        private string _apiToken;
        private string _group;
        private string _activateUserwithCreate;
        private bool _provisionUser;
        private string _sendEmailwithActivation;
        private int _workFactor;
        private string _algorithm;
        private string _saltOrder;
 
        private List<string> _omitFromUserProfile;
        private int _consumerTasks;

        public ConsumerCreateUserService(ILogger<IConsumerService> logger, IConfiguration config, UserQueue<CustomOktaUser> inputQueue, IOutputService outputFiles)
        {
            _logger = logger;
            _inputQueue = inputQueue;
            _config = config;
            _outputFiles = outputFiles;
            _queueWaitms = _config.GetValue<int>("generalConfig:queueWaitms");
            _throttleMs = _config.GetValue<int>("generalConfig:throttleMs");

            _apiUrl = _config.GetValue<string>("generalConfig:org");
            _apiToken = _config.GetValue<string>("generalConfig:apiToken");
            //ImportParameters importParameters = _config.GetSection("importConfig").Get<ImportParameters>();
            _group = _config.GetValue<string>("importConfig:groupId");
            _activateUserwithCreate = _config.GetValue<string>("importConfig:activateUserwithCreate");
            _provisionUser = _config.GetValue<bool>("importConfig:provisionUser");
            _sendEmailwithActivation = _config.GetValue<string>("importConfig:sendEmailwithActivation");
            _workFactor = _config.GetValue<int>("importConfig:workFactor");
            _algorithm = _config.GetValue<string>("importConfig:algorithm");
            _saltOrder = _config.GetValue<string>("importConfig:saltOrder");
            _omitFromUserProfile = _config.GetSection("importConfig:omitFromUserProfile").Get<List<string>>();
            _consumerTasks = _config.GetValue<int>("generalConfig:consumerTasks");

        }

        public async Task ProcessConsumer()
        {
            //_logger.LogDebug("ProcessConsumer  TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
            
            _logger.LogInformation("ProcessCreateUserConsumer Start  with {0} consumerTasks on TaskId={1}, ThreadId={2}", _consumerTasks, Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            Task[] consumers = new Task[_consumerTasks];
            for (int i = 0; i < consumers.Length; i++)
            {
                consumers[i] = Task.Run(() =>
                        {
                           // Console.WriteLine("console ProcessConsumer(s) TaskId={0},ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                            _logger.LogTrace("SpinUp New Create ServiceQueue Task on TaskId={0},ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                            ServiceQueue(_inputQueue.userQueue);
                        }
                    );//end Task.Run
            }//end of for

            
            try
            {
                await Task.WhenAll(consumers);
            }
            catch (AggregateException ae)
            {
                _logger.LogError("One or more exceptions occurred:");
                foreach (var ex in ae.InnerExceptions)
                    _logger.LogError("Exception {0}: {1}", ex.GetType().Name, ex.Message);
            }
            Console.WriteLine();
            _logger.LogInformation("ProcessCreateUserConsumer Complete TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
            return;
        }//end Process Consumer

        public void ServiceQueue(BlockingCollection<CustomOktaUser> userQueue)
        {
            _logger.LogInformation("ServiceQueue Start New CreateUser QueueCount={0},ThrottleMs={1},TaskId={2}, ThreadId={3}", userQueue.Count, _throttleMs, Task.CurrentId, Thread.CurrentThread.ManagedThreadId);


            while (!userQueue.IsCompleted)
            {
                CustomOktaUser nextItem = null;
                try
                {
                    if (!userQueue.TryTake(out nextItem, _queueWaitms))
                    {
                        _logger.LogTrace("ServiceQueue Take Blocked TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                    }
                    else
                    {
                        Task.WaitAll(Task.Delay(_throttleMs));
                        //Thread.Sleep(_throttleMs);
                        Console.Write("\r{0}", userQueue.Count());
                        _logger.LogTrace("ServiceQueue CreateUser Take Success QueueCount={0}, TaskId={1}, ThreadId={2}, item={3}", userQueue.Count, Task.CurrentId, Thread.CurrentThread.ManagedThreadId, nextItem.email);
                        var rsp = PostDataAsync(nextItem);
                        _logger.LogTrace("ServiceQueue CreateUser return PostDataAsync userQueueCount={0}, successQueueCount={1},TaskId={2}, ThreadId={3}, item={4}", userQueue.Count, _inputQueue.successQueue.Count,Task.CurrentId, Thread.CurrentThread.ManagedThreadId, nextItem.email);
                    }
                    
                }
                catch (OperationCanceledException)
                {
                    _logger.LogError("ServiceQueue CreateUser Taking Cancelled TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                    //break;
                }
                catch (Exception ex)
                {
                    _logger.LogError("ServiceQueue CreateUser Exp={0} TaskId={1}, ThreadId={2}", ex.ToString(), Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                }
            }//end while
       
            _logger.LogInformation("ServiceQueue  CreateUser Complete TaskId={0}, ThreadId={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
        }//end ServiceQueue

        //[Benchmark]
        public async Task PostDataAsync(CustomOktaUser user)
        {
            _logger.LogTrace("PostDataAsync Start CreateUser TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email);

            string content = null;
            HttpResponseMessage activateResponse = null;

            //build Okta profile attributes from class
            var properties = typeof(CustomOktaUser).GetProperties().Where(n => n.PropertyType == typeof(string)  || n.PropertyType == typeof(List<string>));

            //filter out non-profile objects
            //properties = properties.Where(n => n.Name != "value");
            //properties = properties.Where(n => n.Name != "workFactor");
            //properties = properties.Where(n => n.Name != "salt");
            //properties = properties.Where(n => n.Name != "algorithm");

            foreach (var item in _omitFromUserProfile)
            {
                properties = properties.Where(n => n.Name != item);
            }

            JObject jsonObject = null;
            //put profile attributes in JSON
            JTokenWriter writer = new JTokenWriter();
            writer.WriteStartObject();
            foreach (PropertyInfo property in properties)
            {
                if (!(property.GetValue(user, null) == null))
                {
                    if (property.PropertyType == typeof(System.String))
                    {
                        writer.WritePropertyName(property.Name);
                        writer.WriteValue(property.GetValue(user, null));
                    }
                    else if (property.PropertyType == typeof(List<string>))
                    {
                        writer.WritePropertyName(property.Name);
                        writer.WriteStartArray();
                        List<string> myList = (List<string>)property.GetValue(user, null);
                        foreach (var item in myList)
                        {
                            writer.WriteValue(item);
                        }                 
                        writer.WriteEndArray();
                    }
                }
            }
            writer.WriteEndObject();

            JObject profile = (JObject)writer.Token;

            ////config to create JSON
            //JObject profile = new JObject(
            //    new JProperty("firstName", user.firstName),
            //    new JProperty("lastName", user.lastName),
            //    new JProperty("email", user.email),
            //    new JProperty("login", user.login)
            //    );

            JArray groups = new JArray(_group);
            JObject credentials = null;
            JObject password = null;
            //check user.algorithm for hash type or cleartext
            if (_algorithm == "BCRYPT" || _algorithm == "SHA-1" || _algorithm == "SHA-256" || _algorithm == "SHA-512" || _algorithm == "MD5" )
            {
                JObject hash = null;
                switch (_algorithm)
                {
                    case "BCRYPT":
                        hash = new JObject(
                            new JProperty("salt", user.salt),
                            new JProperty("workFactor", _workFactor),
                            new JProperty("value", user.value),
                            new JProperty("algorithm", _algorithm)
                            );
                        break;
                    case "SHA-1":
                        hash = new JObject(
                            new JProperty("salt", user.salt),
                            new JProperty("saltOrder", _saltOrder),
                            new JProperty("value", user.value),
                            new JProperty("algorithm", _algorithm)
                            );
                        break;
                    case "SHA-256":
                        hash = new JObject(
                            new JProperty("salt", user.salt),
                            new JProperty("saltOrder", _saltOrder),
                            new JProperty("value", user.value),
                            new JProperty("algorithm", _algorithm)
                            );
                        break;
                    case "SHA-512":
                        hash = new JObject(
                            new JProperty("salt", user.salt),
                            new JProperty("saltOrder", _saltOrder),
                            new JProperty("value", user.value),
                            new JProperty("algorithm", _algorithm)
                            );
                        break;
                    case "MD5":
                        hash = new JObject(
                            new JProperty("salt", user.salt),
                            new JProperty("saltOrder", _saltOrder),
                            new JProperty("value", user.value),
                            new JProperty("algorithm", _algorithm)
                            );
                        break;
                    default:
                        break;
                }
                password = new JObject(
                    new JProperty("hash", hash)
                    );
                credentials = new JObject(
                    new JProperty("password", password)
                    );
            }
            else if (_algorithm == "CLEARTEXT")
            {
                //this is cleartext password in file
                password = new JObject(
                    new JProperty("value", user.value)
                    );
                credentials = new JObject(
                    new JProperty("password", password)
                    );


            }

            if (_algorithm == "NONE")
            {
                //no password provided
                jsonObject = new JObject(
                    new JProperty("profile", profile),
                    new JProperty("groupIds", groups)
                );
            }
            else
            {
                //password provided add credentials
                jsonObject = new JObject(
                        new JProperty("profile", profile),
                        new JProperty("groupIds", groups),
                        new JProperty("credentials", credentials)
                    );
            }


            var json = JsonConvert.SerializeObject(jsonObject);
            _logger.LogTrace("PostDataAsync User Json= {0}", json.ToString());
            var stringContent = new StringContent(json, Encoding.UTF8, "application/json");


            using (HttpClient client = new HttpClient())
            {
                string path = _apiUrl + "/api/v1/users?activate=" + _activateUserwithCreate;
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("SSWS", _apiToken);

                _logger.LogTrace("PostDataAsync send create API email={0}", user.email);

                HttpResponseMessage createResponse = await client.PostAsync(path, stringContent).ConfigureAwait(false);
 
                


                //two flows; create with password or create withoutpassword then activate without email
                if (createResponse.IsSuccessStatusCode)
                {
                    if (_provisionUser)
                    {
                        content = await createResponse.Content.ReadAsStringAsync();
                        BasicOktaUser oktaUser = Newtonsoft.Json.JsonConvert.DeserializeObject<BasicOktaUser>(content);
                        // create empty json payload
                        jsonObject = new JObject();
                        string serialJson = JsonConvert.SerializeObject(jsonObject);
                        _logger.LogTrace("PostDataAsync User Json= {0}", serialJson.ToString());
                        StringContent blankContent = new StringContent(serialJson, Encoding.UTF8, "application/json");

                        //send API
                        path = _apiUrl + "/api/v1/users/" + oktaUser.Id + "/lifecycle/activate?sendEmail=" + _sendEmailwithActivation;
                        _logger.LogTrace("PostDataAsync send activate API");
                        activateResponse = await client.PostAsync(path, blankContent).ConfigureAwait(false);

                        if (activateResponse.IsSuccessStatusCode)
                        {
                            user.output = "successfully activated";
                            string limit = activateResponse.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                            string remaining = activateResponse.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                            string reset = activateResponse.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                            string out429 = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;
                            try
                            {
                                _inputQueue.successQueue.Add(user);
                                _logger.LogDebug("PostDataAsync Success add Queue TaskId={0}, ThreadId={1}, user={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email, out429);
                            }
                            catch (System.InvalidOperationException)
                            {
                                _logger.LogError("PostDataAsync Error Adding Entry to successQueue Id={0}", oktaUser.Id);
                            }                          
                        }
                        else
                        {
                            if (activateResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                            {
                                //for 429 users
                                Consumer_429(user, activateResponse);

                                ////check rate limit headers for details
                                //string limit = activateResponse.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                                //string remaining = activateResponse.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                                //string reset = activateResponse.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                                //user.output = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;
                                //try
                                //{
                                //    _inputQueue.replayQueue.Add(user);
                                //    _logger.LogDebug("PostDataAsync Replay add Queue TaskId={0}, ThreadId={1}, user={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email, user.output);
                                //}
                                //catch (System.InvalidOperationException)
                                //{
                                //    _logger.LogError("PostDataAsync Error Adding Entry to replayQueue");
                                //}

                            }
                            else
                            {
                                //for failed users  
                                content = await activateResponse.Content.ReadAsStringAsync();
                                OktaApiError oktaApiError = Newtonsoft.Json.JsonConvert.DeserializeObject<OktaApiError>(content);
                                user.output = "Status=" + activateResponse.StatusCode.ToString() + ",Error=" + oktaApiError.errorCauses[0].errorSummary;
                                Consumer_Failures(user, activateResponse);

                                //string limit = activateResponse.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                                //string remaining = activateResponse.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                                //string reset = activateResponse.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                                //string out429 = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;
                                
                                //try
                                //{
                                //    _inputQueue.failureQueue.Add(user);
                                //    _logger.LogDebug("PostDataAsync Failure add Queue TaskId={0}, ThreadId={1}, user={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email, user.output);
                                //}
                                //catch (System.InvalidOperationException)
                                //{
                                //    _logger.LogError("PostDataAsync Error Adding Entry to failureQueue");
                                //}
                            }
                        }//end if activateResponse
                    }
                    else
                    {
                        _logger.LogTrace("PostDataAsync API rsp create:success, prov:false");
                        //for create users
                        content = await createResponse.Content.ReadAsStringAsync();
                        BasicOktaUser oktaUser = Newtonsoft.Json.JsonConvert.DeserializeObject<BasicOktaUser>(content);
                        user.output = "OktaId=" + oktaUser.Id;
                        _logger.LogTrace("PostDataAsync API rsp create:success, prov:false Id={0}", oktaUser.Id);
                        Consumer_Success(user, createResponse);

                        //string limit = createResponse.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                        //string remaining = createResponse.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                        //string reset = createResponse.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                        //string out429 = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;

                        //try
                        //{
                        //    _inputQueue.successQueue.Add(user);
                        //    //_logger.LogTrace("PostDataAsync Success add Queue TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email);
                        //    _logger.LogDebug("PostDataAsync Success add Queue TaskId={0}, ThreadId={1}, user={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email, out429);
                        //}
                        //catch (System.InvalidOperationException)
                        //{
                        //    _logger.LogError("PostDataAsync Error Adding Entry to successQueue Id={0}", oktaUser.Id);
                        //}
                    }
                }
                else
                {
                    _logger.LogTrace("PostDataAsync API rsp create:NOT success, status_code={0}", createResponse.StatusCode);
                    if (createResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        _logger.LogTrace("PostDataAsync API rsp create: replay, prov:false");
                        //for 429 users
                        Consumer_429(user, createResponse);

                        ////check rate limit headers for details
                        //string limit = createResponse.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                        //string remaining = createResponse.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                        //string reset = createResponse.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                        //user.output = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;
                        
                        //try
                        //{
                        //    _inputQueue.replayQueue.Add(user);
                        //    _logger.LogDebug("PostDataAsync Replay add Queue TaskId={0}, ThreadId={1}, user={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email, user.output);
                        //}
                        //catch (System.InvalidOperationException)
                        //{
                        //    _logger.LogError("PostDataAsync Error Adding Entry to replayQueue");
                        //}

                    }
                    else
                    {
                        _logger.LogTrace("PostDataAsync API rsp create:failure, prov:false");
                        //for failed users  
                        content = await createResponse.Content.ReadAsStringAsync();
                        OktaApiError oktaApiError = Newtonsoft.Json.JsonConvert.DeserializeObject<OktaApiError>(content);
                        user.output = "Status=" + createResponse.StatusCode.ToString() + ",Error=" + oktaApiError.errorCauses[0].errorSummary;
                        Consumer_Failures(user, createResponse);

                        //string limit = createResponse.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                        //string remaining = createResponse.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                        //string reset = createResponse.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                        //string out429 = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;
                        //try
                        //{
                        //    _inputQueue.failureQueue.Add(user);
                        //    _logger.LogDebug("PostDataAsync Failure add Queue TaskId={0}, ThreadId={1}, user={2},failure={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email, user.output);
                        //}
                        //catch (System.InvalidOperationException)
                        //{
                        //    _logger.LogError("PostDataAsync Error Adding Entry to failureQueue");
                        //}

                    }
                }//end create not success

            }
            _logger.LogTrace("PostDataAsync  Complete TaskId={0}, ThreadId={1}, user={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId,user.email);
        }

        private async Task Consumer_NewUserCreate(CustomOktaUser user, JObject jsonObject, HttpClient client)
        {
            string content = null;
            HttpResponseMessage activateResponse = null;
            string path = _apiUrl + "/api/v1/users?activate=" + _activateUserwithCreate;

            var json = JsonConvert.SerializeObject(jsonObject);
            _logger.LogTrace("PostDataAsync User Json= {0}", json.ToString());
            var stringContent = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogTrace("PostDataAsync send create API email={0}", user.email);

            HttpResponseMessage createResponse = await client.PostAsync(path, stringContent).ConfigureAwait(false);


            //two flows; create with password or create withoutpassword then activate without email
            if (createResponse.IsSuccessStatusCode)
            {
                if (_provisionUser)
                {
                    content = await createResponse.Content.ReadAsStringAsync();
                    BasicOktaUser oktaUser = Newtonsoft.Json.JsonConvert.DeserializeObject<BasicOktaUser>(content);
                    // create empty json payload
                    jsonObject = new JObject();
                    string serialJson = JsonConvert.SerializeObject(jsonObject);
                    _logger.LogTrace("PostDataAsync User Json= {0}", serialJson.ToString());
                    StringContent blankContent = new StringContent(serialJson, Encoding.UTF8, "application/json");

                    //send API
                    path = _apiUrl + "/api/v1/users/" + oktaUser.Id + "/lifecycle/activate?sendEmail=" + _sendEmailwithActivation;
                    _logger.LogTrace("PostDataAsync send activate API");
                    activateResponse = await client.PostAsync(path, blankContent).ConfigureAwait(false);

                    if (activateResponse.IsSuccessStatusCode)
                    {
                        user.output = "successfully activated";
                        Consumer_Success(user, activateResponse);

                    }
                    else
                    {
                        if (activateResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                        {
                            //for 429 users
                            Consumer_429(user, activateResponse);
                        }
                        else
                        {
                            //for failed users  
                            content = await activateResponse.Content.ReadAsStringAsync();
                            OktaApiError oktaApiError = Newtonsoft.Json.JsonConvert.DeserializeObject<OktaApiError>(content);
                            user.output = "Status=" + activateResponse.StatusCode.ToString() + ",Error=" + oktaApiError.errorCauses[0].errorSummary;
                            Consumer_Failures(user, activateResponse);
                        }
                    }//end if activateResponse
                }
                else
                {
                    _logger.LogTrace("PostDataAsync API rsp create:success, prov:false");
                    //for create users
                    content = await createResponse.Content.ReadAsStringAsync();
                    BasicOktaUser oktaUser = Newtonsoft.Json.JsonConvert.DeserializeObject<BasicOktaUser>(content);
                    user.output = "OktaId=" + oktaUser.Id;
                    _logger.LogTrace("PostDataAsync API rsp create:success, prov:false Id={0}", oktaUser.Id);
                    Consumer_Success(user, createResponse);
                }
            }
            else
            {
                _logger.LogTrace("PostDataAsync API rsp create:NOT success, status_code={0}", createResponse.StatusCode);
                if (createResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    _logger.LogTrace("PostDataAsync API rsp create: replay, prov:false");
                    //for 429 users
                    Consumer_429(user, createResponse);
                }
                else
                {
                    _logger.LogTrace("PostDataAsync API rsp create:failure, prov:false");
                    //for failed users  
                    content = await createResponse.Content.ReadAsStringAsync();
                    OktaApiError oktaApiError = Newtonsoft.Json.JsonConvert.DeserializeObject<OktaApiError>(content);
                    user.output = "Status=" + createResponse.StatusCode.ToString() + ",Error=" + oktaApiError.errorCauses[0].errorSummary;
                    Consumer_Failures(user, createResponse);
                }
            }//end create not success
        }



        private void Consumer_Success(CustomOktaUser user, HttpResponseMessage response)
        {

            //for successfully processed  users
            string limit = response.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
            string remaining = response.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
            string reset = response.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
            string out429 = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;

            try
            {
                _inputQueue.successQueue.Add(user);
                _logger.LogDebug("PostDataAsync Success add Queue TaskId={0}, ThreadId={1}, user={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email, out429);
            }
            catch (System.InvalidOperationException)
            {
                _logger.LogError("PostDataAsync Error Adding Entry to successQueue Id={0}", user.output);
            }
        }

        private void Consumer_429(CustomOktaUser user, HttpResponseMessage response)
        {
            //for 429 users
            //check rate limit headers for details
            string limit = response.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
            string remaining = response.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
            string reset = response.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
            user.output = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;
            try
            {
                _inputQueue.replayQueue.Add(user);
                _logger.LogDebug("PostDataAsync Replay add Queue TaskId={0}, ThreadId={1}, user={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email, user.output);
            }
            catch (System.InvalidOperationException)
            {
                _logger.LogError("PostDataAsync Error Adding Entry to replayQueue");
            }
        }

        private void Consumer_Failures(CustomOktaUser user, HttpResponseMessage response)
        {
            //for failed users  
            //content = response.Content.ReadAsStringAsync().Result;
            //OktaApiError oktaApiError = Newtonsoft.Json.JsonConvert.DeserializeObject<OktaApiError>(content);
            //user.output = "Status=" + response.StatusCode.ToString() + ",Error=" + oktaApiError.errorCauses[0].errorSummary;

            string limit = response.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
            string remaining = response.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
            string reset = response.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
            string out429 = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;

            try
            {
                _inputQueue.failureQueue.Add(user);
                _logger.LogDebug("PostDataAsync Failure add Queue TaskId={0}, ThreadId={1}, user={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email, user.output);
            }
            catch (System.InvalidOperationException)
            {
                _logger.LogError("PostDataAsync Error Adding Entry to failureQueue");
            }
        }

    }

}

