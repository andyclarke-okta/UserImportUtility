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

    public class ProducerCsvHelperService : IProducerService
    {

        private readonly ILogger<IProducerService> _logger;
        private readonly IConfiguration _config;
        private BlockingCollection<CustomOktaUser> _userQueue;


        public ProducerCsvHelperService(ILogger<IProducerService> logger, IConfiguration config, UserQueue<CustomOktaUser> inputQueue)
        {
            _logger = logger;
            _config = config;
            _userQueue = inputQueue.userQueue;
        }

        public async Task ProcessProducer()
        {
            //Note: parallelism is handle in Csv Library
            //only one producer task should be created
            _logger.LogInformation("ProcessCsvProducer Start on TaskId={1}, ThreadId={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            await Task.Run(() => GetCsvData());

            _logger.LogInformation("ProcessCsvProducer Complete TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
        }



        public void GetCsvData()
        {
            int producerCount = 0;
            int producerTasks = _config.GetValue<int>("generalConfig:producerTasks");
            _logger.LogInformation("GetCsvEntries Start with {0} producerTasks on TaskId={1}, ThreadId={2}", producerTasks, Task.CurrentId, Thread.CurrentThread.ManagedThreadId);


            var csvFile = _config.GetValue<string>("inputFile");

            TextReader reader = new StreamReader(@csvFile);
            var csvReader = new CsvReader(reader);
            var records = csvReader.GetRecords<CustomOktaUser>();
            var results = records.ToList();

            foreach (var item in results)
            {
                _userQueue.Add(item);
                producerCount++;
                _logger.LogTrace("GetCsvEntries add Queue TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, item.email);
            }
            _userQueue.CompleteAdding();
            _logger.LogInformation("GetCsvEntries Complete  TaskId={0}, ThreadId={1}, count={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, producerCount);
        }

    }

}
