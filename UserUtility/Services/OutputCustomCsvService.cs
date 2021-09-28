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
    public class OutputCustomCsvService : IOutputService
    {
        private readonly ILogger<OutputCustomCsvService> _logger;
        private readonly IConfiguration _config;
        private readonly BlockingCollection<CustomOktaUser> _userQueue;
        private FileStream fsOutputSuccess = null;
        private FileStream fsOutputFailure = null;
        private FileStream fsOutputReplay = null;
        private int _queueWaitms;
        private int _cleanUpWaitms;
        char _inputFileFieldSeperator;
        char _stringArrayFieldSeperator;

        public StreamWriter SwSuccess { get; set; }
        public StreamWriter SwFailure { get; set; }
        public StreamWriter SwReplay { get; set; }

        public BlockingCollection<CustomOktaUser> _successQueue;
        public BlockingCollection<CustomOktaUser> _failureQueue;
        public BlockingCollection<CustomOktaUser> _replayQueue;

        private int outputSuccessCount = 0;
        private int outputFailureCount = 0;
        private int outputReplayCount = 0;

        public OutputCustomCsvService(ILogger<OutputCustomCsvService> logger, IConfiguration config, UserQueue<CustomOktaUser> inputQueue)
        {
            _logger = logger;
            _config = config;
            _queueWaitms = _config.GetValue<int>("generalConfig:queueWaitms");
            _cleanUpWaitms = _config.GetValue<int>("generalConfig:cleanUpWaitms");
            _inputFileFieldSeperator = _config.GetValue<char>("importConfig:inputFileFieldSeperator");
            _stringArrayFieldSeperator = _config.GetValue<char>("importConfig:stringArrayFieldSeperator");
            _userQueue = inputQueue.userQueue;
            _successQueue = inputQueue.successQueue;
            _failureQueue = inputQueue.failureQueue;
            _replayQueue = inputQueue.replayQueue;
        }

        public void ConfigOutput()
        {
            string headerLine = null;
            string outFile = null;

            _logger.LogInformation("ConfigCsvOutput Start on TaskId={1}, ThreadId={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            string csvFile = _config.GetValue<string>("inputFile");
            //get header from input file
            if (File.Exists(csvFile))
            {
                using (StreamReader sr = new StreamReader(@csvFile))
                {
                    // Read the stream to a string, and write the string to the console.
                    headerLine = sr.ReadLine();
                    _logger.LogDebug("header {}", headerLine);
                }
            }
            else
            {
                _logger.LogError("No File Found {}", @csvFile);
            }


            //create output files
            var index = csvFile.IndexOf(".csv");
            string baseFileName = csvFile.Substring(0, index);
            string fileDate = DateTime.Now.ToString("yyyyMMdd-hhmmss");

            //success
            outFile = string.Format("{0}_success_{1}.csv", baseFileName, fileDate);
            fsOutputSuccess = new FileStream(path: outFile, mode: FileMode.Create, access: FileAccess.Write, share: FileShare.Write, bufferSize: 4096, useAsync: true);
            SwSuccess = new StreamWriter(fsOutputSuccess);
            SwSuccess.WriteLine(headerLine);
            SwSuccess.Flush();

            //failure
            outFile = string.Format("{0}_failure_{1}.csv", baseFileName, fileDate);
            fsOutputFailure = new FileStream(path: outFile, mode: FileMode.Create, access: FileAccess.Write, share: FileShare.Write, bufferSize: 4096, useAsync: true);
            SwFailure = new StreamWriter(fsOutputFailure);
            SwFailure.WriteLine(headerLine);
            SwFailure.Flush();

            //replay
            outFile = string.Format("{0}_replay_{1}.csv", baseFileName, fileDate);
            fsOutputReplay = new FileStream(path: outFile, mode: FileMode.Create, access: FileAccess.Write, share: FileShare.Write, bufferSize: 4096, useAsync: true);
            SwReplay = new StreamWriter(fsOutputReplay);
            SwReplay.WriteLine(headerLine);
            SwReplay.Flush();

            return;
        }

        public async Task ProcessOutput()
        {
            _logger.LogInformation("ProcessCsvOutput Start TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
            Task taskS = Task.Run(() => ProcessOutputSuccess());
            Task taskF = Task.Run(() => ProcessOutputFailure());
            Task taskR = Task.Run(() => ProcessOutputReplay());

            try
            {
                await Task.WhenAll(taskS, taskF, taskR);
            }
            catch (AggregateException ae)
            {
                _logger.LogError("ProcessStringOutput One or more exceptions occurred:");
                foreach (var ex in ae.InnerExceptions)
                    _logger.LogError("Exception {0}: {1}", ex.GetType().Name, ex.Message);
            }

            _logger.LogInformation("ProcessCsvOutput Complete TaskId={0}, ThreadId={1}, success={2}, failure={3}, replay={4}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId,outputSuccessCount,outputFailureCount,outputReplayCount);
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

        public async Task ProcessOutputSuccess()
        {
            _logger.LogInformation("ProcessOutputSuccess Start  TaskId={0}, ThreadId={1}",  Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
            while (!_successQueue.IsCompleted)
            {
                CustomOktaUser nextItem = null;
                try
                {
                    if (!_successQueue.TryTake(out nextItem, _queueWaitms))
                    {
                        _logger.LogTrace("ProcessOutputSuccess Take Blocked TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                        //check if all queues ar complete
                        if (_userQueue.IsCompleted && (_failureQueue.Count == 0) && (_replayQueue.Count == 0))
                        {
                            //_logger.LogInformation("ProcessOutputSuccess userQueue complete successQueue={0}", _successQueue.Count);
                            Task.WaitAll(Task.Delay(_cleanUpWaitms));
                            if (_successQueue.Count > 0)
                            {
                                _logger.LogInformation("ProcessOutputSuccess post _cleanUpWaitms successQueue={0}", _successQueue.Count);
                                //before closing reporting queue
                                //wait for any outstanding APis to respond
                                //check queue count
                                continue;
                            }
                            _logger.LogInformation("ProcessOutputSuccess Complete ");
                            _successQueue.CompleteAdding();
                            
                        }
                    }
                    else
                    {
                        _logger.LogTrace("ProcessOutputSuccess TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, nextItem.email);

                        outputSuccessCount++;
                        //write to file
                        string inputDelimiter = _inputFileFieldSeperator.ToString();
                        string arrayDelimiter = _stringArrayFieldSeperator.ToString();
                        var properties = typeof(CustomOktaUser).GetProperties().Where(n => n.PropertyType == typeof(string) || n.PropertyType == typeof(List<string>));

                        //var userRow = properties
                        //.Select(n => n.GetValue(nextItem, null))
                        //.Select(n => n = n.ToString())
                        //.Aggregate((a, b) => a + delimiter + b);

                        string userRow = null;
                        foreach (PropertyInfo property in properties)
                        {
                            var attrName = property.Name;
                            if (property.PropertyType == typeof(System.String))
                            {
                                var attrValue = property.GetValue(nextItem, null);
                                if (userRow == null)
                                {
                                    userRow = attrValue.ToString();
                                }
                                else
                                {
                                    userRow = userRow + inputDelimiter + attrValue;
                                }

                            }
                            else if (property.PropertyType == typeof(List<string>))
                            {
                                List<string> myList = (List<string>)property.GetValue(nextItem, null);
                                foreach (var item in myList)
                                {
                                    var attrValue = item;
                                    if (myList[0] == item)
                                    {
                                        userRow = userRow + inputDelimiter + attrValue;
                                    }
                                    else
                                    {
                                        userRow = userRow + arrayDelimiter + attrValue;
                                    }
                                }
                            }
                        }

                        await SwSuccess.WriteLineAsync(userRow);
                        await SwSuccess.FlushAsync();                  
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogError("ProcessOutputSuccess Taking canceled.");
                    break;
                }
            }//end while
        }

        public async Task ProcessOutputFailure()
        {
            _logger.LogInformation("ProcessOutputFailure Start  TaskId={0}, ThreadId={1}",  Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            while (!_failureQueue.IsCompleted)
            {
                CustomOktaUser nextItem = null;
                try
                {
                    if (!_failureQueue.TryTake(out nextItem, _queueWaitms))
                    {
                        _logger.LogTrace("ProcessOutputFailure Take Blocked TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                        //check if all queues ar complete
                        if (_userQueue.IsCompleted && (_successQueue.Count == 0) && (_replayQueue.Count == 0))
                        {
                            //_logger.LogInformation("ProcessOutputFailure userQueue complete failureQueue={0}", _failureQueue.Count);
                            Task.WaitAll(Task.Delay(_cleanUpWaitms));
                            //Thread.Sleep(_cleanUpWaitms);
                            if (_failureQueue.Count > 0)
                            {
                                _logger.LogInformation("ProcessOutputFailure post _cleanUpWaitms failureQueue={0}", _failureQueue.Count);
                                //before closing reporting queue
                                //wait for any outstanding APis to respond
                                //check queue count
                                continue;
                            }
                            _logger.LogInformation("ProcessOutputFailure Complete ");
                            _failureQueue.CompleteAdding();
                            
                        }
                    }
                    else
                    {
                        _logger.LogTrace("ProcessOutputFailure TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, nextItem.email);
                        outputFailureCount++;
                        //write to file
                        string inputDelimiter = _inputFileFieldSeperator.ToString();
                        string arrayDelimiter = _stringArrayFieldSeperator.ToString();
                        var properties = typeof(CustomOktaUser).GetProperties().Where(n => n.PropertyType == typeof(string) || n.PropertyType == typeof(List<string>));

                        //var userRow = properties
                        //.Select(n => n.GetValue(nextItem, null))
                        //.Select(n => n = n.ToString())
                        //.Aggregate((a, b) => a + delimiter + b);

                        string userRow = null;
                        foreach(PropertyInfo property in properties)
                        {
                            var attrName = property.Name;
                            if (property.PropertyType == typeof(System.String))
                            {
                                var attrValue = property.GetValue(nextItem, null);
                                if (userRow == null)
                                {
                                    userRow = attrValue.ToString();
                                }
                                else
                                {
                                    userRow = userRow + inputDelimiter + attrValue;
                                }
                                
                            }
                            else if (property.PropertyType == typeof(List<string>))
                            {
                                List<string> myList = (List<string>)property.GetValue(nextItem, null);
                                foreach (var item in myList)
                                {
                                    var attrValue = item;
                                    if (myList[0] == item)
                                    {
                                        userRow = userRow + inputDelimiter + attrValue;
                                    }
                                    else
                                    {
                                        userRow = userRow + arrayDelimiter + attrValue;
                                    }
                                }
                            }
                        }

                        await SwFailure.WriteLineAsync(userRow);
                        await SwFailure.FlushAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogError("ProcessOutputFailure Taking canceled.");
                    break;
                }

            }//end while
        }

        public async Task ProcessOutputReplay()
        {
            _logger.LogInformation("ProcessOutputReplay Start TaskId={0}, ThreadId={1}",  Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            while (!_replayQueue.IsCompleted)
            {

                CustomOktaUser nextItem = null;
                try
                {
                    if (!_replayQueue.TryTake(out nextItem, _queueWaitms))
                    {
                        _logger.LogTrace("ProcessOutputReplay Take Blocked TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                        //check if all queues ar complete
                        if (_userQueue.IsCompleted && (_successQueue.Count == 0) && (_failureQueue.Count == 0))
                        {
                            //_logger.LogInformation("ProcessOutputReplay userQueue complete replayQueue={0}", _replayQueue.Count);
                            Task.WaitAll(Task.Delay(_cleanUpWaitms));
                            //Thread.Sleep(_cleanUpWaitms);
                            if (_replayQueue.Count > 0)
                            {
                                _logger.LogInformation("ProcessOutputReplay post _cleanUpWaitms replayQueue={0}", _replayQueue.Count);
                                //before closing reporting queue
                                //wait for any outstanding APis to respond
                                //check queue count
                                continue;
                            }
                            _logger.LogInformation("ProcessOutputReplay Complete " );
                            _replayQueue.CompleteAdding();
                            
                        }
                    }
                    else
                    {
                        _logger.LogTrace("ProcessOutputReplay TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, nextItem.email);
                        outputReplayCount++;
                        //write to file
                        string inputDelimiter = _inputFileFieldSeperator.ToString();
                        string arrayDelimiter = _stringArrayFieldSeperator.ToString();
                        var properties = typeof(CustomOktaUser).GetProperties().Where(n => n.PropertyType == typeof(string) || n.PropertyType == typeof(List<string>));

                        //var userRow = properties
                        //.Select(n => n.GetValue(nextItem, null))
                        //.Select(n => n = n.ToString())
                        //.Aggregate((a, b) => a + delimiter + b);

                        string userRow = null;
                        foreach (PropertyInfo property in properties)
                        {
                            var attrName = property.Name;
                            if (property.PropertyType == typeof(System.String))
                            {
                                var attrValue = property.GetValue(nextItem, null);
                                if (userRow == null)
                                {
                                    userRow = attrValue.ToString();
                                }
                                else
                                {
                                    userRow = userRow + inputDelimiter + attrValue;
                                }

                            }
                            else if (property.PropertyType == typeof(List<string>))
                            {
                                List<string> myList = (List<string>)property.GetValue(nextItem, null);
                                foreach (var item in myList)
                                {
                                    var attrValue = item;
                                    if (myList[0] == item)
                                    {
                                        userRow = userRow + inputDelimiter + attrValue;
                                    }
                                    else
                                    {
                                        userRow = userRow + arrayDelimiter + attrValue;
                                    }
                                }
                            }
                        }

                        await SwReplay.WriteLineAsync(userRow);
                        await SwReplay.FlushAsync();

                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogError("ProcessOutputReplay Taking canceled.");
                    break;
                }
            }//end while

        }



    }
}
