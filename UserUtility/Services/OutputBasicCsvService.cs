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

namespace UserUtility.Services
{
    public class OutputBasicCsvService<T> : IOutputService
    {
        private readonly ILogger<OutputBasicCsvService<T>> _logger;
        private readonly IConfiguration _config;
        private readonly BlockingCollection<BasicOktaUser> _userQueue;
        private FileStream fsOutputSuccess = null;
        private FileStream fsOutputFailure = null;
        private FileStream fsOutputReplay = null;
        private int _queueWaitms;
        private int _reportingWaitms;
        //bool _isAudit;
        string baseFileName = null;
        string _featureMode = null;

        public StreamWriter SwSuccess { get; set; }
        public StreamWriter SwFailure { get; set; }
        public StreamWriter SwReplay { get; set; }

        public BlockingCollection<BasicOktaUser> _successQueue;
        public BlockingCollection<BasicOktaUser> _failureQueue;
        public BlockingCollection<BasicOktaUser> _replayQueue;

        private int outputSuccessCount = 0;
        private int outputFailureCount = 0;
        private int outputReplayCount = 0;

        

        public OutputBasicCsvService(ILogger<OutputBasicCsvService<T>> logger, IConfiguration config, UserQueue<BasicOktaUser> inputQueue)
        {
            _logger = logger;
            _config = config;
            _queueWaitms = _config.GetValue<int>("generalConfig:queueWaitms");
            _reportingWaitms = _config.GetValue<int>("generalConfig:reportingWaitms");
            //_isAudit = _config.GetValue<bool>("generalConfig:isAudit");
            _userQueue = inputQueue.userQueue;
            _successQueue = inputQueue.successQueue;
            _failureQueue = inputQueue.failureQueue;
            _replayQueue = inputQueue.replayQueue;

            _featureMode = typeof(T).ToString();
            _logger.LogInformation("OutputBasicCsvService _featureMode {0}", _featureMode);

        }

