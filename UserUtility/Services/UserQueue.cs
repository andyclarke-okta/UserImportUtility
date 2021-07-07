using UserUtility.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace UserUtility.Services
{
    public class UserQueue<T> 
    {

        private readonly ILogger<UserQueue<T>> _logger;
        private readonly IConfiguration _config;

        public List<T> staticUserQueue { get; set; }
        public BlockingCollection<T> userQueue { get; set; }
        public BlockingCollection<T> successQueue { get; set; }
        public BlockingCollection<T> failureQueue { get; set; }
        public BlockingCollection<T> replayQueue { get; set; }

        public UserQueue(ILogger<UserQueue<T>> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;

            staticUserQueue = new List<T>();
            userQueue = new BlockingCollection<T>(_config.GetValue<int>("generalConfig:userQueueBufferSize"));
            successQueue = new BlockingCollection<T>(_config.GetValue<int>("generalConfig:outputQueueBufferSize"));
            failureQueue = new BlockingCollection<T>(_config.GetValue<int>("generalConfig:outputQueueBufferSize"));
            replayQueue = new BlockingCollection<T>(_config.GetValue<int>("generalConfig:outputQueueBufferSize"));
        }

    }
}
