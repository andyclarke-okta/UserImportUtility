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
    public class OutputDummyService : IOutputService
    {

        private readonly ILogger<IOutputService> _logger;
        private readonly IConfiguration _config;


        public OutputDummyService(ILogger<IOutputService> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;

        }

        public void ConfigOutput()
        {
            _logger.LogInformation("ConfigDummyOutput Config Complete");

            return;
        }

        public async Task ProcessOutput()
        {
            //dummy output class as placeholder

            Console.WriteLine();

            _logger.LogInformation("ProcessDummyOutput Complete ");
            return;
        }


    }
}
