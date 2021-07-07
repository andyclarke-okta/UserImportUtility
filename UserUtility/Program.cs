using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UserUtility.Services;
using Microsoft.Extensions.FileProviders;
using System;
using NLog.Extensions.Logging;
using UserUtility.Models;

//_logger.LogTrace("ProcessConsumer(s) TaskId={0},ThreadId={1}", Task.CurrentId, Thread.CurrentThread.ManagedThreadId);

namespace UserUtility
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var host = new HostBuilder()
                //built in logging, good for console and VS debug window
                //.ConfigureLogging((hostContext, configLogging) =>
                //{
                //    configLogging.AddConsole();
                //    configLogging.AddDebug();
                //})
                .ConfigureHostConfiguration(configHost =>
                {
                    configHost.SetBasePath(Directory.GetCurrentDirectory());
                    configHost.AddJsonFile("hostsettings.json", optional: true);
                    configHost.AddCommandLine(args);
                })
                .ConfigureAppConfiguration((hostContext, configApp) =>
                {
                    configApp.AddJsonFile("appsettings.json", optional: true);
                    //configApp.AddJsonFile(
                    //    $"appsettings.{hostContext.HostingEnvironment.EnvironmentName}.json",
                    //    optional: true);
                    configApp.AddJsonFile("generalConfig.json", optional: false, reloadOnChange: false);
                    configApp.AddCommandLine(args);
                })
                .ConfigureServices((hostContext, services) =>
                {

                    //built-in logging
                    //services.AddLogging();
                    //3rd party NLog
                    services.AddLogging(builder =>
                    {
                        builder.SetMinimumLevel(LogLevel.Trace);
                        builder.AddNLog(new NLogProviderOptions
                        {
                            CaptureMessageTemplates = true,
                            CaptureMessageProperties = true
                        });
                    });

                    if (hostContext.HostingEnvironment.IsDevelopment())
                    {
                        // Development service configuration
                    }
                    else
                    {
                        // Non-development service configuration
                    }

                    //Create Test Users without blocking queue
                    services.AddSingleton<IOutputService, OutputDummyService>();
                    services.AddSingleton<IConsumerService, ConsumerTestUserService>();
                    services.AddSingleton<IProducerService, ProducerDummyService>();

                    //Create Test Users with Producer Consumer Model
                    //services.AddSingleton<IOutputService, OutputCustomCsvService>();
                    //services.AddSingleton<UserQueue<CustomOktaUser>>();
                    //services.AddSingleton<IConsumerService, ConsumerCreateUserService>();
                    //services.AddSingleton<IProducerService, ProducerTestUserService>();


                    ////Create Users from CSV File
                    //services.AddSingleton<IOutputService, OutputCustomCsvService>();
                    //services.AddSingleton<UserQueue<CustomOktaUser>>();
                    //services.AddSingleton<IConsumerService, ConsumerCreateUserService>();
                    //services.AddSingleton<IProducerService, ProducerTinyCsvService>();

                    //Create Users from SQL DB
                    //services.AddSingleton<IOutputService, OutputCustomCsvService>();
                    //services.AddSingleton<UserQueue<CustomOktaUser>>();
                    //services.AddSingleton<IConsumerService, ConsumerCreateUserService>();
                    //services.AddSingleton<IProducerService, ProducerSqlDbService>();

                    //Upsert Users from CSV File
                    //services.AddSingleton<IOutputService, OutputCustomCsvService>();
                    //services.AddSingleton<UserQueue<CustomOktaUser>>();
                    //services.AddSingleton<IConsumerService, ConsumerUpsertUserService>();
                    //services.AddSingleton<IProducerService, ProducerTinyCsvService>();

                    //Delete Users 
                    //services.AddSingleton<IOutputService, OutputBasicCsvService<RollbackOktaUser>>();
                    //services.AddSingleton<UserQueue<BasicOktaUser>>();
                    //services.AddSingleton<IConsumerService, ConsumerDeleteUserService>();
                    //services.AddSingleton<IProducerService, ProduceUserApiService>();

                    ////Audit Users
                    //services.AddSingleton<IOutputService, OutputBasicCsvService<AuditOktaUser>>();
                    //services.AddSingleton<UserQueue<BasicOktaUser>>();
                    //services.AddSingleton<IConsumerService, ConsumerAuditUserService>();
                    //services.AddSingleton<IProducerService, ProduceUserApiService>();


                    //Validate user
                    //services.AddSingleton<IOutputService, OutputCustomCsvService>();
                    //services.AddSingleton<UserQueue<CustomOktaUser>>();
                    //services.AddSingleton<IConsumerService, ConsumerValidateUserService>();
                    //services.AddSingleton<IProducerService, ProducerTinyCsvService>();

                    //Obfuscate user
                    //services.AddSingleton<IOutputService, OutputCustomCsvService>();
                    //services.AddSingleton<UserQueue<CustomOktaUser>>();
                    //services.AddSingleton<IConsumerService, ConsumerObfuscateService>();
                    //services.AddSingleton<IProducerService, ProducerTinyCsvService>();

                })
           
                .UseConsoleLifetime()
                .Build();
            //end of new HostBuilder

            using (host)
            {
                Console.WriteLine("Begin Main ...");
                // Start the host
                await host.StartAsync();

                var outputFiles = host.Services.GetRequiredService<IOutputService>();
                outputFiles.ConfigOutput();

                //Note: using await in method calls executes services sequentially, which is better for troubleshooting
                //without await the services all fire concurrently per design
                var producer = host.Services.GetRequiredService<IProducerService>();
                //producer.ProcessProducer();
                await producer.ProcessProducer();

                var consumer = host.Services.GetRequiredService<IConsumerService>();
                //consumer.ProcessConsumer();
                await consumer.ProcessConsumer();

                //comment this for producer/consumer test user
                //outputFiles.ProcessOutput();
                await outputFiles.ProcessOutput();

                // Wait for the host to shutdown
                Console.WriteLine("...All Tasks have been Started");
                //await host.WaitForShutdownAsync();
                // enabled StopAsync may mask some error conditions and not report them
                await host.StopAsync();  
                //host.Dispose();              
            }

        }
    }
}