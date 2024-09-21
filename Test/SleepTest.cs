using NUnit.Framework.Legacy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramDownloader.Models;
using TelegramDownloader.Services;

namespace Test
{
    internal class SleepTest
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public async Task TestSleepAsync()
        {
            GeneralConfigStatic.config.TimeSleepBetweenTransactions = 200;
            var timeSleep = GeneralConfigStatic.config.TimeSleepBetweenTransactions;
            WaitingTime wt = new WaitingTime();
            var initialTime = DateTime.Now;
            await wt.Sleep();
            var totalTime = (DateTime.Now - initialTime).TotalMilliseconds;
            ClassicAssert.GreaterOrEqual(totalTime, GeneralConfigStatic.config.TimeSleepBetweenTransactions);
        }
    }
}
