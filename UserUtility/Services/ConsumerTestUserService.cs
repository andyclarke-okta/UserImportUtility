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


    public class ConsumerTestUserService : IConsumerService
    {

        private readonly ILogger<IConsumerService> _logger;

        private readonly IConfiguration _config;

        private int _queueWaitms;
        private int _throttleMs;
        private string _apiUrl;
        private string _apiToken;
        private string _group;
        private int _consumerTasks;

        private string _activateUserwithCreate;
        private bool _provisionUser;
        private string _sendEmailwithActivation;
        private int _workFactor;
        private string _algorithm;
        private string _saltOrder;
        private List<string> _omitFromUserProfile;
        private bool _createInGroup;

        int _numTestUsers;
        int _perTaskUsers;
        string _testUserDomain;
        string _testUserPsw;
        //CustomOktaUser _testUser = null;

        int _myCount = 0;
        int _successCount = 0;
        int _429Count = 0;
        int _failureCount = 0;

        public ConsumerTestUserService(ILogger<IConsumerService> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
    
            _queueWaitms = _config.GetValue<int>("generalConfig:queueWaitms");
            _throttleMs = _config.GetValue<int>("generalConfig:throttleMs");
            _group = _config.GetValue<string>("importConfig:groupId");
            _apiUrl = _config.GetValue<string>("generalConfig:org");
            _apiToken = _config.GetValue<string>("generalConfig:apiToken");
            _consumerTasks = _config.GetValue<int>("generalConfig:consumerTasks");

            _activateUserwithCreate = _config.GetValue<string>("importConfig:activateUserwithCreate");
            _provisionUser = _config.GetValue<bool>("importConfig:provisionUser");
            _sendEmailwithActivation = _config.GetValue<string>("importConfig:sendEmailwithActivation");
            _workFactor = _config.GetValue<int>("importConfig:workFactor");
            _algorithm = _config.GetValue<string>("importConfig:algorithm");
            _saltOrder = _config.GetValue<string>("importConfig:saltOrder");
            _omitFromUserProfile = _config.GetSection("importConfig:omitFromUserProfile").Get<List<string>>();
            _createInGroup = _config.GetValue<bool>("importConfig:createInGroup");

            _testUserDomain = _config.GetValue<string>("testUserConfig:testUserDomain");
            _numTestUsers = _config.GetValue<int>("testUserConfig:numTestUsers");
            _testUserPsw = _config.GetValue<string>("testUserConfig:testUserPsw");

            decimal myDec = (decimal)(_numTestUsers / _consumerTasks);
            _perTaskUsers = (int)Math.Round(myDec);

            //_testUser = new CustomOktaUser();


        }

        public async Task ProcessConsumer()
        {
            _logger.LogInformation("ProcessTestUserConsumer Start  with {0} consumerTasks on TaskId={1}, ThreadId={2}", _consumerTasks, Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            Task[] consumers = new Task[_consumerTasks];
            for (int i = 0; i < consumers.Length; i++)
            {
                consumers[i] = Task.Run(() =>
                {
                    // Console.WriteLine("console ProcessConsumer(s) TaskId={0},ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                    _logger.LogTrace("SpinUp New Create ServiceQueue Task on TaskId={0},ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                
                    ServiceQueue(_perTaskUsers);
                }
                    );//end Task.Run
            }//end of for


            try
            {
                 Task.WaitAll(consumers);
            }
            catch (AggregateException ae)
            {
                _logger.LogError("One or more exceptions occurred:");
                foreach (var ex in ae.InnerExceptions)
                    _logger.LogError("Exception {0}: {1}", ex.GetType().Name, ex.Message);
            }
            Console.WriteLine();
            _logger.LogInformation("ProcessTestUserConsumer Complete TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
            _logger.LogInformation("ProcessTestUserConsumer Results totalCount={0}, successCount={1}, 429Count={2}, failureCount={3}", _myCount,_successCount,_429Count,_failureCount);
            return;
        }//end Process Consumer

        public void ServiceQueue(int perTaskUsers)
        {
            int spinnerCount = 0;
            _logger.LogInformation("ServiceQueue Start New TestUser ThrottleMs={0},TaskId={1}, ThreadId={2}",  _throttleMs, Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            //for async based approach
            var tasks = new Task[perTaskUsers];

            for (int i = 0; i < perTaskUsers; i++)
            {
                //for blocking approach
                //_testUser.email = null;
                //_testUser.login = null;
                //_testUser.firstName = null;
                //_testUser.lastName = null;
                //_testUser.value = null;

                //for async based approach
                CustomOktaUser _testUser = new CustomOktaUser();



                string myGuidStr = Guid.NewGuid().ToString();

                _testUser.email = "test." + myGuidStr + "@" + _testUserDomain;
                _testUser.login = _testUser.email;
                _testUser.firstName = "test";
                _testUser.lastName = myGuidStr;
                _testUser.value = _testUserPsw;

                spinnerCount++;
                Console.Write("\r{0}", spinnerCount);
                Task.WaitAll(Task.Delay(_throttleMs));
                _logger.LogTrace("ServiceQueue TestUser object created  TaskId={0}, ThreadId={1}, email={2}",  Task.CurrentId, Thread.CurrentThread.ManagedThreadId, _testUser.email);
 
                //for blocking approach
                //var rsp = PostDataAsync(_testUser);

                //for async based approach
                tasks[i]  = Task.Run(() =>PostDataAsync(_testUser));
                _logger.LogTrace("ServiceQueue TestUser return PostDataAsync  TaskId={0}, ThreadId={1}, item={2}",   Task.CurrentId, Thread.CurrentThread.ManagedThreadId, _testUser.email);

            }

            _myCount = _myCount + spinnerCount;

            //for async based approach
            Task.WaitAll(tasks);
            _logger.LogInformation("ServiceQueue Complete TaskId={0}, ThreadId={1}, spinnerCOUNT={2}, myCount={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, spinnerCount, _myCount);
        }//end ServiceQueue




     
        public async Task PostDataAsync(CustomOktaUser user)
        {
            _logger.LogTrace("PostDataAsync Start TestUser TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email);


            string content = null;
            //HttpResponseMessage activateResponse = null;

            //build Okta profile attributes from class
            var properties = typeof(CustomOktaUser).GetProperties().Where(n => n.PropertyType == typeof(string) || n.PropertyType == typeof(List<string>));

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

                //this is cleartext password in file
                password = new JObject(
                    new JProperty("value", user.value)
                    );
                credentials = new JObject(
                    new JProperty("password", password)
                    );


            if (_createInGroup)
            {
                //password provided add credentials and groupId
                jsonObject = new JObject(
                        new JProperty("profile", profile),
                        new JProperty("groupIds", groups),
                        new JProperty("credentials", credentials)
                    );
            }
            else
            {
                //password provided add credentials onlly
                jsonObject = new JObject(
                        new JProperty("profile", profile),
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

                //for blocking approach
                //HttpResponseMessage createResponse =  client.PostAsync(path, stringContent).Result;

                //for async based approach 
                HttpResponseMessage createResponse = await client.PostAsync(path, stringContent).ConfigureAwait(false);




                //two flows; create with password or create withoutpassword then activate without email
                if (createResponse.IsSuccessStatusCode)
                {
                        _logger.LogTrace("PostDataAsync API rsp create:success, prov:false");
                    _successCount = _successCount + 1;
                }
                else
                {
                    _logger.LogTrace("PostDataAsync API rsp create:NOT success, status_code={0}", createResponse.StatusCode);
                    if (createResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        //429 response
                        _logger.LogTrace("PostDataAsync API rsp create: replay, prov:false");
                        _429Count = _429Count + 1;
                    }
                    else
                    {
                        //failure response
                        _logger.LogTrace("PostDataAsync API rsp create:failure, prov:false");
                        _failureCount = _failureCount + 1;

                        //for failed users  
                        content = createResponse.Content.ReadAsStringAsync().Result;
                        OktaApiError oktaApiError = Newtonsoft.Json.JsonConvert.DeserializeObject<OktaApiError>(content);
                        //user.output = "Status=" + response.StatusCode.ToString() + ",Error=" + oktaApiError.errorCauses[0].errorSummary;
                        _logger.LogDebug("PostDataAsync Failure email{0}, error={1}", user.email, oktaApiError.errorCauses[0].errorSummary);
                    }
                }//end create not success

            }
            _logger.LogTrace("PostDataAsync  Complete TaskId={0}, ThreadId={1}, user={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email);
        }

      


    }

}


