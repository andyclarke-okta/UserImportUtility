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
    public class ProduceOktaApiService : IProducerService
    {

        private readonly ILogger<IProducerService> _logger;
        private readonly IConfiguration _config;
        private BlockingCollection<BasicOktaUser> _userQueue;
       

        public ProduceOktaApiService(ILogger<IProducerService> logger, IConfiguration config, UserQueue<BasicOktaUser> inputQueue)
        {
            _logger = logger;
            _config = config;
            _userQueue = inputQueue.userQueue;
        }

        public async Task ProcessProducer()
        {
            //Note: only can fire Sql request to get data
            //only one producer task should be created
            _logger.LogInformation("ProduceOktaApiService Start on TaskId={1}, ThreadId={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
            
            await Task.Run(() => GetApiDataAsync());

            _logger.LogInformation("ProduceOktaApiService Complete TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
        }

        public async Task GetApiDataAsync()
        {
            int producerCount = 0;
            _logger.LogDebug("GetApiDataAsync Start  TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
            string apiUrl = _config.GetValue<string>("generalConfig:org");
            string apiToken = _config.GetValue<string>("generalConfig:apiToken");
            int throttleMs = _config.GetValue<int>("generalConfig:throttleMs");
            string endpoint = _config.GetValue<string>("userApiConfig:endpoint");
            string searchcriteria = _config.GetValue<string>("userApiConfig:searchcriteria");
            string groupId = _config.GetValue<string>("userApiConfig:groupId");
            int apiPageSize = _config.GetValue<int>("userApiConfig:apiPageSize");
            int numTestUsers = _config.GetValue<int>("testUserConfig:numTestUsers");
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
                        switch (endpoint)
                        {
                            case "allUsers":
                                path = new Uri(apiUrl + "/api/v1/users?limit=" + apiPageSize);
                                break;
                            case "searchUsers":

                                //urlencoding
                                string encodedSearchcriteria = System.Web.HttpUtility.UrlEncode(searchcriteria);

                                path = new Uri(apiUrl + "/api/v1/users?search=" + encodedSearchcriteria + "&limit=" + apiPageSize);
                                break;
                            case "groups":
                                path = new Uri(apiUrl + "/api/v1/groups/" + groupId + "/users?limit=" + apiPageSize);
                                break;
                            default:
                                path = new Uri(apiUrl + "/api/v1/groups/" + groupId + "/users?limit=" + apiPageSize);
                                break;
                        }


                    }
                    else
                    {
                        path = myNextPage;
                    }
                
                    //using (HttpClient client = new HttpClient())
                    //{
                        _logger.LogDebug("ProduceOktaApiService Send api path {0}", path);
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
                            _logger.LogDebug("ProduceOktaApiService API content received");
                            List<BasicOktaUser> listOktaUser = Newtonsoft.Json.JsonConvert.DeserializeObject<List<BasicOktaUser>>(content);
                            foreach (var item in listOktaUser)
                            {
                                if (_userQueue.Count <= numTestUsers)
                                {
                                    _userQueue.TryAdd(item, 1000);
                                    producerCount++;
                                    _logger.LogTrace("ProduceOktaApiService add Queue TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                                }

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
                            _logger.LogTrace("ProduceOktaApiService ProcessPagination isThisLastPage={0}", isThisLastPage);
                        }
                        else
                        {
                            //for failed GET
                            content = await response.Content.ReadAsStringAsync();
                            OktaApiError oktaApiError = Newtonsoft.Json.JsonConvert.DeserializeObject<OktaApiError>(content);
                            _logger.LogError("ProduceOktaApiService Status={0},Error={1}", response.StatusCode.ToString(), oktaApiError.errorCauses[0].errorSummary);
                        }
                    
                    //}//end using
                } while (!(isThisLastPage || (_userQueue.Count > numTestUsers)));

            }//end using

            _userQueue.CompleteAdding();
            _logger.LogInformation("ProduceOktaApiService Complete TaskId={0}, ThreadId={1}, User_COUNT={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, producerCount);
        }
    }
}
