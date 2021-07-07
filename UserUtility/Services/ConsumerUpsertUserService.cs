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


    public class ConsumerUpsertUserService : IConsumerService
    {

        private readonly ILogger<IConsumerService> _logger;
 
        private readonly IConfiguration _config;
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
        List<string> _omitFromUserProfile;
        private int _consumerTasks;

        public ConsumerUpsertUserService(ILogger<IConsumerService> logger, IConfiguration config, UserQueue<CustomOktaUser> inputQueue, IOutputService outputFiles)
        {
            _logger = logger;
            _inputQueue = inputQueue;
            _config = config;
            _outputFiles = outputFiles;
            _queueWaitms = _config.GetValue<int>("generalConfig:queueWaitms");
            _throttleMs = _config.GetValue<int>("generalConfig:throttleMs");

            _apiUrl = _config.GetValue<string>("generalConfig:org");
            _apiToken = _config.GetValue<string>("generalConfig:apiToken");
            _group = _config.GetValue<string>("importConfig:groupId");
            _activateUserwithCreate = _config.GetValue<string>("importConfig:activateUserwithCreate");
            _provisionUser = _config.GetValue<bool>("importConfig:provisionUser");
            _sendEmailwithActivation = _config.GetValue<string>("importConfig:sendEmailwithActivation");
            _omitFromUserProfile = _config.GetSection("importConfig:omitFromUserProfile").Get<List<string>>();
            _consumerTasks = _config.GetValue<int>("generalConfig:consumerTasks");
        }

        public async Task ProcessConsumer()
        {
            
            _logger.LogInformation("ConsumerUpsertUserService Start with {0} consumerTasks on TaskId={1}, ThreadId={2}", _consumerTasks, Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            Task[] consumers = new Task[_consumerTasks];
            for (int i = 0; i < consumers.Length; i++)
            {
                consumers[i] = Task.Run(() =>
                        {
                            _logger.LogTrace("SpinUp New Upsert ServiceQueue Task on TaskId={0},ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
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
            _logger.LogInformation("ConsumerUpdateUserService  Complete TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
            return;
        }//end Process Consumer

        public void ServiceQueue(BlockingCollection<CustomOktaUser> userQueue)
        {
            _logger.LogInformation("ServiceQueue Start New UpsertUser QueueCount={0},ThrottleMs={1},TaskId={2}, ThreadId={3}", userQueue.Count, _throttleMs, Task.CurrentId, Thread.CurrentThread.ManagedThreadId);


            while (!userQueue.IsCompleted)
            {
                CustomOktaUser nextItem = null;
                try
                {
                    if (!userQueue.TryTake(out nextItem, _queueWaitms))
                    {
                        //_logger.LogTrace("ServiceQueue UpsertUser Take Blocked TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                    }
                    else
                    {
                        Task.WaitAll(Task.Delay(_throttleMs));
                        Console.Write("\r{0}", userQueue.Count());          
                        _logger.LogDebug("ServiceQueue UpsertUser Take Success QueueCount={0}, TaskId={1}, ThreadId={2}, item={3}", userQueue.Count, Task.CurrentId, Thread.CurrentThread.ManagedThreadId, nextItem.email);
                        var rsp = PostDataAsync(nextItem);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogError("ServiceQueue  UpsertUser Taking Cancelled TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                    //break;
                }
                catch (Exception ex)
                {
                    _logger.LogError("ServiceQueue UpsertUser Exp={0} TaskId={1}, ThreadId={2}", ex.ToString(), Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                }
            }//end while
            _logger.LogDebug("ServiceQueue Complete UpsertUser TaskId={0}, ThreadId={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
        }//end ServiceQueue

        //[Benchmark]
        public async Task PostDataAsync(CustomOktaUser user)
        {
            _logger.LogTrace("Start UpsertUser PostDataAsync  TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email);
            string path = null;
            string content = null;
            HttpResponseMessage updateResponse = null;
            HttpResponseMessage createResponse = null;
            HttpResponseMessage activateResponse = null;
            JObject jsonObject = null;
            JObject credentials = null;
            JObject password = null;
            JObject profile = null;


            //build Okta profile attributes from class
            //put profile attributes in JSON
            JTokenWriter writer = new JTokenWriter();
            writer.WriteStartObject();

            var properties = typeof(CustomOktaUser).GetProperties().Where(n => n.PropertyType == typeof(string));
            foreach (var item in _omitFromUserProfile)
            {
                properties = properties.Where(n => n.Name != item);
            }
            foreach (PropertyInfo property in properties)
            {
                if (!(property.GetValue(user, null) == null))
                {
                    writer.WritePropertyName(property.Name);
                    writer.WriteValue(property.GetValue(user, null));
                }
            }
            writer.WriteEndObject();
            profile = (JObject)writer.Token;


            using (HttpClient client = new HttpClient())
            {                         
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("SSWS", _apiToken);
                //GET user to determine existence
                path = _apiUrl + "/api/v1/users/" + user.login;
                HttpResponseMessage getResponse = await client.GetAsync(path).ConfigureAwait(false);

                if (getResponse.IsSuccessStatusCode)
                {
                    //user found must be an update
                    //finish building the JSON
                    if (!string.IsNullOrEmpty(user.value))
                    {
                        password = new JObject(
                            new JProperty("value", user.value)
                            );
                        credentials = new JObject(
                            new JProperty("password", password)
                            );
                        jsonObject = new JObject(
                                new JProperty("profile", profile),
                                new JProperty("credentials", credentials)
                            );
                    }
                    else
                    {
                        jsonObject = new JObject(
                            new JProperty("profile", profile)
                        );
                    }


                    string serialJson = Newtonsoft.Json.JsonConvert.SerializeObject(jsonObject);
                    //_logger.LogTrace("User Json= {0}", serialJson.ToString());
                    StringContent stringContent = new StringContent(serialJson, Encoding.UTF8, "application/json");

                    //send API
                    path = _apiUrl + "/api/v1/users/" + user.login;
                    _logger.LogTrace("send update API for {0}", user.login);
                    updateResponse = await client.PostAsync(path, stringContent).ConfigureAwait(false);

                    if (updateResponse.IsSuccessStatusCode)
                    {

                        content = await updateResponse.Content.ReadAsStringAsync();
                        BasicOktaUser oktaUser = Newtonsoft.Json.JsonConvert.DeserializeObject<BasicOktaUser>(content);
                        user.output = "successful update on OktaId=" + oktaUser.Id;
                        Consumer_Success(user, updateResponse);

                        ////_inputQueue.successQueue.TryAdd(user);
                        ////_logger.LogTrace("PostDataAsync Success add Queue TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId,user.email);

                        //string limit = updateResponse.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                        //string remaining = updateResponse.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                        //string reset = updateResponse.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                        //string out429 = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;


                        //try
                        //{
                        //    _inputQueue.successQueue.Add(user);
                        //    _logger.LogDebug("PostDataAsync Update Success add Queue TaskId={0}, ThreadId={1}, User={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email, out429);
                        //}
                        //catch (System.InvalidOperationException)
                        //{
                        //    _logger.LogError("PostDataAsync Error Adding Entry to successQueue Id={0}", oktaUser.Id);
                        //}


                    }
                    else
                    {
                        if (updateResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                        {
                            //for 429 users
                            Consumer_429(user, updateResponse);

                            ////check rate limit headers for details
                            //string limit = updateResponse.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                            //string remaining = updateResponse.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                            //string reset = updateResponse.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                            //user.output = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;
                            ////_inputQueue.replayQueue.TryAdd(user);
               
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
                            content = await updateResponse.Content.ReadAsStringAsync();
                            OktaApiError oktaApiError = Newtonsoft.Json.JsonConvert.DeserializeObject<OktaApiError>(content);
                            user.output = "Status=" + updateResponse.StatusCode.ToString() + ",Error=" + oktaApiError.errorCauses[0].errorSummary;
                            Consumer_Failures(user, updateResponse);

                            ////_inputQueue.failureQueue.TryAdd(user);
                            ////_logger.LogTrace("PostDataAsync Failure add Queue TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email);

                            //string limit = updateResponse.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                            //string remaining = updateResponse.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                            //string reset = updateResponse.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
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
                    }//end if createResponse

                }
                else if (getResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    await Consumer_NewUserCreate(user, profile, client);


                    ////user not found must be a create
                    ////finish building the JSON
                    //JArray groups = new JArray(_group);

                    //if (!string.IsNullOrEmpty(user.value))
                    //{
                    //    password = new JObject(
                    //        new JProperty("value", user.value)
                    //        );
                    //    credentials = new JObject(
                    //        new JProperty("password", password)
                    //        );
                    //    jsonObject = new JObject(
                    //            new JProperty("profile", profile),
                    //            new JProperty("groupIds", groups),
                    //            new JProperty("credentials", credentials)
                    //        );
                    //}
                    //else
                    //{
                    //    jsonObject = new JObject(
                    //        new JProperty("profile", profile),
                    //        new JProperty("groupIds", groups)
                    //    );
                    //}


                    //string serialJson = JsonConvert.SerializeObject(jsonObject);
                    //_logger.LogTrace("PostDataAsync User Json= {0}", serialJson.ToString());
                    //StringContent stringContent = new StringContent(serialJson, Encoding.UTF8, "application/json");

                    ////send API
                    //path = _apiUrl + "/api/v1/users?activate=" + _activateUserwithCreate;
                    //_logger.LogTrace("PostDataAsync send create API email={0}", user.email);
                    //createResponse = await client.PostAsync(path, stringContent).ConfigureAwait(false);

                    //if (createResponse.IsSuccessStatusCode)
                    //{
                    //    if (_provisionUser)
                    //    {
                    //        content = await createResponse.Content.ReadAsStringAsync();
                    //        BasicOktaUser oktaUser = Newtonsoft.Json.JsonConvert.DeserializeObject<BasicOktaUser>(content);
                    //        // create empty json payload
                    //        jsonObject = new JObject();
                    //        string serialJson1 = JsonConvert.SerializeObject(jsonObject);

                    //        StringContent blankContent = new StringContent(serialJson1, Encoding.UTF8, "application/json");

                    //        //send API
                    //        path = _apiUrl + "/api/v1/users/" + oktaUser.Id + "/lifecycle/activate?sendEmail=" + _sendEmailwithActivation;
                    //        _logger.LogTrace("PostDataAsync send activate API Id={0}", oktaUser.Id);
                    //        activateResponse = await client.PostAsync(path, blankContent).ConfigureAwait(false);


                    //        if (activateResponse.IsSuccessStatusCode)
                    //        {

                    //            user.output = "successfully activated";
                    //            //_inputQueue.successQueue.TryAdd(user);
                    //            string limit = activateResponse.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                    //            string remaining = activateResponse.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                    //            string reset = activateResponse.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                    //            string out429 = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;
                    //            try
                    //            {
                    //                _inputQueue.successQueue.Add(user);
                    //                _logger.LogDebug("PostDataAsync Activate Success add Queue TaskId={0}, ThreadId={1}, user={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email, out429);
                    //            }
                    //            catch (System.InvalidOperationException)
                    //            {
                    //                _logger.LogError("PostDataAsync Error Adding Entry to successQueue Id={0}", oktaUser.Id);
                    //            }

                    //        }
                    //        else
                    //        {
                    //            if (activateResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    //            {
                    //                //for 429 users
                    //                //check rate limit headers for details
                    //                string limit = activateResponse.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                    //                string remaining = activateResponse.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                    //                string reset = activateResponse.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                    //                user.output = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;
                    //                //_inputQueue.replayQueue.TryAdd(user);

                    //                try
                    //                {
                    //                    _inputQueue.replayQueue.Add(user);
                    //                    _logger.LogDebug("PostDataAsync Replay add Queue TaskId={0}, ThreadId={1}, user={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email, user.output);

                    //                }
                    //                catch (System.InvalidOperationException)
                    //                {
                    //                    _logger.LogError("PostDataAsync Error Adding Entry to replayQueue");
                    //                }



                    //            }
                    //            else
                    //            {
                    //                //for failed users  
                    //                content = await activateResponse.Content.ReadAsStringAsync();
                    //                OktaApiError oktaApiError = Newtonsoft.Json.JsonConvert.DeserializeObject<OktaApiError>(content);
                    //                user.output = "Status=" + activateResponse.StatusCode.ToString() + ",Error=" + oktaApiError.errorCauses[0].errorSummary;
                    //                //_inputQueue.failureQueue.TryAdd(user);
                    //                //_logger.LogTrace("PostDataAsync Failure add Queue TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email);

                    //                string limit = activateResponse.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                    //                string remaining = activateResponse.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                    //                string reset = activateResponse.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                    //                string out429 = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;


                    //                try
                    //                {
                    //                    _inputQueue.failureQueue.Add(user);
                    //                    _logger.LogDebug("PostDataAsync Failure add Queue TaskId={0}, ThreadId={1}, user={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email, user.output);

                    //                }
                    //                catch (System.InvalidOperationException)
                    //                {
                    //                    _logger.LogError("PostDataAsync Error Adding Entry to failureQueue");
                    //                }



                    //            }
                    //        }//end if activateResponse

                    //    }
                    //    else
                    //    {
                    //        _logger.LogTrace("PostDataAsync API rsp create:success, prov:false");
                    //        //for create users
                    //        content = await createResponse.Content.ReadAsStringAsync();
                    //        BasicOktaUser oktaUser = Newtonsoft.Json.JsonConvert.DeserializeObject<BasicOktaUser>(content);
                    //        user.output = "OktaId=" + oktaUser.Id;
                    //        //_inputQueue.successQueue.TryAdd(user);
                    //        //_logger.LogTrace("PostDataAsync Success add Queue TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId,user.email);

                    //        string limit = createResponse.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                    //        string remaining = createResponse.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                    //        string reset = createResponse.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                    //        string out429 = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;

                    //        try
                    //        {
                    //            _inputQueue.successQueue.Add(user);
                    //            _logger.LogDebug("PostDataAsync Success add Queue TaskId={0}, ThreadId={1}, user={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email, out429);
                    //        }
                    //        catch (System.InvalidOperationException)
                    //        {
                    //            _logger.LogError("PostDataAsync Error Adding Entry to successQueue Id={0}", oktaUser.Id);
                    //        }

                    //    }
                    //}
                    //else
                    //{
                    //    if (createResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    //    {
                    //        //for 429 users
                    //        //check rate limit headers for details
                    //        string limit = createResponse.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                    //        string remaining = createResponse.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                    //        string reset = createResponse.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                    //        user.output = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;
                    //       //_inputQueue.replayQueue.TryAdd(user);
                    //        try
                    //        {
                    //            _inputQueue.replayQueue.Add(user);
                    //            _logger.LogDebug("PostDataAsync Replay add Queue TaskId={0}, ThreadId={1}, user={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email, user.output);

                    //        }
                    //        catch (System.InvalidOperationException)
                    //        {
                    //            _logger.LogError("PostDataAsync Error Adding Entry to replayQueue");
                    //        }

                    //    }
                    //    else
                    //    {
                    //        //for failed users  
                    //        content = await createResponse.Content.ReadAsStringAsync();
                    //        OktaApiError oktaApiError = Newtonsoft.Json.JsonConvert.DeserializeObject<OktaApiError>(content);
                    //        user.output = "Status=" + updateResponse.StatusCode.ToString() + ",Error=" + oktaApiError.errorCauses[0].errorSummary;
                    //        //_inputQueue.failureQueue.TryAdd(user);
                    //        //_logger.LogTrace("PostDataAsync Failure add Queue TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email);

                    //        string limit = createResponse.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                    //        string remaining = createResponse.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                    //        string reset = createResponse.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                    //        string out429 = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;

                    //        try
                    //        {
                    //            _inputQueue.failureQueue.Add(user);
                    //            _logger.LogDebug("PostDataAsync Failure add Queue TaskId={0}, ThreadId={1}, user={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email, user.output);
                    //        }
                    //        catch (System.InvalidOperationException)
                    //        {
                    //            _logger.LogError("PostDataAsync Error Adding Entry to failureQueue");
                    //        }


                    //    }
                    //}//end if createResponse
                }
                else if (getResponse.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    //for 429 users
                    Consumer_429(user, getResponse);

                    ////check rate limit headers for details
                    //string limit = getResponse.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                    //string remaining = getResponse.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                    //string reset = getResponse.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                    //user.output = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;
                    ////_inputQueue.replayQueue.TryAdd(user);
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
                    content = await getResponse.Content.ReadAsStringAsync();
                    OktaApiError oktaApiError = Newtonsoft.Json.JsonConvert.DeserializeObject<OktaApiError>(content);
                    user.output = "Status=" + getResponse.StatusCode.ToString() + ",Error=" + oktaApiError.errorCauses[0].errorSummary;
                    Consumer_Failures(user, getResponse);



                    //content = await updateResponse.Content.ReadAsStringAsync();
                    //OktaApiError oktaApiError = Newtonsoft.Json.JsonConvert.DeserializeObject<OktaApiError>(content);
                    //user.output = "Status=" + updateResponse.StatusCode.ToString() + ",Error=" + oktaApiError.errorCauses[0].errorSummary;
                    ////_inputQueue.failureQueue.TryAdd(user);
                    ////_logger.LogTrace("PostDataAsync Failure add Queue TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email);

                    //string limit = updateResponse.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                    //string remaining = updateResponse.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                    //string reset = updateResponse.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
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


                }//end if getResponse 


            }
            _logger.LogTrace("PostDataAsync Complete TaskId={0}, ThreadId={1}, user={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId,user.email);
        }

        private async Task Consumer_NewUserCreate(CustomOktaUser user, JObject profile, HttpClient client)
        {
            JObject password;
            JObject credentials;
            JObject jsonObject;
            string path;
            HttpResponseMessage createResponse;
            string content;
            HttpResponseMessage activateResponse;

            //user not found must be a create
            //finish building the JSON
            JArray groups = new JArray(_group);

            if (!string.IsNullOrEmpty(user.value))
            {
                password = new JObject(
                    new JProperty("value", user.value)
                );
                credentials = new JObject(
                    new JProperty("password", password)
                );
                jsonObject = new JObject(
                    new JProperty("profile", profile),
                    new JProperty("groupIds", groups),
                    new JProperty("credentials", credentials)
                );
            }
            else
            {
                jsonObject = new JObject(
                    new JProperty("profile", profile),
                    new JProperty("groupIds", groups)
                );
            }


            string serialJson = JsonConvert.SerializeObject(jsonObject);
            _logger.LogTrace("PostDataAsync User Json= {0}", serialJson.ToString());
            StringContent stringContent = new StringContent(serialJson, Encoding.UTF8, "application/json");

            //send API
            path = _apiUrl + "/api/v1/users?activate=" + _activateUserwithCreate;
            _logger.LogTrace("PostDataAsync send create API email={0}", user.email);
            createResponse = await client.PostAsync(path, stringContent).ConfigureAwait(false);

            if (createResponse.IsSuccessStatusCode)
            {
                if (_provisionUser)
                {
                    content = await createResponse.Content.ReadAsStringAsync();
                    BasicOktaUser oktaUser = Newtonsoft.Json.JsonConvert.DeserializeObject<BasicOktaUser>(content);
                    // create empty json payload
                    jsonObject = new JObject();
                    string serialJson1 = JsonConvert.SerializeObject(jsonObject);

                    StringContent blankContent = new StringContent(serialJson1, Encoding.UTF8, "application/json");

                    //send API
                    path = _apiUrl + "/api/v1/users/" + oktaUser.Id + "/lifecycle/activate?sendEmail=" + _sendEmailwithActivation;
                    _logger.LogTrace("PostDataAsync send activate API Id={0}", oktaUser.Id);
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
                    } //end if activateResponse
                }
                else
                {
                    _logger.LogTrace("PostDataAsync API rsp create:success, prov:false");
                    //for create users
                    content = await createResponse.Content.ReadAsStringAsync();
                    BasicOktaUser oktaUser = Newtonsoft.Json.JsonConvert.DeserializeObject<BasicOktaUser>(content);
                    user.output = "OktaId=" + oktaUser.Id;
                    Consumer_Success(user, createResponse);
                }
            }
            else
            {
                if (createResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    //for 429 users
                    Consumer_429(user, createResponse);
                }
                else
                {
                    //for failed users  
                    content = await createResponse.Content.ReadAsStringAsync();
                    OktaApiError oktaApiError = Newtonsoft.Json.JsonConvert.DeserializeObject<OktaApiError>(content);
                    user.output = "Status=" + createResponse.StatusCode.ToString() + ",Error=" + oktaApiError.errorCauses[0].errorSummary;
                    Consumer_Failures(user, createResponse);

                }
            } //end if createResponse
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
            //string content;
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

