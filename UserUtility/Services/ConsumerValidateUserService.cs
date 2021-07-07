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
    public class ConsumerValidateUserService : IConsumerService
    {
        private readonly ILogger<IConsumerService> _logger;

        private readonly IConfiguration _config;
        private readonly UserQueue<CustomOktaUser> _inputQueue;

        private IOutputService _outputFiles;
        private int _queueWaitms;
        private int _throttleMs;
        private int _consumerTasks;

        public ConsumerValidateUserService(ILogger<IConsumerService> logger, IConfiguration config, UserQueue<CustomOktaUser> inputQueue, IOutputService outputFiles)
        {
            _logger = logger;
            _inputQueue = inputQueue;
            _config = config;
            _queueWaitms = _config.GetValue<int>("generalConfig:queueWaitms");
            _throttleMs = _config.GetValue<int>("generalConfig:throttleMs");
            _outputFiles = outputFiles;
            _consumerTasks = _config.GetValue<int>("generalConfig:consumerTasks");
        }

        //public void ProcessConsumer()
        public async Task ProcessConsumer()
        {
            
            _logger.LogInformation("ProcessValidateUserConsumer Start with {0} consumerTasks on TaskId={1}, ThreadId={2}", _consumerTasks, Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

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

            _logger.LogInformation("ProcessValidateUserConsumer Complete TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
            return;
        }//end ProcessConsumer

        public void ServiceQueue(BlockingCollection<CustomOktaUser> userQueue)
        {
            _logger.LogInformation("ServiceQueue Start New ValidateUser QueueCount={0},TaskId={1}, ThreadId={2}", userQueue.Count, Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
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
                        //Task.WaitAll(Task.Delay(_throttleMs));
                        InterrogateUser(nextItem);
                        _logger.LogTrace("ServiceQueue ValidateUser Take Success QueueCount={0}, TaskId={1}, ThreadId={2}, item={3}", userQueue.Count, Task.CurrentId, Thread.CurrentThread.ManagedThreadId, nextItem.email);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogError("ServiceQueue ValidateUser Taking Cancelled TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                    //break;
                }
                catch (Exception)
                {
                    _logger.LogError("ServiceQueue ValidateUser  TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                }
            }
            _logger.LogDebug("ServiceQueue ValidateUser Complete TaskId={0}, ThreadId={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
        }//end ServiceQueue

        //[Benchmark]
        public void InterrogateUser(CustomOktaUser user)
        {
            //https://oktawiki.atlassian.net/wiki/spaces/serv/pages/644522829/Design+Data+Validation
            _logger.LogTrace("Start InterrogateUser  TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email);
            List<string> emailAttributes = _config.GetSection("validationConfig:validateEmailFormat").Get<List<string>>();
            List<string> lengthAttributes = _config.GetSection("validationConfig:validateFieldLength").Get<List<string>>();
            List<string> nullAttributes = _config.GetSection("validationConfig:validateNull").Get<List<string>>();
            List<string> uniqueAttributes = _config.GetSection("validationConfig:validateUniqueness").Get<List<string>>();

            string output = null;
            bool isUserValid = true;

            //Validate null or empty
            if (nullAttributes != null)
            {
                foreach (var item in nullAttributes)
                {
                    var property = typeof(CustomOktaUser).GetProperties().FirstOrDefault(n => n.Name == item);
                    var attr = property.GetValue(user, null).ToString();
                    if (string.IsNullOrEmpty(attr))
                    {
                        isUserValid = false;
                        output = output + " Attribute is Null=" + item;
                    }
                }
            }


            //Validate email format
            Regex regex = new Regex(@"[a-zA-Z0-9._-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]");
            //Regex regex = new Regex(@" ^[a-zA-Z0-9_!#$%&'*+/=?`{|}~^-]+(?:\\.[a-zA-Z0-9_!#$%&'*+/=?`{|}~^-]+)*@[a-zA-Z0-9-]+(?:\\.[a-zA-Z0-9-]+)*$");
            //Regex regex = new Regex(@"[\p{L}\p{Digit}\!\#\$\%\&\*\+\-\/\=\?\^\_]+(?:\.[\p{L}\p{Digit}\-\!\#\$\%\&\*\+\-\/\=\?\^\_]+)*@(?:[\p{L}\p{Digit}](?:[\p{L}\p{Digit}-]*[\p{L}\p{Digit}])?\.)+[\p{L}]{2,20}");
            if (emailAttributes != null)
            {
                foreach (var item in emailAttributes)
                {
                    var property = typeof(CustomOktaUser).GetProperties().FirstOrDefault(n => n.Name == item);
                    var attr = property.GetValue(user, null).ToString();
                    Match match = regex.Match(attr);
                    if (!match.Success)
                    {
                        isUserValid = false;
                        output = output + " Bad Email format attribute=" + item;
                    }
                }
            }


            //Validate field uniqueness within the Source data
            //if (uniqueAttributes != null)
            //{
            //    foreach (var item in uniqueAttributes)
            //    {
            //        var property = typeof(CustomOktaUser).GetProperties().FirstOrDefault(n => n.Name == item);
            //        var currentAttr = property.GetValue(user, null).ToString();
            //        int count = 0;
            //        foreach (var staticItem in _inputQueue.staticUserQueue)
            //        {
            //            var staticAttr = property.GetValue(staticItem, null).ToString();
            //            //check current entry with all entries in statis queue
            //            if (currentAttr == staticAttr)
            //            {
            //                count++;
            //                _logger.LogTrace("count={0},current={1},static={2}", count, currentAttr, staticAttr);
            //                if (count > 1)
            //                {
            //                    isUserValid = false;
            //                    output = output + " Attribute is Not Unique=" + item;
            //                }
            //            }
            //        }
            //    }
            //}


            //Validate field length
            if (lengthAttributes != null)
            {
                foreach (var item in lengthAttributes)
                {
                    //split field into attribute name and target length
                    string[] entry = item.Split(":");
                    int target = Convert.ToInt16(entry[1]);
                    var property = typeof(CustomOktaUser).GetProperties().FirstOrDefault(n => n.Name == entry[0]);
                    var attr = property.GetValue(user, null).ToString();
                    var length = attr.Length;
                    if (length > target)
                    {
                        isUserValid = false;
                        output = output + " Attribute too long=" + item;
                    }
                }
            }


            //Validate special charactors

            if (isUserValid)
            {
                user.output = "user validated successfully";
                _inputQueue.successQueue.TryAdd(user);
                _logger.LogDebug("InterrogateUser Success add Queue TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email);
            }
            else
            {
                //for failed users 
                user.output = output;
                _inputQueue.failureQueue.TryAdd(user);
                _logger.LogDebug("InterrogateUser Failure add Queue TaskId={0}, ThreadId={1}, item={2},reason={3}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, user.email, output);
            }
            //Replay Queue is not needed here 
            //_inputQueue.replayQueue.CompleteAdding();
            //_logger.LogTrace("Complete InterrogateUser TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId,user.email);
        }

    }
}