        public void ConfigOutput()
        {
            //string headerLine = null;
            string outFile = null;

            _logger.LogInformation("ConfigAuditCsvOutput Start on TaskId={1}, ThreadId={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);



            //create output files
            if (_featureMode == "UserUtility.Models.AuditOktaUser")
            {
                baseFileName = "AuditUser";
            }
            else
            {
                baseFileName = "DeleteUser";
            }
            
            string fileDate = DateTime.Now.ToString("yyyyMMdd-hhmmss");

            //success
            outFile = string.Format("{0}_success_{1}.csv", baseFileName, fileDate);
            fsOutputSuccess = new FileStream(path: outFile, mode: FileMode.Create, access: FileAccess.Write, share: FileShare.Write, bufferSize: 4096, useAsync: true);
            SwSuccess = new StreamWriter(fsOutputSuccess);
            SwSuccess.WriteLine("login,email,firstName,lastName,oktaId,status,output");
            SwSuccess.Flush();

            if (!(_featureMode == "UserUtility.Models.AuditOktaUser"))
            {
                ////failure
                outFile = string.Format("{0}_failure_{1}.csv", baseFileName, fileDate);
                fsOutputFailure = new FileStream(path: outFile, mode: FileMode.Create, access: FileAccess.Write, share: FileShare.Write, bufferSize: 4096, useAsync: true);
                SwFailure = new StreamWriter(fsOutputFailure);
                SwFailure.WriteLine("login,email,firstName,lastName,oktaId,status,output");
                SwFailure.Flush();

                ////replay
                outFile = string.Format("{0}_replay_{1}.csv", baseFileName, fileDate);
                fsOutputReplay = new FileStream(path: outFile, mode: FileMode.Create, access: FileAccess.Write, share: FileShare.Write, bufferSize: 4096, useAsync: true);
                SwReplay = new StreamWriter(fsOutputReplay);
                SwReplay.WriteLine("login,email,firstName,lastName,oktaId,status,output");
                SwReplay.Flush();
            }

            return;
        }

        public async Task ProcessOutput()
        {
            Task taskS = null;
            Task taskF = null;
            Task taskR = null;

            _logger.LogInformation("ProcessCsvOutput Start TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            taskS = Task.Run(() => ProcessOutputSuccess());
            if (!(_featureMode == "UserUtility.Models.AuditOktaUser"))
            {         
                taskF = Task.Run(() => ProcessOutputFailure());
                taskR = Task.Run(() => ProcessOutputReplay());
            }
            
            try
            {
                if (_featureMode == "UserUtility.Models.AuditOktaUser")
                {
                    await Task.WhenAll(taskS);
                }
                else
                {
                    await Task.WhenAll(taskS, taskF, taskR);
                }             
            }
            catch (AggregateException ae)
            {
                _logger.LogError("ProcessOutput One or more exceptions occurred:");
                foreach (var ex in ae.InnerExceptions)
                    _logger.LogError("Exception {0}: {1}", ex.GetType().Name, ex.Message);
            }

            _logger.LogInformation("ProcessOutput Complete TaskId={0}, ThreadId={1}, success={2}, failure={3}, replay={4}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId,outputSuccessCount,outputFailureCount,outputReplayCount);
            return;
        }

        public async Task ProcessOutputSuccess()
        {
            _logger.LogInformation("ProcessOutputSuccess Start  TaskId={0}, ThreadId={1}",  Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
            while (!_successQueue.IsCompleted)
            {

                BasicOktaUser nextItem;

                try
                {
                    if (!_successQueue.TryTake(out nextItem, _queueWaitms))
                    {
                        _logger.LogTrace("ProcessOutputSuccess Take Blocked TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                        //check if all queues ar complete
                        if (_userQueue.IsCompleted && (_failureQueue.Count == 0) && (_replayQueue.Count == 0))
                        {
                            //_logger.LogInformation("ProcessOutputSuccess userQueue complete successQueue={0}", _successQueue.Count);
                            Task.WaitAll(Task.Delay(_reportingWaitms));
                            if (_successQueue.Count > 0)
                            {
                                _logger.LogInformation("ProcessOutputSuccess post reportingWaitms successQueue={0}", _successQueue.Count);
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
                        _logger.LogTrace("ProcessOutputSuccess TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, nextItem.profile.email);

                        outputSuccessCount++;
                        //write to file
                        var sb = new StringBuilder();
                        sb.AppendFormat("{0},{1},{2},{3},{4},{5},{6}",nextItem.profile.login,nextItem.profile.email,nextItem.profile.firstName,nextItem.profile.lastName,nextItem.Id,nextItem.Status,nextItem.output);
                        var tempStr = sb.ToString();
                        await SwSuccess.WriteLineAsync(sb.ToString());

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
            _logger.LogInformation("ProcessOutputFailure Start  TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            while (!_failureQueue.IsCompleted)
            {
                BasicOktaUser nextItem;
                try
                {
                    if (!_failureQueue.TryTake(out nextItem, _queueWaitms))
                    {
                        _logger.LogTrace("ProcessOutputFailure Take Blocked TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                        //check if all queues ar complete
                        if (_userQueue.IsCompleted && (_successQueue.Count == 0) && (_replayQueue.Count == 0))
                        {
                            //_logger.LogInformation("ProcessOutputFailure userQueue complete failureQueue={0}", _failureQueue.Count);
                            Task.WaitAll(Task.Delay(_reportingWaitms));
                            //Thread.Sleep(_reportingWaitms);
                            if (_failureQueue.Count > 0)
                            {
                                _logger.LogInformation("ProcessOutputFailure post reportingWaitms failureQueue={0}", _failureQueue.Count);
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
                        _logger.LogTrace("ProcessOutputFailure TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, nextItem.profile.email);
                        outputFailureCount++;
                        //write to file
                        var sb = new StringBuilder();
                        sb.AppendFormat("{0},{1},{2},{3},{4},{5},{6}", nextItem.profile.login, nextItem.profile.email, nextItem.profile.firstName, nextItem.profile.lastName, nextItem.Id, nextItem.Status,nextItem.output);
                        var tempStr = sb.ToString();
                        await SwFailure.WriteLineAsync(sb.ToString());

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
            _logger.LogInformation("ProcessOutputReplay Start TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            while (!_replayQueue.IsCompleted)
            {

                BasicOktaUser nextItem;
                try
                {
                    if (!_replayQueue.TryTake(out nextItem, _queueWaitms))
                    {
                        _logger.LogTrace("ProcessOutputReplay Take Blocked TaskId={0}, ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
                        //check if all queues ar complete
                        if (_userQueue.IsCompleted && (_successQueue.Count == 0) && (_failureQueue.Count == 0))
                        {
                            //_logger.LogInformation("ProcessOutputReplay userQueue complete replayQueue={0}", _replayQueue.Count);
                            Task.WaitAll(Task.Delay(_reportingWaitms));
                            if (_replayQueue.Count > 0)
                            {
                                _logger.LogInformation("ProcessOutputReplay post reportingWaitms replayQueue={0}", _replayQueue.Count);
                                //before closing reporting queue
                                //wait for any outstanding APis to respond
                                //check queue count
                                continue;
                            }
                            _logger.LogInformation("ProcessOutputReplay Complete");
                            _replayQueue.CompleteAdding();

                        }
                    }
                    else
                    {
                        _logger.LogTrace("ProcessOutputReplay TaskId={0}, ThreadId={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, nextItem.profile.email);
                        outputReplayCount++;
                        //write to file
                        var sb = new StringBuilder();
                        sb.AppendFormat("{0},{1},{2},{3},{4},{5},{6}", nextItem.profile.login, nextItem.profile.email, nextItem.profile.firstName, nextItem.profile.lastName, nextItem.Id, nextItem.Status,nextItem.output);
                        var tempStr = sb.ToString();
                        await SwReplay.WriteLineAsync(sb.ToString());
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
