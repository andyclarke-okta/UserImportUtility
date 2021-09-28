using UserUtility.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace UserUtility.Services
{
    //public interface IOutputService<T>
    public interface IOutputService
    {
       

        void ConfigOutput();
        Task ProcessOutput();

        void IncrementSuccessCount();
        void IncrementFailureCount();
        void IncrementReplayCount();
    }
}
