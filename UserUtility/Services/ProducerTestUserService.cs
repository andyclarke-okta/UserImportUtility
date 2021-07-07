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
using System.Threading.Tasks;
using System.Threading;

namespace UserUtility.Services
{
    public class ProducerTestUserService : IProducerService
    {

        private readonly ILogger<IProducerService> _logger;
        private readonly IConfiguration _config;
        private readonly BlockingCollection<CustomOktaUser> _userQueue;

        int _producerTasks;
        int _numTestUsers;
        int _perTaskUsers;
        string _testUserDomain;
        string _testUserPsw;
        int _myCount = 0;
        CustomOktaUser _testUser = null;


        public ProducerTestUserService(ILogger<IProducerService> logger, IConfiguration config, UserQueue<CustomOktaUser> inputQueue)
        {
            _logger = logger;
            _config = config;
            _userQueue = inputQueue.userQueue;
            _producerTasks = _config.GetValue<int>("generalConfig:producerTasks");
            _testUserDomain = _config.GetValue<string>("testUserConfig:testUserDomain");
            _numTestUsers = _config.GetValue<int>("testUserConfig:numTestUsers");
            _testUserPsw = _config.GetValue<string>("testUserConfig:testUserPsw");

            decimal myDec = (decimal)(_numTestUsers / _producerTasks);
            _perTaskUsers = (int) Math.Round(myDec);
 

            //_testUser = new CustomOktaUser();
        }

        public async Task ProcessProducer()
        {
            //Note: parallelism is handle in Csv Library
            //only one producer task should be created
            _logger.LogInformation("ProcessTestUserProducer Start  on TaskId={1}, ThreadId={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            //await Task.Run(() => CreateTestUserData());

            Task[] producers = new Task[_producerTasks];
            for (int i = 0; i < producers.Length; i++)
            {
                producers[i] = Task.Run(() =>
                {
                    // Console.WriteLine("console ProcessConsumer(s) TaskId={0},ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                    _logger.LogInformation("SpinUp New Create Test Users Task on TaskId={0},ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                    CreateTestUserData();
                }
                    );//end Task.Run
            }//end of for


            try
            {
                await Task.WhenAll(producers);
                _userQueue.CompleteAdding();
            }
            catch (AggregateException ae)
            {
                _logger.LogError("One or more exceptions occurred:");
                foreach (var ex in ae.InnerExceptions)
                    _logger.LogError("Exception {0}: {1}", ex.GetType().Name, ex.Message);
            }
            Console.WriteLine();





            _logger.LogInformation("ProcessTestUserProducer Complete TaskId={0}, ThreadId={1}, myCount={2}" ,Task.CurrentId, Thread.CurrentThread.ManagedThreadId, _myCount);
        }


        public void CreateTestUserData()
        {
            int producerCount = 0;

            _logger.LogInformation("CreateTestUserData Start with {0} producerTasks on TaskId={1}, ThreadId={2}", _producerTasks, Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            Random rd = new Random();//Use the hash code returned by Guid.NewGuid().GetHashCode() to get the seed
            for (int i = 0; i < _perTaskUsers; i++)
            {
                _testUser = new CustomOktaUser();
                string myGuidStr = Guid.NewGuid().ToString();

                _testUser.email = "test." + myGuidStr + "@" + _testUserDomain;
                _testUser.login = _testUser.email;
                _testUser.firstName = "test";
                _testUser.lastName = myGuidStr;
                _testUser.value = _testUserPsw;

                _userQueue.Add(_testUser);
                producerCount++;
                _logger.LogTrace("CreateTestUserData add Queue TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, _testUser.email);
            }

            _myCount = _myCount + producerCount;
            //_userQueue.CompleteAdding();
            _logger.LogInformation("CreateTestUserData Complete TaskId={0}, ThreadId={1}, User_COUNT={2}, myCount={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, producerCount,_myCount);
        }


    }
}
