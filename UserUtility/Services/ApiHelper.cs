using UserUtility.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;

namespace UserUtility.Services
{
    public class ApiHelper
    {

        public RateLimitHeader RateLimitCheck(HttpResponseMessage response)
        {
            RateLimitHeader rateLimitHeader = new RateLimitHeader();

            rateLimitHeader.limit =  response.Headers.GetValues("X-Rate-Limit-Limit").FirstOrDefault<string>();
            rateLimitHeader.remaining = response.Headers.GetValues("X-Rate-Limit-Remaining").FirstOrDefault<string>();
            rateLimitHeader.reset = response.Headers.GetValues("X-Rate-Limit-Reset").FirstOrDefault<string>();
            // Console.WriteLine(" Limit Config:" + limit + " Remaining:" + remaining);

            int waitUntilUnixTime;
            if (!int.TryParse(rateLimitHeader.reset, out waitUntilUnixTime))
            {
                Console.WriteLine("unable to calculate wait time");
            }
            //// See how long until we hit that time
            var unixTime = (Int64)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalMilliseconds;
            ////var millisToWait = unixTime - ((Int64)waitUntilUnixTime * 1000);
            var millisToWait = ((Int64)waitUntilUnixTime * 1000) - unixTime;


            //Console.WriteLine(" Limit Config:" + limitMod + " Remaining:" + remainingMod + " Epoch sec " + unixTime / 1000 + " ResetTime_sec:" + resetMod + " millisToWait:" + millisToWait);

            //if (millisToWait >= 100)
            //{
            //    //logger.Debug(" wait for " + myReset.ToString());
            //    // wait the reset time then return true to recylce the command
            //    WaitTimer(millisToWait);
            //}
            return rateLimitHeader;

        }

        public void WaitTimer(Int64 milliseconds)
        {
            //logger.Debug("wait " + milliseconds);
            //delay before checking
            //provide time for user to respond
            //int milliseconds = 3000;
            //int milliseconds = Convert.ToInt32("3000");
            if (milliseconds > 0)
            {
                // Cross platform sleep
                //using (var mre = new ManualResetEvent(false))
                //{
                //    mre.WaitOne((int)milliseconds);
                //}
            }
            return;
        }


        public PagedResultHeader GetHeaderInfo(HttpResponseMessage response, bool isContentNull = false)
        {
            var headerInfo = new PagedResultHeader(isContentNull);
            headerInfo.RequestUri = response.RequestMessage.RequestUri;

            IEnumerable<string> linkHeaders;
            if (response.Headers.TryGetValues("Link", out linkHeaders))
            {
                foreach (var header in linkHeaders)
                {
                    // Split the header on semicolons
                    var split = header.Split(';');

                    // Get and sanitize the url
                    var url = split[0];
                    url = url.Trim('<', '>', ' ');

                    // Get and sanitize the relation
                    var relation = split[1];
                    relation = relation.Split('=')[1];
                    relation = relation.Trim('"');

                    if (relation == "self")
                    {
                        headerInfo.RequestUri = new Uri(url);
                    }
                    else if (relation == "next")
                    {
                        headerInfo.NextPage = new Uri(url);
                    }
                    else if (relation == "prev")
                    {
                        headerInfo.PrevPage = new Uri(url);
                    }
                }
            }
            return headerInfo;
        }



    }
}
