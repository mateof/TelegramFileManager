using TelegramDownloader.Services;

namespace Test
{
    /// <summary>The in-flight refresh registry: claiming a channel must be atomic.</summary>
    public class ChannelRefreshRegistryTest
    {
        [Test]
        public void SecondClaimOfTheSameChannelFailsUntilReleased()
        {
            var registry = new ChannelRefreshRegistry();

            Assert.That(registry.TryBegin("1"), Is.True);
            Assert.That(registry.TryBegin("1"), Is.False);
            Assert.That(registry.TryBegin("2"), Is.True);
            Assert.That(registry.IsRunning("1"), Is.True);

            registry.End("1");

            Assert.That(registry.IsRunning("1"), Is.False);
            Assert.That(registry.IsRunning("2"), Is.True);
            Assert.That(registry.TryBegin("1"), Is.True);
        }

        [Test]
        public void EndingAChannelThatIsNotRunningIsHarmless()
        {
            var registry = new ChannelRefreshRegistry();
            Assert.DoesNotThrow(() => registry.End("nope"));
            Assert.That(registry.IsRunning("nope"), Is.False);
        }

        [Test]
        public void OnlyOneOfManySimultaneousClaimsWins()
        {
            for (int round = 0; round < 50; round++)
            {
                var registry = new ChannelRefreshRegistry();
                using var start = new ManualResetEventSlim(false);
                int winners = 0;
                var threads = Enumerable.Range(0, 16).Select(_ => new Thread(() =>
                {
                    start.Wait();
                    if (registry.TryBegin("42"))
                        Interlocked.Increment(ref winners);
                })).ToList();
                threads.ForEach(t => t.Start());
                start.Set();
                threads.ForEach(t => t.Join());

                Assert.That(winners, Is.EqualTo(1), $"round {round}");
            }
        }
    }
}
