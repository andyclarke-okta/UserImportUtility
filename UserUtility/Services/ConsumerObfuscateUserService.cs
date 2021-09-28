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
using System.Text.RegularExpressions;

namespace UserUtility.Services
{
    public class ConsumerObfuscateService : IConsumerService
    {
        private readonly ILogger<IConsumerService> _logger;

        private readonly IConfiguration _config;
        private readonly UserQueue<CustomOktaUser> _inputQueue;
        private IOutputService _outputService;
        private int _queueWaitms;
        private int _throttleMs;
        private int _consumerTasks;
        private List<string> _obfuscateWithStatic;
        private List<string> _obfuscateWithGUID;

        public ConsumerObfuscateService(ILogger<IConsumerService> logger, IConfiguration config, UserQueue<CustomOktaUser> inputQueue, IOutputService outputService)
        {
            _logger = logger;
            _inputQueue = inputQueue;
            _config = config;
            _queueWaitms = _config.GetValue<int>("generalConfig:queueWaitms");
            _throttleMs = _config.GetValue<int>("generalConfig:throttleMs");
            _outputService = outputService;
            _consumerTasks = _config.GetValue<int>("generalConfig:consumerTasks");
            _obfuscateWithStatic = _config.GetSection("obfuscateConfig:obfuscateWithStatic").Get<List<string>>();
            _obfuscateWithGUID = _config.GetSection("obfuscateConfig:obfuscateWithGUID").Get<List<string>>();
        }

        //public void ProcessConsumer()
        public async Task ProcessConsumer()
        {
            //_logger.LogDebug("ProcessConsumer  TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
            
            _logger.LogInformation("ProcessObfuscateUserConsumer Start with {0} consumerTasks on TaskId={1}, ThreadId={2}", _consumerTasks, Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            Task[] consumers = new Task[_consumerTasks];
            for (int i = 0; i < consumers.Length; i++)
            {
                consumers[i] = Task.Run(() =>
                {
                    _logger.LogTrace("SpinUp New ServiceQueue Task on TaskId={0},ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                    ServiceQueue(_inputQueue.userQueue);
                }
                    );//end Task.Run
            }//end of for

            try
            {
                //Task.WaitAll(consumers);
                await Task.WhenAll(consumers);
            }
            catch (AggregateException ae)
            {
                _logger.LogError("One or more exceptions occurred:");
                foreach (var ex in ae.InnerExceptions)
                    _logger.LogError("Exception {0}: {1}", ex.GetType().Name, ex.Message);
            }

            _logger.LogInformation("ProcessObfuscateUserConsumer Complete TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
            return;
        }//end ProcessConsumer

        public void ServiceQueue(BlockingCollection<CustomOktaUser> userQueue)
        {
            _logger.LogInformation("ServiceQueue Start New  ObfuscateUser QueueCount={0},TaskId={1}, ThreadId={2}", userQueue.Count, Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
            while (!userQueue.IsCompleted)
            {
                CustomOktaUser nextItem = null;
                try
                {
                    if (!userQueue.TryTake(out nextItem, _queueWaitms))
                    {
                        // _logger.LogTrace("ServiceQueue Take Blocked TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                    }
                    else
                    {
                        Task.WaitAll(Task.Delay(_throttleMs));
                        MaskUser(nextItem);
                        _logger.LogTrace("ServiceQueue ObfuscateUser Take Success QueueCount={0}, TaskId={1}, ThreadId={2}, item={3}", userQueue.Count, Task.CurrentId, Thread.CurrentThread.ManagedThreadId, nextItem.email);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogError("ServiceQueue ObfuscateUser Taking Cancelled TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                    //break;
                }
                catch (Exception)
                {
                    _logger.LogError("ServiceQueue ObfuscateUser TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                }
            }
            _logger.LogDebug("ServiceQueue Complete ObfuscateUser TaskId={0}, ThreadId={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
        }//end ServiceQueue

        //[Benchmark]
        public void MaskUser(CustomOktaUser user)
        {
            //https://oktawiki.atlassian.net/wiki/spaces/serv/pages/644522829/Design+Data+Obfuscate
            _logger.LogTrace("Start MaskUser  TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email);



            string output = null;
            bool isUserMasked = true;


            if (isUserMasked)
            {
                user.output = "user obfuscated successfully";
                //_inputQueue.successQueue.TryAdd(user);
                

                try
                {
                    _inputQueue.successQueue.Add(user);
                    _logger.LogDebug("MaskUser Success add Queue TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email);
                }
                catch (System.InvalidOperationException)
                {
                    _logger.LogError("MaskUser Error Adding Entry to successQueue");
                }



            }
            else
            {
                //for failed users 
                user.output = "user obfuscated failed";
                //_inputQueue.failureQueue.TryAdd(user);
                

                try
                {
                    _inputQueue.failureQueue.Add(user);
                    _logger.LogDebug("MaskUser Failure add Queue TaskId={0}, ThreadId={1}, item={2},reason={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email, output);
                }
                catch (System.InvalidOperationException)
                {
                    _logger.LogError("MaskUser Error Adding Entry to failureQueue");
                }

            }

        }

    }
}
