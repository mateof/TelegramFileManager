using System.Text.RegularExpressions;
using TelegramDownloader.Models;
using TelegramDownloader.Services.Upload;

namespace Test
{
    /// <summary>
    /// Covers the pure rules behind uploading a server folder to a channel:
    /// the single-pass tree walk that feeds the task totals, the duplicate
    /// skip rule and the per-folder index snapshot that replaces one MongoDB
    /// lookup per file.
    /// </summary>
    public class FolderUploadPlanningTest
    {
        private string _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "tfm-upload-plan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(_root, true);
            }
            catch { /* best effort */ }
        }

        private string Touch(string relative, int bytes)
        {
            string full = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, new byte[bytes]);
            return full;
        }

        /// <summary>The implementation the walk replaced, kept to prove equivalence.</summary>
        private static (int count, long size) LegacyTotals(string dir)
        {
            var all = new DirectoryInfo(dir).GetFiles("*.*", SearchOption.AllDirectories)
                .Where(x => !x.Attributes.HasFlag(FileAttributes.Hidden) && !x.Attributes.HasFlag(FileAttributes.Directory)
                            && !x.FullName.Contains("@eaDir") && x.Length > 0);
            return (all.Count(), all.Sum(x => x.Length));
        }

        [Test]
        public void CountUploadableFiles_applies_the_same_filters_as_before()
        {
            Touch("a.txt", 3);
            Touch("empty.txt", 0);
            Touch(Path.Combine("sub", "b.bin"), 5);
            Touch(Path.Combine("sub", "deeper", "noext"), 7);
            Touch(Path.Combine("@eaDir", "thumb.jpg"), 4);
            Touch(Path.Combine("sub", "@eaDir", "thumb2.jpg"), 2);
            string hidden = Touch(Path.Combine("sub", ".hidden"), 9);
            File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);

            var totals = FolderUploadPlanning.CountUploadableFiles(_root);

            Assert.That(totals.Count, Is.EqualTo(3));
            Assert.That(totals.Size, Is.EqualTo(15));
            var legacy = LegacyTotals(_root);
            Assert.That((totals.Count, totals.Size), Is.EqualTo(legacy));
        }

        [Test]
        public void CountUploadableFiles_matches_legacy_on_a_large_tree()
        {
            // ~1000 files spread over nested folders, like the reported case.
            var rnd = new Random(42);
            for (int d = 0; d < 20; d++)
                for (int f = 0; f < 50; f++)
                    Touch(Path.Combine($"d{d % 5}", $"s{d}", $"f{f}.dat"), rnd.Next(0, 4));

            var totals = FolderUploadPlanning.CountUploadableFiles(_root);

            Assert.That((totals.Count, totals.Size), Is.EqualTo(LegacyTotals(_root)));
        }

        [Test]
        public void CountUploadableFiles_of_an_empty_folder_is_zero()
        {
            Assert.That(FolderUploadPlanning.CountUploadableFiles(_root), Is.EqualTo(new UploadTotals(0, 0)));
        }

        [TestCase("/x/a.txt", FileAttributes.Normal, 1, ExpectedResult = true)]
        [TestCase("/x/a.txt", FileAttributes.Normal, 0, ExpectedResult = false)]
        [TestCase("/x/a.txt", FileAttributes.Hidden, 1, ExpectedResult = false)]
        [TestCase("/x/@eaDir/a.txt", FileAttributes.Normal, 1, ExpectedResult = false)]
        [TestCase("/x/a@eaDir.txt", FileAttributes.Normal, 1, ExpectedResult = false)]
        public bool IsCountedFile(string fullName, FileAttributes attributes, long length)
            => FolderUploadPlanning.IsCountedFile(fullName, attributes, length);

        [Test]
        public void ShouldSkipFile_follows_the_duplicate_rules()
        {
            var local = new DateTime(2025, 1, 10, 12, 0, 0, DateTimeKind.Utc);
            var newer = new BsonFileManagerModel { DateModified = local.AddMinutes(1) };
            var older = new BsonFileManagerModel { DateModified = local.AddMinutes(-1) };
            var same = new BsonFileManagerModel { DateModified = local };

            Assert.Multiple(() =>
            {
                Assert.That(FolderUploadPlanning.ShouldSkipFile(10, local, null), Is.False, "new file is uploaded");
                Assert.That(FolderUploadPlanning.ShouldSkipFile(0, local, null), Is.True, "empty file is never sent");
                Assert.That(FolderUploadPlanning.ShouldSkipFile(10, local, newer), Is.True, "indexed copy is newer");
                Assert.That(FolderUploadPlanning.ShouldSkipFile(10, local, older), Is.False, "indexed copy is older: replace");
                Assert.That(FolderUploadPlanning.ShouldSkipFile(10, local, same), Is.False, "same date is re-uploaded, as before");
            });
        }

        [Test]
        public void ChildPathPrefix_matches_how_entry_paths_are_built()
        {
            foreach (var folder in new[] { "/", "/a/", "/a/b c/", "/a" })
            {
                string child = Path.Combine(folder, "file.txt");
                Assert.That(child, Does.StartWith(FolderUploadPlanning.ChildPathPrefix(folder)), folder);
                Assert.That(FolderUploadPlanning.ChildPathPrefix(folder) + "file.txt", Is.EqualTo(child), folder);
            }
        }

        [Test]
        public void DirectChildrenRegex_selects_only_direct_children_and_escapes_names()
        {
            const string folder = "/My (stuff) [2024] +.x$/";
            var regex = new Regex(FolderUploadPlanning.DirectChildrenRegex(folder));

            Assert.Multiple(() =>
            {
                Assert.That(regex.IsMatch(folder + "a.mkv"), Is.True);
                Assert.That(regex.IsMatch(folder + "Sub"), Is.True, "a child folder entry");
                Assert.That(regex.IsMatch(folder + "Sub/a.mkv"), Is.False, "grandchild");
                Assert.That(regex.IsMatch("/My (stuff) [2024] +.x$x/a.mkv"), Is.False, "sibling sharing the prefix");
                Assert.That(regex.IsMatch("/other" + folder + "a.mkv"), Is.False, "anchored");
            });
        }

        [Test]
        public void DestinationCache_tracks_snapshot_and_own_writes()
        {
            var cache = new UploadDestinationCache();
            const string folder = "/dest/";
            Assert.That(cache.IsFolderLoaded(folder), Is.False);

            var first = new BsonFileManagerModel { FilePath = "/dest/a", Name = "a" };
            var dup = new BsonFileManagerModel { FilePath = "/dest/a", Name = "dup" };
            cache.LoadFolder(folder, new[] { first, dup, new BsonFileManagerModel { FilePath = null! } });

            Assert.That(cache.IsFolderLoaded(folder), Is.True);
            Assert.That(cache.Get("/dest/a"), Is.SameAs(first), "first entry wins, like FirstOrDefault");
            Assert.That(cache.Get("/dest/b"), Is.Null);

            var created = new BsonFileManagerModel { FilePath = "/dest/b" };
            cache.Set("/dest/b", created);
            Assert.That(cache.Get("/dest/b"), Is.SameAs(created));

            cache.Remove("/dest/a");
            Assert.That(cache.Get("/dest/a"), Is.Null);
        }

        [Test]
        public void DestinationCache_parent_can_be_invalidated()
        {
            var cache = new UploadDestinationCache();
            var parent = new BsonFileManagerModel { Id = "p", HasChild = true };
            cache.SetParent("/dest/", parent);
            Assert.That(cache.GetParent("/dest/"), Is.SameAs(parent));

            cache.InvalidateParent("/dest/");
            Assert.That(cache.GetParent("/dest/"), Is.Null);
        }

        [Test]
        public void Snapshot_dedupe_gives_the_same_upload_set_as_per_file_lookups()
        {
            var local = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
            var index = new Dictionary<string, BsonFileManagerModel>
            {
                ["/dest/keep.mkv"] = new() { FilePath = "/dest/keep.mkv", DateModified = local.AddDays(1) },
                ["/dest/replace.mkv"] = new() { FilePath = "/dest/replace.mkv", DateModified = local.AddDays(-1) },
                ["/dest/sub/deep.mkv"] = new() { FilePath = "/dest/sub/deep.mkv", DateModified = local.AddDays(1) },
            };
            var files = new (string name, long size)[]
            {
                ("keep.mkv", 10), ("replace.mkv", 10), ("new.mkv", 10), ("empty.txt", 0), ("deep.mkv", 10)
            };

            // What the per-file loop decided with one query each.
            var perFile = files.Where(f => !FolderUploadPlanning.ShouldSkipFile(
                f.size, local, index.GetValueOrDefault(Path.Combine("/dest/", f.name)))).Select(f => f.name).ToList();

            // What the run decides from a single children query of /dest/.
            var regex = new Regex(FolderUploadPlanning.DirectChildrenRegex("/dest/"));
            var cache = new UploadDestinationCache();
            cache.LoadFolder("/dest/", index.Values.Where(e => regex.IsMatch(e.FilePath)));
            var bulk = files.Where(f => !FolderUploadPlanning.ShouldSkipFile(
                f.size, local, cache.Get(Path.Combine("/dest/", f.name)))).Select(f => f.name).ToList();

            Assert.That(bulk, Is.EqualTo(perFile));
            Assert.That(bulk, Is.EqualTo(new[] { "replace.mkv", "new.mkv", "deep.mkv" }));
        }
    }
}
