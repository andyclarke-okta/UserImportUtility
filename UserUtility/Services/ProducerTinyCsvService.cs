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
    public class ProducerTinyCsvService : IProducerService
    {

        private readonly ILogger<IProducerService> _logger;
        private readonly IConfiguration _config;
        private readonly BlockingCollection<CustomOktaUser> _userQueue;
        private readonly List<CustomOktaUser> _staticUserQueue;
        int _producerTasks;
        bool _isValidation;
        bool _isCustomInputLogic;
        bool _isLoginEqualEmail;

        bool _isComboHashSalt;
        char _inputFileFieldSeperator;
        

        public ProducerTinyCsvService(ILogger<IProducerService> logger, IConfiguration config, UserQueue<CustomOktaUser> inputQueue)
        {
            _logger = logger;
            _config = config;
            _userQueue = inputQueue.userQueue;
            _staticUserQueue = inputQueue.staticUserQueue;
            _producerTasks = _config.GetValue<int>("generalConfig:producerTasks");
            _isValidation = _config.GetValue<bool>("generalConfig:isValidation");
            _isCustomInputLogic = _config.GetValue<bool>("importConfig:isCustomInputLogic");
            _isLoginEqualEmail = _config.GetValue<bool>("importConfig:isLoginEqualEmail");
            _isComboHashSalt = _config.GetValue<bool>("importConfig:isComboHashSalt");
            _inputFileFieldSeperator = _config.GetValue<char>("importConfig:inputFileFieldSeperator");
            
        }

        public async Task ProcessProducer()
        {
            //Note: parallelism is handle in Csv Library
            //only one producer task should be created
            _logger.LogInformation("ProcessCsvProducer Start  on TaskId={1}, ThreadId={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            await Task.Run(() => GetCsvData());

            _logger.LogInformation("ProcessCsvProducer Complete TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
        }

        public void GetCsvData()
        {
            int producerCount = 0;
            //int producerTasks = _config.GetValue<int>("generalConfig:producerTasks");
            //bool isValidation = _config.GetValue<bool>("generalConfig:isValidation");
            //bool isLoginEqualEmail = _config.GetValue<bool>("importConfig:isLoginEqualEmail");

            _logger.LogInformation("GetCsvData Start with {0} producerTasks on TaskId={1}, ThreadId={2}", _producerTasks, Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            if (_isValidation)
            {
                _logger.LogInformation("GetCsvData Fill Static User Queue");
            }

            var csvFile = _config.GetValue<string>("inputFile");
            CsvParserOptions csvParserOptions = new CsvParserOptions(skipHeader: true, fieldsSeparator: _inputFileFieldSeperator , degreeOfParallelism: _producerTasks, keepOrder: false);
            CsvHeaderMapping csvMapper = new CsvHeaderMapping(_config);
            CsvParser<CustomOktaUser> csvParser = new CsvParser<CustomOktaUser>(csvParserOptions, csvMapper);

            var records = csvParser
                .ReadFromFile(@csvFile, Encoding.ASCII);
            var results = records.Select(x => x.Result).ToList<CustomOktaUser>();

            foreach (var item in results)
            {
                
                if (_isCustomInputLogic)
                {
                    //use this code block to implement customer specific transformations of input fields
                    _logger.LogTrace("GetCsvData isCustomInputLogic is true" );

                    if ((item.email != null) && ( item.email.IndexOf("@") > -1 ))
                    {
                        _logger.LogTrace("GetCsvData email is present");
                    }
                    else 
                    {
                        item.email = item.login + "@t-mobile.com";
                        _logger.LogTrace("GetCsvData created email {0}", item.email);
                    }
                }

                if (_isLoginEqualEmail)
                {
                    item.login = item.email;
                }

                if (_isComboHashSalt)
                {
                    _logger.LogTrace("GetCsvData ComboHashSalt for {0} orig value {1}", item.login,item.value);

                    var index = item.value.IndexOf("}");
                    string myValue = item.value.Substring(index+1);

                    byte[] comboValue = System.Convert.FromBase64String(myValue);

                    int arrayLength = comboValue.Length;
                    int saltLength = 8;
                    int hashLength = 20;

                    byte[] saltOutput;
                    saltOutput = new byte[saltLength];

                    byte[] hashOutput;
                    hashOutput = new byte[hashLength];

                    Array.Copy(comboValue, arrayLength - saltLength, saltOutput, 0, saltLength);
                    Array.Copy(comboValue, 0, hashOutput, 0, hashLength);

                    string value = Convert.ToBase64String(hashOutput);
                    string salt = Convert.ToBase64String(saltOutput);

                    _logger.LogTrace("GetCsvData Splits for {0}; hash {1}, salt {2}", item.login, value, salt);
                    item.value = value;
                    item.salt = salt;

                }

                if (_isValidation)
                {
                    //add to static list
                    _staticUserQueue.Add(item);
                }
                _userQueue.Add(item);
                producerCount++;
                _logger.LogTrace("GetCsvData add Queue TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, item.email);
            }
            _userQueue.CompleteAdding();
            _logger.LogInformation("GetCsvData Complete TaskId={0}, ThreadId={1}, User_COUNT={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, producerCount);
        }



    }
}
