using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using TelegramDownloader.Controllers.Api.V1;
using TelegramDownloader.Data;
using TelegramDownloader.Models;
using TelegramDownloader.Services;

namespace Test
{
    /// <summary>
    /// Covers the `states` filter of POST /api/v1/transfers/clear. The parser is
    /// a private static helper, so it is reached by reflection rather than by
    /// widening its visibility just for the test.
    /// </summary>
    public class TransfersClearStatesTest
    {
        private static readonly MethodInfo Parse =
            typeof(TransfersController).GetMethod(
                "TryParseStates", BindingFlags.NonPublic | BindingFlags.Static)!;

        private static bool TryParse(string? states, out HashSet<StateTask>? parsed, out string? error)
        {
            object?[] args = { states, null, null };
            bool ok = (bool)Parse.Invoke(null, args)!;
            parsed = (HashSet<StateTask>?)args[1];
            error = (string?)args[2];
            return ok;
        }

        [Test]
        public void NoFilterMeansEveryFinishedState()
        {
            foreach (string? empty in new[] { null, "", "   " })
            {
                Assert.That(TryParse(empty, out var parsed, out _), Is.True);
                Assert.That(parsed, Is.Null, "null is what tells the service to clear everything");
            }
        }

        [Test]
        public void ParsesTheStatesTheAppSends()
        {
            Assert.That(TryParse("completed", out var one, out _), Is.True);
            Assert.That(one, Is.EquivalentTo(new[] { StateTask.Completed }));

            Assert.That(TryParse("completed,failed", out var two, out _), Is.True);
            Assert.That(two, Is.EquivalentTo(new[] { StateTask.Completed, StateTask.Error }));
        }

        [Test]
        public void AcceptsAliasesCasingAndSpacing()
        {
            Assert.That(TryParse(" Completed , FAILED ", out var parsed, out _), Is.True);
            Assert.That(parsed, Is.EquivalentTo(new[] { StateTask.Completed, StateTask.Error }));

            Assert.That(TryParse("cancelled", out var cancelled, out _), Is.True);
            Assert.That(cancelled, Is.EquivalentTo(new[] { StateTask.Canceled }));
        }

        [Test]
        public void RejectsUnknownStates()
        {
            Assert.That(TryParse("done", out _, out var error), Is.False);
            Assert.That(error, Does.Contain("done"));
        }

        [Test]
        public void RejectsWorkingBecauseARunningTransferIsCancelledNotCleared()
        {
            Assert.That(TryParse("working", out _, out var error), Is.False);
            Assert.That(error, Is.Not.Null);
        }
    }

    /// <summary>
    /// The other half of the filter: that the service actually keeps what the
    /// caller did not ask to remove, and never touches a running transfer.
    /// </summary>
    public class ClearUploadCompletedTest
    {
        private static TransactionInfoService Service(params StateTask[] states)
        {
            TransactionInfoService tis = new TransactionInfoService(
                NullLogger<IFileService>.Instance);
            foreach (StateTask s in states)
                tis.uploadModels.Add(new UploadModel { state = s, name = s.ToString() });
            return tis;
        }

        [Test]
        public void ClearingCompletedLeavesTheFailuresOnScreen()
        {
            TransactionInfoService tis = Service(
                StateTask.Completed, StateTask.Error, StateTask.Working, StateTask.Paused);

            tis.clearUploadCompleted(new HashSet<StateTask> { StateTask.Completed });

            Assert.That(tis.uploadModels.Select(u => u.state),
                Is.EquivalentTo(new[] { StateTask.Error, StateTask.Working, StateTask.Paused }));
        }

        [Test]
        public void ClearingCompletedAndFailedLeavesTheQueue()
        {
            TransactionInfoService tis = Service(
                StateTask.Completed, StateTask.Error, StateTask.Pending, StateTask.Working);

            tis.clearUploadCompleted(new HashSet<StateTask> { StateTask.Completed, StateTask.Error });

            Assert.That(tis.uploadModels.Select(u => u.state),
                Is.EquivalentTo(new[] { StateTask.Pending, StateTask.Working }));
        }

        [Test]
        public void NoFilterStillClearsEverythingButTheRunningOnes()
        {
            TransactionInfoService tis = Service(
                StateTask.Completed, StateTask.Error, StateTask.Paused,
                StateTask.Pending, StateTask.Canceled, StateTask.Working);

            tis.clearUploadCompleted();

            Assert.That(tis.uploadModels.Select(u => u.state),
                Is.EquivalentTo(new[] { StateTask.Working }));
        }
    }
}
