using Ej2 = Syncfusion.EJ2.FileManager.Base;
using Syncfusion.EJ2.FileManager.PhysicalFileProvider;
using TelegramDownloader.Models.FileSystem;
using TelegramDownloader.Services.Upload;

namespace Test
{
    /// <summary>
    /// <see cref="LocalFolderLister"/> replaces the EJ2 PhysicalFileProvider the
    /// folder upload used to list local folders with. On a real folder tree it
    /// must give exactly what the old code gave: the provider's listing, minus
    /// the <c>@eaDir</c> folders, projected the way the service projected it.
    /// </summary>
    public class LocalFolderListerTest
    {
        private string _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "tfm-lister-" + Guid.NewGuid().ToString("N"), "local");
            Directory.CreateDirectory(_root);

            File.WriteAllText(Path.Combine(_root, "a.txt"), "hello");
            File.WriteAllBytes(Path.Combine(_root, "b.bin"), new byte[1234]);
            Directory.CreateDirectory(Path.Combine(_root, "music", "rock"));
            Directory.CreateDirectory(Path.Combine(_root, "music", "@eaDir"));
            File.WriteAllText(Path.Combine(_root, "music", "song.mp3"), "123");
            File.WriteAllBytes(Path.Combine(_root, "music", "rock", "deep.flac"), new byte[50]);
            File.WriteAllBytes(Path.Combine(_root, "music", "@eaDir", "thumb.jpg"), new byte[7]);
            Directory.CreateDirectory(Path.Combine(_root, "empty"));
            Directory.CreateDirectory(Path.Combine(_root, "@eaDir"));
            Directory.CreateDirectory(Path.Combine(_root, "music", "@eaDir"));

            var hiddenFile = Path.Combine(_root, ".hidden.txt");
            File.WriteAllText(hiddenFile, "x");
            var hiddenInMusic = Path.Combine(_root, "music", ".cover.jpg");
            File.WriteAllBytes(hiddenInMusic, new byte[1000]);
            var hiddenDir = Path.Combine(_root, ".hiddendir");
            Directory.CreateDirectory(hiddenDir);
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(hiddenFile, File.GetAttributes(hiddenFile) | FileAttributes.Hidden);
                File.SetAttributes(hiddenInMusic, File.GetAttributes(hiddenInMusic) | FileAttributes.Hidden);
                new DirectoryInfo(hiddenDir).Attributes |= FileAttributes.Hidden;
            }
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(Path.GetDirectoryName(_root)!, true); } catch { /* best effort */ }
        }

        /// <summary>What FileService.ListLocalFolderForUpload returned before the change.</summary>
        private List<FsEntry> Legacy(string path)
        {
            var operation = new PhysicalFileProvider();
            operation.RootFolder(_root);
            var files = operation.GetFiles(path, false);
            var result = new List<FsEntry>();
            foreach (Ej2.FileManagerDirectoryContent f in files.Files)
            {
                if (!f.IsFile && f.Name == "@eaDir") continue;
                // The projection of the former converToFMD (Type was not copied).
                result.Add(new FsEntry
                {
                    Name = f.Name,
                    Path = f.Path,
                    FilterPath = f.FilterPath,
                    HasChild = f.HasChild,
                    CaseSensitive = f.CaseSensitive,
                    DateCreated = f.DateCreated,
                    DateModified = f.DateModified,
                    FilterId = f.FilterId,
                    IsFile = f.IsFile,
                    ParentId = f.ParentId,
                    PreviousName = f.PreviousName,
                    ShowHiddenItems = f.ShowHiddenItems,
                    Size = f.Size
                });
            }
            return result;
        }

        private static object Key(FsEntry e) => new
        {
            e.Name, e.Path, e.FilterPath, e.HasChild, e.CaseSensitive, Created = e.DateCreated.Ticks, Modified = e.DateModified.Ticks,
            e.FilterId, e.IsFile, e.ParentId, e.PreviousName, e.ShowHiddenItems, e.Size, e.Type, e.Id
        };

        [TestCase("/")]
        [TestCase("/music/")]
        [TestCase("/music/rock/")]
        [TestCase("/empty/")]
        public void ListsLikeThePhysicalFileProvider(string path)
        {
            // Warm-up: NTFS refreshes the folder timestamps cached in the parent's
            // index once a child folder has been opened, so the very first
            // listing after the fixture is written can see slightly older times.
            Legacy(path);
            var expected = Legacy(path);
            var actual = LocalFolderLister.ListForUpload(_root, path);

            Assert.That(actual.Select(Key), Is.EqualTo(expected.Select(Key)));
        }

        [Test]
        public void Root_HasFoldersFirst_SkipsHiddenAndSynologyFolders()
        {
            var sep = Path.DirectorySeparatorChar.ToString();
            var items = LocalFolderLister.ListForUpload(_root, "/");

            Assert.That(items.Where(i => !i.IsFile).Select(i => i.Name), Is.EquivalentTo(new[] { "music", "empty" }));
            Assert.That(items.Where(i => i.IsFile).Select(i => i.Name), Is.EquivalentTo(new[] { "a.txt", "b.bin" }));
            Assert.That(items.TakeWhile(i => !i.IsFile).Count(), Is.EqualTo(2), "folders come first");
            Assert.That(items.All(i => i.FilterPath == sep), Is.True);
            Assert.That(items.Single(i => i.Name == "music").HasChild, Is.True);
            Assert.That(items.Single(i => i.Name == "empty").HasChild, Is.False);
            Assert.That(items.Single(i => i.Name == "b.bin").Size, Is.EqualTo(1234));
            Assert.That(items.Single(i => i.Name == "music").Size, Is.EqualTo(3 + 1000), "direct files only, hidden included");
            Assert.That(items.All(i => i.Type == null), Is.True);
        }

        [Test]
        public void SubFolder_FilterPathIsRelativeToTheRoot()
        {
            var sep = Path.DirectorySeparatorChar;
            var items = LocalFolderLister.ListForUpload(_root, "/music/");

            Assert.That(items.Select(i => i.Name), Is.EqualTo(new[] { "rock", "song.mp3" }));
            Assert.That(items.All(i => i.FilterPath == $"{sep}music{sep}"), Is.True);
        }

        [Test]
        public void PathEscapingTheRoot_IsRejected()
        {
            Assert.Throws<UnauthorizedAccessException>(() => LocalFolderLister.ListForUpload(_root, "/../"));
        }
    }
}
