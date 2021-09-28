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


    public class ConsumerUpdateTestUserService : IConsumerService
    {

        private readonly ILogger<IConsumerService> _logger;
 
        private readonly IConfiguration _config;
        private readonly UserQueue<BasicOktaUser> _inputQueue;
        private IOutputService _outputService;
        private int _queueWaitms;
        private int _throttleMs;

        private string _apiUrl;
        private string _apiToken;
        private string _group;
        private string _activateUserwithCreate;
        private bool _provisionUser;
        private string _sendEmailwithActivation;
        List<string> _omitFromUserProfile;
        private Dictionary<string, string> _addionalAttributes;
        private int _consumerTasks;

        //int _myCount = 0;


      
        public ConsumerUpdateTestUserService(ILogger<IConsumerService> logger, IConfiguration config, UserQueue<BasicOktaUser> inputQueue, IOutputService outputService)
        {
            _logger = logger;
            _inputQueue = inputQueue;
            _config = config;
            _outputService = outputService;
            _queueWaitms = _config.GetValue<int>("generalConfig:queueWaitms");
            _throttleMs = _config.GetValue<int>("generalConfig:throttleMs");

            _apiUrl = _config.GetValue<string>("generalConfig:org");
            _apiToken = _config.GetValue<string>("generalConfig:apiToken");
            _group = _config.GetValue<string>("importConfig:groupId");
            _activateUserwithCreate = _config.GetValue<string>("importConfig:activateUserwithCreate");
            _provisionUser = _config.GetValue<bool>("importConfig:provisionUser");
            _sendEmailwithActivation = _config.GetValue<string>("importConfig:sendEmailwithActivation");
            _omitFromUserProfile = _config.GetSection("importConfig:omitFromUserProfile").Get<List<string>>();
            _addionalAttributes = _config.GetSection("testUserConfig:addionalAttributes").GetChildren().ToDictionary(x => x.Key, x => x.Value);
            _consumerTasks = _config.GetValue<int>("generalConfig:consumerTasks");
        }

        public async Task ProcessConsumer()
        {
            
            _logger.LogInformation("ConsumerUpdateTestUserService Start with {0} consumerTasks on TaskId={1}, ThreadId={2}", _consumerTasks, Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            Task[] consumers = new Task[_consumerTasks];
            for (int i = 0; i < consumers.Length; i++)
            {
                consumers[i] = Task.Run(() =>
                        {
                            _logger.LogTrace("SpinUp New UpdateTestUser ServiceQueue Task on TaskId={0},ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
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
            _logger.LogInformation("ConsumerUpdateTestUserService  Complete TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);         
            return;
        }//end Process Consumer

        public void ServiceQueue(BlockingCollection<BasicOktaUser> userQueue)
        {
            _logger.LogInformation("ServiceQueue Start New UpdateTestUser QueueCount={0},ThrottleMs={1},TaskId={2}, ThreadId={3}", userQueue.Count, _throttleMs, Task.CurrentId, Thread.CurrentThread.ManagedThreadId);


            while (!userQueue.IsCompleted)
            {
                BasicOktaUser nextItem = null;
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
                        _logger.LogDebug("ServiceQueue UpdateTestUser Take Success QueueCount={0}, TaskId={1}, ThreadId={2}, item={3}", userQueue.Count, Task.CurrentId, Thread.CurrentThread.ManagedThreadId, nextItem.Id);
                        var rsp = PostDataAsync(nextItem);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogError("ServiceQueue  UpdateTestUser Taking Cancelled TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                    //break;
                }
                catch (Exception ex)
                {
                    _logger.LogError("ServiceQueue UpdateTestUser Exp={0} TaskId={1}, ThreadId={2}", ex.ToString(), Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                }
            }//end while


            _logger.LogDebug("ServiceQueue Complete UpdateTestUser TaskId={0}, ThreadId={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
        }//end ServiceQueue

     
        public async Task PostDataAsync(BasicOktaUser user)
        {
            _logger.LogTrace("ConsumerUpdateTestUserService PostDataAsync Start TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.Id);
            string path = null;
            string content = null;
            HttpResponseMessage updateResponse = null;
            //HttpResponseMessage createResponse = null;
            //HttpResponseMessage activateResponse = null;
            JObject jsonObject = null;
            //JObject credentials = null;
            //JObject password = null;
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
                string myValue;
                if (_addionalAttributes.TryGetValue(property.Name, out myValue))
                {
                    writer.WritePropertyName(property.Name);
                    writer.WriteValue(myValue);
                }
            }
            writer.WriteEndObject();
            profile = (JObject)writer.Token;



            


            using (HttpClient client = new HttpClient())
            {                         
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("SSWS", _apiToken);
 
                jsonObject = new JObject(
                    new JProperty("profile", profile)
                );
                    


                string serialJson = Newtonsoft.Json.JsonConvert.SerializeObject(jsonObject);
                //_logger.LogTrace("User Json= {0}", serialJson.ToString());
                StringContent stringContent = new StringContent(serialJson, Encoding.UTF8, "application/json");

                //send API
                path = _apiUrl + "/api/v1/users/" + user.Id;
                _logger.LogTrace("ConsumerUpdateTestUserService send update API for {0} {1}", user.Id, path);
                updateResponse = await client.PostAsync(path, stringContent).ConfigureAwait(false);

                //var updateResponse = client.PostAsync(path, stringContent).Result;

                if (updateResponse.IsSuccessStatusCode)
                {

                    //content = await updateResponse.Content.ReadAsStringAsync();
                    //BasicOktaUser oktaUser = Newtonsoft.Json.JsonConvert.DeserializeObject<BasicOktaUser>(content);
                    //user.output = "successful update on OktaId=" + oktaUser.Id;
                    _outputService.IncrementSuccessCount();
                    _logger.LogTrace("ConsumerUpdateTestUserService PostDataAsync Update Success TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId,user.Id);
                }
                else
                {
                    if (updateResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        //for 429 users             
                        //check rate limit headers for details
                        string limit = updateResponse.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                        string remaining = updateResponse.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                        string reset = updateResponse.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                        user.output = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;
                        _outputService.IncrementReplayCount();
                        _logger.LogDebug("ConsumerUpdateTestUserService PostDataAsync Update Replay TaskId={0}, ThreadId={1}, user={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.Id, user.output);
                            
                    }
                    else
                    {
                        //for failed users  
                        content = await updateResponse.Content.ReadAsStringAsync();
                        OktaApiError oktaApiError = Newtonsoft.Json.JsonConvert.DeserializeObject<OktaApiError>(content);
                        user.output = "Status=" + updateResponse.StatusCode.ToString() + ",Error=" + oktaApiError.errorCauses[0].errorSummary;

                        _outputService.IncrementFailureCount();
                        _logger.LogDebug("ConsumerUpdateTestUserService PostDataAsync Failure add Queue TaskId={0}, ThreadId={1}, user={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.Id, user.output);                         
                    }
                }


            } //httpClient
            _logger.LogTrace("ConsumerUpdateTestUserService PostDataAsync Complete TaskId={0}, ThreadId={1}, user={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId,user.Id);
        } //PostDataAsync

    }

}

