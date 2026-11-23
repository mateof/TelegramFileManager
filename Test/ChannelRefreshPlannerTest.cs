using TelegramDownloader.Models;
using TelegramDownloader.Services;

namespace Test
{
    /// <summary>
    /// The pure steps of a channel refresh. Each one must produce exactly what
    /// the original inline code did, so the old algorithms are kept here as
    /// reference implementations and compared against.
    /// </summary>
    public class ChannelRefreshPlannerTest
    {
        private static TelegramChatDocuments Doc(int id, string name, DocumentType type = DocumentType.Document) =>
            new TelegramChatDocuments { id = id, name = name, documentType = type, extension = Path.GetExtension(name) };

        // The pre-optimisation renaming loop, verbatim.
        private static List<string> ReferenceNames(IEnumerable<string> existing, IEnumerable<string> incoming)
        {
            var nameCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string n in existing) nameCount[n] = 1;
            var output = new List<string>();
            foreach (string baseName in incoming)
            {
                var name = baseName;
                int count = 0;
                while (nameCount.ContainsKey(name))
                {
                    count++;
                    name = $"{baseName}({count})";
                }
                output.Add(name);
                nameCount[name] = 1;
                if (count == 0) nameCount[baseName] = 1;
            }
            return output;
        }

        private static List<string> Assign(IEnumerable<string> existing, IEnumerable<string> incoming)
        {
            var docs = incoming.Select((n, i) => Doc(i, n)).ToList();
            ChannelRefreshPlanner.AssignUniqueNames(existing, docs);
            return docs.Select(d => d.name).ToList();
        }

        [Test]
        public void NamesAreSuffixedAgainstExistingAndEarlierOnes()
        {
            var names = Assign(new[] { "a.pdf", "b.mp3" }, new[] { "a.pdf", "A.PDF", "c.zip", "c.zip", "b.mp3" });
            Assert.That(names, Is.EqualTo(new[] { "a.pdf(1)", "A.PDF(2)", "c.zip", "c.zip(1)", "b.mp3(1)" }));
        }

        [Test]
        public void SkipsSuffixesThatAreAlreadyTaken()
        {
            var names = Assign(new[] { "x", "x(1)", "x(3)" }, new[] { "x", "x", "x", "x(1)" });
            Assert.That(names, Is.EqualTo(ReferenceNames(new[] { "x", "x(1)", "x(3)" }, new[] { "x", "x", "x", "x(1)" })));
            Assert.That(names, Is.EqualTo(new[] { "x(2)", "x(4)", "x(5)", "x(1)(1)" }));
        }

        [Test]
        public void MatchesTheOriginalAlgorithmOnRandomInput()
        {
            var rnd = new Random(1234);
            string[] pool = { "a", "A", "a(1)", "b", "a(2)", "B(1)", "c", "a(1)(1)" };
            for (int round = 0; round < 200; round++)
            {
                var existing = Enumerable.Range(0, rnd.Next(0, 6)).Select(_ => pool[rnd.Next(pool.Length)]).ToList();
                var incoming = Enumerable.Range(0, rnd.Next(0, 40)).Select(_ => pool[rnd.Next(pool.Length)]).ToList();
                Assert.That(Assign(existing, incoming), Is.EqualTo(ReferenceNames(existing, incoming)), $"round {round}");
            }
        }

        [Test]
        public void ManyFilesWithTheSameNameStayLinear()
        {
            var incoming = Enumerable.Repeat("track.mp3", 20000).ToList();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var names = Assign(Array.Empty<string>(), incoming);
            sw.Stop();
            Assert.That(names[0], Is.EqualTo("track.mp3"));
            Assert.That(names[^1], Is.EqualTo("track.mp3(19999)"));
            Assert.That(names.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(20000));
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(2000));
        }

        [Test]
        public void MergeKeepsPhaseOrderAndFirstPhaseWins()
        {
            var docs = new List<TelegramChatDocuments> { Doc(10, "doc.mkv"), Doc(8, "a.pdf") };
            var audio = new List<TelegramChatDocuments> { Doc(9, "s.mp3", DocumentType.Audio) };
            var video = new List<TelegramChatDocuments> { Doc(10, "video_1.mp4", DocumentType.Video), Doc(7, "v.mp4", DocumentType.Video) };

            var merged = ChannelRefreshPlanner.MergePhaseResults(new[] { docs, audio, video });

            Assert.That(merged.Select(d => d.id), Is.EqualTo(new[] { 10, 8, 9, 7 }));
            Assert.That(merged[0].name, Is.EqualTo("doc.mkv"));
        }

        [Test]
        public void SelectNewDropsIndexedMessagesAndKeepsOrder()
        {
            var docs = new[] { Doc(5, "a"), Doc(3, "b"), Doc(9, "c"), Doc(1, "d") };
            var fresh = ChannelRefreshPlanner.SelectNew(docs, new HashSet<int> { 3, 1 });
            Assert.That(fresh.Select(d => d.id), Is.EqualTo(new[] { 5, 9 }));
        }

        [Test]
        public void RootEntryHasTheSameFieldsAsBefore()
        {
            var created = new DateTime(2024, 1, 2, 3, 4, 5);
            var tcd = new TelegramChatDocuments { id = 42, name = "song.flac", extension = ".flac", fileSize = 1234, creationDate = created, modifiedDate = created.AddDays(1) };

            var m = ChannelRefreshPlanner.ToRootFileEntry(tcd, "root");

            Assert.Multiple(() =>
            {
                Assert.That(m.MessageId, Is.EqualTo(42));
                Assert.That(m.Name, Is.EqualTo("song.flac"));
                Assert.That(m.Size, Is.EqualTo(1234));
                Assert.That(m.IsFile, Is.True);
                Assert.That(m.HasChild, Is.False);
                Assert.That(m.DateCreated, Is.EqualTo(created));
                Assert.That(m.DateModified, Is.EqualTo(created.AddDays(1)));
                Assert.That(m.FilterPath, Is.EqualTo("/"));
                Assert.That(m.FilterId, Is.EqualTo("root/"));
                Assert.That(m.ParentId, Is.EqualTo("root"));
                Assert.That(m.FilePath, Is.EqualTo("/song.flac"));
                Assert.That(m.Type, Is.EqualTo(".flac"));
                Assert.That(m.isSplit, Is.False);
                Assert.That(m.isEncrypted, Is.False);
                Assert.That(m.Id, Is.Null);
            });
        }
    }
}
