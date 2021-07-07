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

namespace UserUtility.Services
{
    public class ConsumerDeleteUserService : IConsumerService
    {
        private readonly ILogger<IConsumerService> _logger;

        private readonly IConfiguration _config;
        //private readonly BlockingCollection<String> _userQueue;
        private readonly UserQueue<BasicOktaUser> _inputQueue;
        private IOutputService _outputFiles;
        private int _queueWaitms;
        private int _throttleMs;
        private bool _deactivateOnly;
        private int _secondDeleteDelayMs;
        private int _consumerTasks;
        private List<string> _excludeOktaIds;
        private string _apiUrl;
        private string _apiToken;

        public ConsumerDeleteUserService(ILogger<IConsumerService> logger, IConfiguration config, UserQueue<BasicOktaUser> inputQueue, IOutputService outputFiles)
        {
            _logger = logger;
            _inputQueue = inputQueue;
            _config = config;
            _outputFiles = outputFiles;
            _queueWaitms = _config.GetValue<int>("generalConfig:queueWaitms");
            _throttleMs = _config.GetValue<int>("generalConfig:throttleMs");
            _deactivateOnly = _config.GetValue<bool>("rollbackConfig:deactivateOnly");
            _secondDeleteDelayMs = _config.GetValue<int>("rollbackConfig:secondDeleteDelayMs");
            _consumerTasks = _config.GetValue<int>("generalConfig:consumerTasks");
            _apiUrl = _config.GetValue<string>("generalConfig:org");
            _apiToken = _config.GetValue<string>("generalConfig:apiToken");
            _excludeOktaIds = _config.GetSection("rollbackConfig:excludeOktaIds").Get<List<string>>();
            if (_excludeOktaIds == null)
            {
                // add placeholder to avoid null error
                _excludeOktaIds = new List<string>();
            }
        }

