using System.IO.Hashing;
using TelegramDownloader.Services.Upload;

namespace Test
{
    /// <summary>
    /// The hash computed while uploading must be byte for byte the value the
    /// old two-pass code stored (including its 512-byte block quirk), whatever
    /// the read pattern: sequential, restarted by a retry, split in parts.
    /// </summary>
    public class UploadHashingTest
    {
        private string _dir = null!;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "tfm-hash-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch { /* best effort */ }
        }

        /// <summary>Literal copy of the removed FileService.ComputeXXHash64 loop.</summary>
        private static string OldTwoPassHash(string filePath)
        {
            var hashAlgorithm = new XxHash64();
            var buffer = new Span<byte>(new byte[512]);
            using (Stream entryStream = File.OpenRead(filePath))
            {
                while (entryStream.Read(buffer) > 0)
                {
                    hashAlgorithm.Append(buffer);
                }
            }
            return BitConverter.ToString(hashAlgorithm.GetHashAndReset()).Replace("-", string.Empty);
        }

        private string WriteFile(int length, int seed)
        {
            var data = new byte[length];
            new Random(seed).NextBytes(data);
            var path = Path.Combine(_dir, $"f{length}-{seed}.bin");
            File.WriteAllBytes(path, data);
            return path;
        }

        private static string Complete(UploadHashAccumulator acc, string path)
        {
            using var source = File.OpenRead(path);
            return acc.Complete(source);
        }

        public static readonly int[] Lengths = { 0, 1, 100, 511, 512, 513, 1000, 1024, 4095, 4096, 4097, 70_001, 262_144, 300_007 };

        [TestCaseSource(nameof(Lengths))]
        public void ReferenceAlgorithm_EqualsOldCode(int length)
        {
            var path = WriteFile(length, length);
            Assert.That(StoredXxHash.ComputeFile(path), Is.EqualTo(OldTwoPassHash(path)));
        }

        [TestCaseSource(nameof(Lengths))]
        public void SequentialUpload_EqualsOldHash_WithoutExtraRead(int length)
        {
            var path = WriteFile(length, length + 1);
            var acc = new UploadHashAccumulator(length);
            using (var fs = File.OpenRead(path))
            using (var hs = new HashingReadStream(fs, acc, 0, leaveOpen: true))
                hs.CopyTo(Stream.Null, 81920);

            Assert.That(Complete(acc, path), Is.EqualTo(OldTwoPassHash(path)));
            Assert.That(acc.CatchUpBytes, Is.EqualTo(0));
        }

        [TestCase(70_001)]
        [TestCase(300_007)]
        public async Task OddReadSizes_Async_EqualOldHash(int length)
        {
            var path = WriteFile(length, 7);
            var acc = new UploadHashAccumulator(length);
            var rnd = new Random(3);
            await using (var fs = File.OpenRead(path))
            await using (var hs = new HashingReadStream(fs, acc))
            {
                var buffer = new byte[10_000];
                while (await hs.ReadAsync(buffer.AsMemory(0, rnd.Next(1, buffer.Length))) > 0) { }
            }
            Assert.That(Complete(acc, path), Is.EqualTo(OldTwoPassHash(path)));
        }

        [TestCase(300_007)]
        [TestCase(4097)]
        public void RetryRestartingTheStream_DoesNotHashTwice(int length)
        {
            var path = WriteFile(length, 11);
            var acc = new UploadHashAccumulator(length);

            // First attempt fails after reading part of the file.
            using (var fs = File.OpenRead(path))
            using (var hs = new HashingReadStream(fs, acc))
            {
                var buffer = new byte[1500];
                hs.ReadExactly(buffer);
                hs.ReadExactly(buffer);
            }
            // Retry: a new stream from the beginning, read to the end.
            using (var fs = File.OpenRead(path))
            using (var hs = new HashingReadStream(fs, acc))
                hs.CopyTo(Stream.Null);

            Assert.That(Complete(acc, path), Is.EqualTo(OldTwoPassHash(path)));
            Assert.That(acc.CatchUpBytes, Is.EqualTo(0));
        }

        [Test]
        public void SeekBackOnTheSameStream_IsLikeARetry()
        {
            const int length = 50_000;
            var path = WriteFile(length, 12);
            var acc = new UploadHashAccumulator(length);
            using (var fs = File.OpenRead(path))
            using (var hs = new HashingReadStream(fs, acc))
            {
                hs.ReadExactly(new byte[20_000]);
                hs.Position = 0;
                hs.CopyTo(Stream.Null);
            }
            Assert.That(Complete(acc, path), Is.EqualTo(OldTwoPassHash(path)));
        }

        [TestCase(300_007, 65_536)]
        [TestCase(300_000, 100_000)]
        [TestCase(131_072, 65_536)]
        public void SplitParts_WithRetryOfOnePart_EqualOldHash(int length, int partSize)
        {
            var path = WriteFile(length, 13);
            var acc = new UploadHashAccumulator(length);
            int parts = (length + partSize - 1) / partSize;
            for (int i = 0; i < parts; i++)
            {
                long start = (long)i * partSize;
                long len = Math.Min(partSize, length - start);
                int attempts = i == 1 ? 2 : 1; // part 2 fails once half way
                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    using var chunk = new TelegramDownloader.Services.Streams.FileChunkStream(path, start, len);
                    using var hs = new HashingReadStream(chunk, acc, start, leaveOpen: true);
                    if (attempt == 0 && attempts == 2)
                        hs.ReadExactly(new byte[len / 2]);
                    else
                        hs.CopyTo(Stream.Null, 8192);
                }
            }
            Assert.That(Complete(acc, path), Is.EqualTo(OldTwoPassHash(path)));
            Assert.That(acc.CatchUpBytes, Is.EqualTo(0));
        }

        [Test]
        public void SkippedPart_IsReadBackOnCompletion()
        {
            // Disk split reusing an existing part: that range is never read.
            const int length = 200_003;
            var path = WriteFile(length, 14);
            var acc = new UploadHashAccumulator(length);
            var data = File.ReadAllBytes(path);
            acc.Observe(0, data.AsSpan(0, 50_000));
            acc.Observe(100_000, data.AsSpan(100_000, 100_003)); // gap: ignored

            Assert.That(Complete(acc, path), Is.EqualTo(OldTwoPassHash(path)));
            Assert.That(acc.CatchUpBytes, Is.EqualTo(length - 50_000));
        }

        [TestCase(0)]
        [TestCase(300)]
        [TestCase(300_007)]
        public async Task ComputeFileAsync_EqualsOldHash(int length)
        {
            var path = WriteFile(length, 15);
            Assert.That(await StoredXxHash.ComputeFileAsync(path), Is.EqualTo(OldTwoPassHash(path)));
        }

        [Test]
        public void Complete_TwiceThrows()
        {
            var path = WriteFile(10, 16);
            var acc = new UploadHashAccumulator(10);
            Complete(acc, path);
            Assert.Throws<InvalidOperationException>(() => Complete(acc, path));
        }
    }
}
