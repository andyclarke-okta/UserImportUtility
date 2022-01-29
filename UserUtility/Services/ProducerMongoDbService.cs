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
using System.Data.SqlClient;
using System.Data;
using MongoDB.Driver;

namespace UserUtility.Services
{
    public class ProducerMongoDbService : IProducerService
    {

        private readonly ILogger<IProducerService> _logger;
        private readonly IConfiguration _config;
        private readonly BlockingCollection<CustomOktaUser> _userQueue;
    
        private IMongoCollection<MongoDbUsers> _mongoCollection;
        bool _isLoginEqualEmail;
        bool _isComboHashSalt;

        public ProducerMongoDbService(ILogger<IProducerService> logger, IConfiguration config, UserQueue<CustomOktaUser> inputQueue)
        {
            _logger = logger;
            _config = config;
            _userQueue = inputQueue.userQueue;
            _isLoginEqualEmail = _config.GetValue<bool>("importConfig:isLoginEqualEmail");
            _isComboHashSalt = _config.GetValue<bool>("importConfig:isComboHashSalt");
            //setup MongoDb 
            var mongoUri = _config.GetValue<string>("mongoDbConfig:uri");
            var mongodatabase = _config.GetValue<string>("mongoDbConfig:database");
            var mongocollection = _config.GetValue<string>("mongoDbConfig:collection");

            IMongoClient mongoClient = new MongoClient(mongoUri);
            IMongoDatabase mongoDatabase = mongoClient.GetDatabase(mongodatabase);
            _mongoCollection = mongoDatabase.GetCollection<MongoDbUsers>(mongocollection);

        }

        public async Task ProcessProducer()
        {
            //Note: only one SQL request should be executed
            //only one producer task should be created
           // _logger.LogInformation("Start ProcessMongoDBProducer  on Task={0}, Thread={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            await Task.Run(() => GetDbData());

            //_logger.LogInformation("Complete ProcessMongoDBProducer Task={0}, Thread={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
        }


        public void GetDbData()
        {
            int producerCount = 0;
            _logger.LogInformation("Start ProcessMongoDBProducer  on Task={0}, Thread={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            List<MongoDbUsers> myUsers = _mongoCollection.Find(f => true).ToList();


            foreach (var item in myUsers)
            {
                _logger.LogTrace("ProcessMongoDBProducer add user to queue email={0}", item.email);


               CustomOktaUser newUser = new CustomOktaUser()
                {
                    email = item.email,
                    firstName = item.firstName ,
                    lastName = item.lastName,
                    //identityAssuranceLevel = item.identityAssuranceLevel,
                    value = item.password,
                    salt = "none"
                };

 

                if (_isLoginEqualEmail)
                {
                    newUser.login = newUser.email;
                }


                if (_isComboHashSalt)
                {
                    _logger.LogTrace("ProcessMongoDBProducer ComboHashSalt for {0} orig value {1}", newUser.email, newUser.value);
                    int saltLength = 22;
                    int hashLength = 31;

                    //var index = newUser.value.IndexOf("$2a$10$");
                    string myValue = newUser.value.Substring(7);

                    string salt = myValue.Substring(0,saltLength);
                    string value = myValue.Substring(saltLength, hashLength);                 

                    _logger.LogTrace("ProcessMongoDBProducer Splits for {0}; hash {1}, salt {2}", newUser.login, value, salt);
                    newUser.value = value;
                    newUser.salt = salt;
                }

                _userQueue.Add(newUser);
                producerCount++;
                _logger.LogTrace("ProcessMongoDBProducer add Queue Task={0}, Thread={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, newUser.email);
                newUser = null;
            }

            _userQueue.CompleteAdding();
            _logger.LogInformation("Complete ProcessMongoDBProducer Task={0}, Thread={1}, count={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, producerCount);
        }
    }
}