        public async Task ProcessConsumer()
        {         
            _logger.LogInformation("ProcessDeleteUserConsumer Start with {0} consumerTasks on TaskId={1}, ThreadId={2}", _consumerTasks, Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            Task[] consumers = new Task[_consumerTasks];
            for (int i = 0; i < consumers.Length; i++)
            {
                consumers[i] = Task.Run(() =>
                {
                    _logger.LogTrace("SpinUp New Delete ServiceQueue Task on TaskId={0},ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

                    ServiceQueue(_inputQueue.userQueue);
                }
                    );//end Task.Run
            }//end of for


            try
            {
                //Task.WaitAll(consumers);
                await Task.WhenAll(consumers);
            }
            catch (AggregateException ae)
            {
                _logger.LogError("One or more exceptions occurred:");
                foreach (var ex in ae.InnerExceptions)
                    _logger.LogError("Exception {0}: {1}", ex.GetType().Name, ex.Message);
            }
            Console.WriteLine();
            _logger.LogInformation("ProcessDeleteUserConsumer CompleteTaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
            return;
        }//end ProcessConsumer

        public void ServiceQueue(BlockingCollection<BasicOktaUser> userQueue)
        {
            _logger.LogInformation("ServiceQueue Start New DeleteUser QueueCount={0},ThrottleMs={1}, SecondDeleteDelayMs={2}, TaskId={3}, ThreadId={4}", userQueue.Count, _throttleMs, _secondDeleteDelayMs, Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
            while (!userQueue.IsCompleted)
            {
                BasicOktaUser nextItem = null;
                try
                {
                    if (!userQueue.TryTake(out nextItem, _queueWaitms))
                    {
                        // _logger.LogTrace("ServiceQueue Take Blocked TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                    }
                    else
                    {
                        Task.WaitAll(Task.Delay(_throttleMs));
                        //Thread.Sleep(_throttleMs);
                        Console.Write("\r{0}", userQueue.Count());
                        //add check for excluded Okta Ids
                        if (_excludeOktaIds.Contains(nextItem.Id))
                        {
                            _logger.LogInformation("ServiceQueue DeleteUser Excluded QueueCount={0}, TaskId={1}, ThreadId={2}, oktaId={3}", userQueue.Count, Task.CurrentId, Thread.CurrentThread.ManagedThreadId, nextItem.Id);
                        }
                        else
                        {                            
                            _logger.LogDebug("ServiceQueue DeleteUser Take Success QueueCount={0}, TaskId={1}, ThreadId={2}, oktaId={3}", userQueue.Count, Task.CurrentId, Thread.CurrentThread.ManagedThreadId, nextItem.Id);
                            var rsp = PostDataAsync(nextItem);
                        }

                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogError("ServiceQueue DeleteUser Taking Cancelled TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                    //break;
                }
                catch (Exception ex)
                {
                    _logger.LogError("ServiceQueue DeleteUser Exp={0} TaskId={1}, ThreadId={2}", ex.ToString(), Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                }
            }
            _logger.LogDebug("ServiceQueue Complete DeleteUser TaskId={0}, ThreadId={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
        }//end ServiceQueue

        //[Benchmark]
        public async Task PostDataAsync(BasicOktaUser user)
        {
            _logger.LogTrace("PostDataAsync Start DeleteUser  TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user);
            string path = null;
            HttpResponseMessage response = null;

            //create empty json body
            JObject jsonObject = new JObject();
            var json = JsonConvert.SerializeObject(jsonObject);
            var stringContent = new StringContent(json, Encoding.UTF8, "application/json");

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("SSWS", _apiToken);

                if (_deactivateOnly)
                {
                    _logger.LogTrace("PostDataAsync try First Deactivate {0}", user.profile.login);
                    path = _apiUrl + "/api/v1/users/" + user.Id + "/lifecycle/deactivate";
                    response = await client.PostAsync(path, stringContent).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogTrace("PostDataAsync Try First Delete {0}", user.profile.login);
                    path = _apiUrl + "/api/v1/users/" + user.Id;
                    response = await client.DeleteAsync(path).ConfigureAwait(false);
                }

                if (response.IsSuccessStatusCode)
                {
                    //string output = "OktaId = " + user.Id;
                    _logger.LogDebug("PostDataAsync Success on First Deactivate/Delete {0}", user.profile.login);

                    if (!_deactivateOnly)
                    {
                        //for active users that are now deactivated users
                        //delay to allow user state change in Okta
                        Task.WaitAll(Task.Delay(_secondDeleteDelayMs));
                        path = _apiUrl + "/api/v1/users/" + user.Id;
                        HttpResponseMessage response1 = await client.DeleteAsync(path).ConfigureAwait(false);

                        if (response1.IsSuccessStatusCode || (response1.StatusCode == HttpStatusCode.NotFound))
                        {
                            string limit = response1.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                            string remaining = response1.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                            string reset = response1.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                            string out429 = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;                       

                            try
                            {
                                _inputQueue.successQueue.Add(user);
                                _logger.LogDebug("PostDataAsync Delete SUCCESS add Queue TaskId={0}, ThreadId={1}, item={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.profile.login,out429);
                            }
                            catch (System.InvalidOperationException)
                            {
                                _logger.LogError("PostDataAsync Error Adding Entry to successQueue User={0}", user.profile.login);
                            }
                        }
                        else
                        {
                            if (response1.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                            {
                                //for 429 users during delete
                                //check rate limit headers for details
                                string limit = response1.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                                string remaining = response1.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                                string reset = response1.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                                user.output = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;
                                //_inputQueue.replayQueue.TryAdd(output1);                   
                                try
                                {
                                    _inputQueue.replayQueue.Add(user);
                                    _logger.LogDebug("PostDataAsync Delete Replay add Queue TaskId={0}, ThreadId={1}, ={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.profile.login, user.output);
                                }
                                catch (System.InvalidOperationException)
                                {
                                    _logger.LogError("PostDataAsync Error Adding Entry to replayQueue");
                                }
                            }
                            else
                            {
                                //for failed users during delete
                                string content = await response1.Content.ReadAsStringAsync();
                                OktaApiError oktaApiError = Newtonsoft.Json.JsonConvert.DeserializeObject<OktaApiError>(content);
                                user.output = "Status=" + response1.StatusCode.ToString() + ",Error=" + oktaApiError.errorCauses[0].errorSummary;
 
                                string limit = response1.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                                string remaining = response1.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                                string reset = response1.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                                string out429 = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;
                                
                                try
                                {
                                    _inputQueue.failureQueue.Add(user);
                                    _logger.LogDebug("PostDataAsync Delete Failure add Queue TaskId={0}, ThreadId={1}, user={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.profile.login, user.output);
                                }
                                catch (System.InvalidOperationException)
                                {
                                    _logger.LogError("PostDataAsync Error Adding Entry to failureQueue");
                                }
                            }
                        }
                    }
                    else
                    {
                        //success path for deactivate only
                        //_inputQueue.successQueue.TryAdd(output);
                        string limit = response.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                        string remaining = response.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                        string reset = response.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                        string out429 = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;
                        
                        try
                        {
                            _inputQueue.successQueue.Add(user);
                            _logger.LogDebug("PostDataAsync Deactivate SUCCESS add Queue TaskId={0}, ThreadId={1}, user={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.profile.login,out429);
                        }
                        catch (System.InvalidOperationException)
                        {
                            _logger.LogError("PostDataAsync Error Adding Entry to successQueue");
                        }
                    }

                }
                else
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        //for rate limited users on deactivate path
                        //check rate limit headers for details
                        string limit = response.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                        string remaining = response.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                        string reset = response.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                        user.output = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;
                        //_inputQueue.replayQueue.TryAdd(output);
                        try
                        {
                            _inputQueue.replayQueue.Add(user);
                            _logger.LogDebug("PostDataAsync Deactivate Replay add Queue TaskId={0}, ThreadId={1}, user={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.profile.login, user.output);
                        }
                        catch (System.InvalidOperationException)
                        {
                            _logger.LogError("PostDataAsync Error Adding Entry to replayQueue");
                        }

                    }
                    else
                    {
                        //for failed users on deactivate path
                        string content = await response.Content.ReadAsStringAsync();
                        OktaApiError oktaApiError = Newtonsoft.Json.JsonConvert.DeserializeObject<OktaApiError>(content);
                        user.output = "Status=" + response.StatusCode.ToString() + ",Error=" + oktaApiError.errorCauses[0].errorSummary;
                        //_inputQueue.failureQueue.TryAdd(output);
                        string limit = response.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
                        string remaining = response.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
                        string reset = response.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
                        string out429 = "Limit=" + limit + ", Remaining=" + remaining + ", Reset=" + reset;

                        try
                        {
                            _inputQueue.failureQueue.Add(user);
                            _logger.LogDebug("PostDataAsync Deactivate Failure add Queue TaskId={0}, ThreadId={1}, user={2},failure={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.profile.login, user.output);
                        }
                        catch (System.InvalidOperationException)
                        {
                            _logger.LogError("PostDataAsync Error Adding Entry to failureQueue");
                        }

                    }
                }
            }
             _logger.LogTrace("PostDataAsync Complete TaskId={0}, ThreadId={1}, user={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId,user.profile.login);
        }

    }
}
