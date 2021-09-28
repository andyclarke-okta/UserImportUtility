using UserUtility.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;

namespace UserUtility.Services
{
    public class OutputCountService : IOutputService
    {

        private readonly ILogger<IOutputService> _logger;
        private readonly IConfiguration _config;
        private int _cleanUpWaitms;

        private int outputSuccessCount = 0;
        private int outputFailureCount = 0;
        private int outputReplayCount = 0;

        public OutputCountService(ILogger<IOutputService> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
            _cleanUpWaitms = _config.GetValue<int>("generalConfig:cleanUpWaitms");

           
        }

        public void ConfigOutput()
        {

            _logger.LogInformation("OutputCountService Config Complete");

            return;
        }

        public async Task ProcessOutput()
        {
            //dummy output class as placeholder

            Console.WriteLine();
            //add delay to allow async API responses to be processed
            Task.WaitAll(Task.Delay(_cleanUpWaitms));
            _logger.LogInformation("OutputCountService Complete successCount {0}, failureCout {1}, replayCount{2} ", outputSuccessCount, outputFailureCount, outputReplayCount);
            return;
        }

        public void IncrementSuccessCount()
        {
            outputSuccessCount++;
        }

        public void IncrementFailureCount()
        {
            outputFailureCount++;
        }

        public void IncrementReplayCount()
        {
            outputReplayCount++;
        }
    }
}
