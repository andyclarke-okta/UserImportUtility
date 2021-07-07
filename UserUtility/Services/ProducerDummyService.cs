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
    public class ProducerDummyService : IProducerService
    {

        private readonly ILogger<IProducerService> _logger;
        private readonly IConfiguration _config;




        public ProducerDummyService(ILogger<IProducerService> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        public async Task ProcessProducer()
        {
            //dummy producer class as placeholder

            //Console.WriteLine();


            _logger.LogInformation("ProcessDummyProducer Complete");
        }

    }
}
