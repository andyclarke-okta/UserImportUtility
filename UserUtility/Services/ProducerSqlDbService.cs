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
using System.Data.SqlClient;
using System.Data;

namespace UserUtility.Services
{
    public class ProducerSqlDbService : IProducerService
    {

        private readonly ILogger<IProducerService> _logger;
        private readonly IConfiguration _config;
        private readonly BlockingCollection<CustomOktaUser> _userQueue;
        SqlConnection cn;
        bool _isLoginEqualEmail;

        public ProducerSqlDbService(ILogger<IProducerService> logger, IConfiguration config, UserQueue<CustomOktaUser> inputQueue)
        {
            _logger = logger;
            _config = config;
            _userQueue = inputQueue.userQueue;
            _isLoginEqualEmail = _config.GetValue<bool>("importConfig:isLoginEqualEmail");
            //set up SQL connection
            cn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

        }

        public async Task ProcessProducer()
        {
            //Note: only one SQL request should be executed
            //only one producer task should be created
            _logger.LogInformation("Start ProcessSqlProducer  on Task={0}, Thread={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            await Task.Run(() => GetDbData());

            _logger.LogInformation("Complete ProcessSqlProducer Task={0}, Thread={1}, count={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);
        }


        public void GetDbData()
        {
            int producerCount = 0;
            _logger.LogInformation("Start GetDbData  on Task={0}, Thread={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

            DataSet dsProvQ = new DataSet();
            string sqlquery = "SELECT [email] ,[firstName],[lastName],[customId],[dateOfBirth],[value],[salt]  FROM [OktaUserImport].[dbo].[CustomOktaUser]";
            SqlDataAdapter daProvQ = new SqlDataAdapter(sqlquery, cn);
            // fill the dataSet based on the sql query specified in the dataAdapter
            daProvQ.Fill(dsProvQ, "provQ");

            foreach (DataRow pRow in dsProvQ.Tables["provQ"].Rows)
            {
                CustomOktaUser newUser = new CustomOktaUser()
                {
                    email = Convert.ToString(pRow["email"]),
                    firstName = Convert.ToString(pRow["firstName"]),
                    lastName = Convert.ToString(pRow["lastName"]),
                   // customId = Convert.ToString(pRow["customId"]),
                    //dateOfBirth = Convert.ToString(pRow["dateOfBirth"]),
                    value = Convert.ToString(pRow["value"]),
                    salt = Convert.ToString(pRow["salt"]),
                };

                if (_isLoginEqualEmail)
                {
                    newUser.login = newUser.email;
                }

                _userQueue.Add(newUser);
                producerCount++;
                _logger.LogTrace("GetDbData add Queue Task={0}, Thread={1}, item={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, newUser.email);
                newUser = null;
            }

            _userQueue.CompleteAdding();
            _logger.LogInformation("Complete GetDbData Task={0}, Thread={1}, count={2}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId, producerCount);
        }
    }
}
