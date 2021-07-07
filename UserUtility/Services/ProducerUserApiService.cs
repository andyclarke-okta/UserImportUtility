using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using UserUtility.Models;
using TinyCsvParser;
using System.Linq;
using TinyCsvParser.Mapping;
using Microsoft.Extensions.Configuration;
using System.IO;
using CsvHelper;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Threading;

namespace UserUtility.Services
{
    public class ProduceUserApiService : IProducerService
    {

        private readonly ILogger<IProducerService> _logger;
        private readonly IConfiguration _config;
        private BlockingCollection<BasicOktaUser> _userQueue;
       

        public ProduceUserApiService(ILogger<IProducerService> logger, IConfiguration config, UserQueue<BasicOktaUser> inputQueue)
        {
            _logger = logger;
            _config = config;
            _userQueue = inputQueue.userQueue;
        }

        public async Task ProcessProducer()
        {
            //Note: only can fire Sql request to get data
            //only one producer task should be created
            _logger.LogInformation("ProduceUserApiService Start on TaskId={1}, ThreadId={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
            
            await Task.Run(() => GetApiDataAsync());

            _logger.LogInformation("ProduceUserApiService Complete TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
        }

        public async Task GetApiDataAsync()
        {
            int producerCount = 0;
            _logger.LogDebug("GetApiDataAsync Start  TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
            string apiUrl = _config.GetValue<string>("generalConfig:org");
            string apiToken = _config.GetValue<string>("generalConfig:apiToken");
            int throttleMs = _config.GetValue<int>("generalConfig:throttleMs");
            string endpoint = _config.GetValue<string>("userApiConfig:endpoint");
            string groupId = _config.GetValue<string>("userApiConfig:groupId");
            int apiPageSize = _config.GetValue<int>("userApiConfig:apiPageSize");
            ApiHelper apiHelper = new ApiHelper();
            string content = null;
            bool isThisLastPage = true;
            Uri myNextPage = null;
            PagedResultHeader apiHeaderResults = null;
            Uri path = null;

            using (HttpClient client = new HttpClient())
            {


                do
                {
                    if (apiHeaderResults == null)
                    {
                        //initialize path
                        if (endpoint == "users")
                        {
                            path = new Uri(apiUrl + "/api/v1/users?limit=" + apiPageSize);
                        }
                        else
                        {
                            path = new Uri(apiUrl + "/api/v1/groups/" + groupId + "/users?limit=" + apiPageSize);
                        }


                    }
                    else
                    {
                        path = myNextPage;
                    }
                
                    //using (HttpClient client = new HttpClient())
                    //{
                        _logger.LogDebug("ProduceUserApiService Send api path {0}", path);
                        client.DefaultRequestHeaders.Accept.Clear();
                        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("SSWS", apiToken);
                        //HttpResponseMessage response = await client.GetAsync(path);
                        HttpResponseMessage response = client.GetAsync(path).Result;

                    Task.WaitAll(Task.Delay(throttleMs));

                    if (response.IsSuccessStatusCode)
                        {
                            //Handle paged user content
                            //content = await response.Content.ReadAsStringAsync();
                            content = response.Content.ReadAsStringAsync().Result;
                            _logger.LogDebug("ProduceUserApiService API content received");
                            List<BasicOktaUser> listOktaUser = Newtonsoft.Json.JsonConvert.DeserializeObject<List<BasicOktaUser>>(content);
                            foreach (var item in listOktaUser)
                            {

                            //CustomOktaUser inputUser = new CustomOktaUser();
                            //inputUser.login = item.profile.login;
                            //inputUser.email = item.profile.email;
                            //inputUser.firstName = item.profile.firstName;
                            //inputUser.lastName = item.profile.lastName;
                            //inputUser.output = item.Status + "," + item.Id;

                            _userQueue.TryAdd(item, 1000);
                                producerCount++;
                                _logger.LogTrace("ProduceUserApiService add Queue TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, item.Id);
                            }
                            bool isContentNull = false;
                            if (listOktaUser == null)
                            {
                                isContentNull = true;
                            }

                            //handle paged user header info
                            apiHeaderResults = apiHelper.GetHeaderInfo(response, isContentNull);
                            isThisLastPage = apiHeaderResults.IsLastPage;
                            myNextPage = apiHeaderResults.NextPage;
                            _logger.LogTrace("ProduceUserApiService GetUserByGroup isThisLastPage={0}", isThisLastPage);
                        }
                        else
                        {
                            //for failed GET
                            content = await response.Content.ReadAsStringAsync();
                            OktaApiError oktaApiError = Newtonsoft.Json.JsonConvert.DeserializeObject<OktaApiError>(content);
                            _logger.LogError("ProduceUserApiService Status={0},Error={1}", response.StatusCode.ToString(), oktaApiError.errorCauses[0].errorSummary);
                        }
                    
                    //}//end using
                } while (!isThisLastPage);

            }//end using

            _userQueue.CompleteAdding();
            _logger.LogInformation("ProduceUserApiService Complete TaskId={0}, ThreadId={1}, User_COUNT={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, producerCount);
        }
    }
}
