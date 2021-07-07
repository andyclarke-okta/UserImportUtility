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

namespace UserUtility.Services
{
    public class ConsumerAuditUserService : IConsumerService
    {
        private readonly ILogger<IConsumerService> _logger;

        private readonly IConfiguration _config;
        //private readonly BlockingCollection<String> _userQueue;
        private readonly UserQueue<BasicOktaUser> _inputQueue;
        //private IOutputService _outputFiles;
        private int _queueWaitms;
        private int _consumerTasks;

        public ConsumerAuditUserService(ILogger<IConsumerService> logger, IConfiguration config, UserQueue<BasicOktaUser> inputQueue, IOutputService outputFiles)
        {
            _logger = logger;
            _inputQueue = inputQueue;
            _config = config;
           // _outputFiles = outputFiles;
            _queueWaitms = _config.GetValue<int>("generalConfig:queueWaitms");
            _consumerTasks = _config.GetValue<int>("generalConfig:consumerTasks");
        }

        public async Task ProcessConsumer()
        {         
            _logger.LogInformation("ProcessAuditUserConsumer Start with {0} consumerTasks on TaskId={1}, ThreadId={2}", _consumerTasks, Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            Task[] consumers = new Task[_consumerTasks];
            for (int i = 0; i < consumers.Length; i++)
            {
                consumers[i] = Task.Run(() =>
                {
                    _logger.LogTrace("SpinUp New Audit ServiceQueue Task on TaskId={0},ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

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
            Console.WriteLine();
            _logger.LogInformation("ProcessAuditUserConsumer Complete TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
            return;
        }//end ProcessConsumer

        public void ServiceQueue(BlockingCollection<BasicOktaUser> userQueue)
        {
            _logger.LogInformation("ServiceQueue Start New AuditUser QueueCount={0}, TaskId={1}, ThreadId={2}", userQueue.Count, Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
            while (!userQueue.IsCompleted)
            {
                BasicOktaUser nextItem = null;
                try
                {
                    if (!userQueue.TryTake(out nextItem, _queueWaitms))
                    {
                        // _logger.LogTrace("ServiceQueue Take Blocked TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                    }
                    else
                    {

                        Console.Write("\r{0}", userQueue.Count());                        
                        _logger.LogDebug("ServiceQueue AuditUser Take Success QueueCount={0}, TaskId={1}, ThreadId={2}, oktaId={3}", userQueue.Count, Task.CurrentId, Thread.CurrentThread.ManagedThreadId, nextItem);
                        AuditGroupUser(nextItem);
                  

                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogError("ServiceQueue AuditUser Taking Cancelled TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                    //break;
                }
                catch (Exception ex)
                {
                    _logger.LogError("ServiceQueue AuditUser Exp={0} TaskId={1}, ThreadId={2}", ex.ToString(), Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                }
            }
            _logger.LogDebug("ServiceQueue Complete AuditUser TaskId={0}, ThreadId={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
        }//end ServiceQueue

        //[Benchmark]
        public  void AuditGroupUser(BasicOktaUser user)
        {
            _logger.LogTrace("AuditGroupUser Start AuditUser  TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user);


            try
            {
                _inputQueue.successQueue.Add(user);
                _logger.LogDebug("AuditGroupUser Success add Queue TaskId={0}, ThreadId={1}, item={2},429={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.profile.email);
            }
            catch (System.InvalidOperationException)
            {
                _logger.LogError("AuditGroupUser Error Adding Entry to successQueue Id={0}", user.profile.email);
            }


        }

    }
}
