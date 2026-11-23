using Syncfusion.Blazor.FileManager;
using TelegramDownloader.Models;
using TelegramDownloader.Services.Upload;

namespace Test
{
    /// <summary>
    /// The upload preview walks the selection with the same rule and keys as
    /// the real upload, so both make the same decision for every item.
    /// </summary>
    public class UploadPreviewPlannerTest
    {
        private static readonly DateTime T0 = new(2024, 1, 1, 12, 0, 0);

        private static FileManagerDirectoryContent LocalFile(string filterPath, string name, long size, DateTime modified)
            => new() { FilterPath = filterPath, Name = name, IsFile = true, Size = size, DateModified = modified };

        private static FileManagerDirectoryContent LocalFolder(string filterPath, string name)
            => new() { FilterPath = filterPath, Name = name, IsFile = false };

        private static BsonFileManagerModel Indexed(string filePath, bool isFile, DateTime modified)
            => new() { FilePath = filePath, IsFile = isFile, DateModified = modified, Name = Path.GetFileName(filePath) };

        /// <summary>Local tree: relative folder path -> its content.</summary>
        private static Dictionary<string, List<FileManagerDirectoryContent>> LocalTree() => new()
        {
            ["/music/album/"] = new()
            {
                LocalFile("/music/album/", "01.flac", 100, T0),          // new
                LocalFile("/music/album/", "02.flac", 200, T0),          // replace (index older)
                LocalFile("/music/album/", "03.flac", 300, T0),          // skip-newer
                LocalFile("/music/album/", "empty.txt", 0, T0),          // skip-empty
                LocalFolder("/music/album/", "cd2"),                     // create-folder
            },
            ["/music/album/cd2/"] = new()
            {
                LocalFile("/music/album/cd2/", "01.flac", 400, T0),      // new (folder is new)
            },
        };

        private static List<BsonFileManagerModel> Index() => new()
        {
            Indexed("/dest/album", false, T0),
            Indexed("/dest/album/02.flac", true, T0.AddDays(-1)),
            Indexed("/dest/album/03.flac", true, T0.AddDays(1)),
            Indexed("/dest/single.mp3", true, T0),                         // equal date: replace
        };

        private static List<FileManagerDirectoryContent> Selection() => new()
        {
            LocalFolder("/music/", "album"),                                // existing-folder
            LocalFile("/music/", "single.mp3", 50, T0),
        };

        private static IEnumerable<BsonFileManagerModel> Children(List<BsonFileManagerModel> index, string currentPath)
        {
            var regex = new System.Text.RegularExpressions.Regex(FolderUploadPlanning.DirectChildrenRegex(currentPath));
            return index.Where(x => regex.IsMatch(x.FilePath));
        }

        private static UploadPlanSource Source(List<BsonFileManagerModel> index, Dictionary<string, List<FileManagerDirectoryContent>> tree)
            => new()
            {
                LoadDestinationChildren = (path, _) => Task.FromResult(Children(index, path)),
                ListLocalFolder = rel => tree.TryGetValue(rel, out var items) ? items : new List<FileManagerDirectoryContent>(),
            };

        /// <summary>
        /// The decisions of FileService.UploadFileFromServer, transcribed: the
        /// folder snapshot check, the re-read before sending, the replacement
        /// when an entry was read, and folder creation when the key is unknown.
        /// </summary>
        private static Dictionary<string, string> RealUploadDecisions(
            string currentPath, List<FileManagerDirectoryContent> files, List<BsonFileManagerModel> index,
            Dictionary<string, List<FileManagerDirectoryContent>> tree, UploadDestinationCache cache, Dictionary<string, string> result)
        {
            if (currentPath != null && files.Count > 0 && !cache.IsFolderLoaded(currentPath))
                cache.LoadFolder(currentPath, Children(index, currentPath));
            foreach (var file in files)
            {
                var filePath = file.IsFile ? file.FilterPath.Replace("\\", "/") + file.Name : file.FilterPath.Replace("\\", "/") + file.Name + "/";
                if (file.IsFile)
                {
                    string entryPath = Path.Combine(currentPath!, file.Name);
                    if (FolderUploadPlanning.ShouldSkipFile(file.Size, file.DateModified, cache.Get(entryPath)))
                    {
                        result[filePath] = file.Size == 0 ? "skip-empty" : "skip-newer";
                        continue;
                    }
                    var savedFile = index.FirstOrDefault(x => x.FilePath == entryPath);
                    if (FolderUploadPlanning.ShouldSkipFile(file.Size, file.DateModified, savedFile))
                    {
                        result[filePath] = file.Size == 0 ? "skip-empty" : "skip-newer";
                        continue;
                    }
                    result[filePath] = savedFile != null ? "replace" : "new";
                    if (savedFile != null) cache.Remove(entryPath);
                }
                string modelKey = Path.Combine(currentPath!, file.Name);
                if (!file.IsFile)
                    result[filePath] = cache.Get(modelKey) == null ? "create-folder" : "existing-folder";
                if (cache.Get(modelKey) == null)
                    cache.Set(modelKey, new BsonFileManagerModel { FilePath = modelKey, IsFile = file.IsFile, DateModified = DateTime.Now });
                if (!file.IsFile)
                    RealUploadDecisions(Path.Combine(currentPath!, file.Name) + "/", tree.TryGetValue(filePath, out var c) ? c : new(), index, tree, cache, result);
            }
            return result;
        }

        [Test]
        public async Task Preview_MakesTheSameDecisionsAsTheUpload()
        {
            var index = Index();
            var tree = LocalTree();

            var preview = await UploadPreviewPlanner.PlanAsync("/dest/", Selection(), Source(index, tree));
            var real = RealUploadDecisions("/dest/", Selection(), index, tree, new UploadDestinationCache(), new());

            var planned = preview.Items.ToDictionary(i => i.LocalPath, i => i.Action);
            Assert.That(planned, Is.EquivalentTo(real));
        }

        [Test]
        public async Task Preview_CountsAndBytes()
        {
            var preview = await UploadPreviewPlanner.PlanAsync("/dest/", Selection(), Source(Index(), LocalTree()));

            Assert.Multiple(() =>
            {
                Assert.That(preview.NewFiles, Is.EqualTo(2));            // 01.flac, cd2/01.flac
                Assert.That(preview.NewBytes, Is.EqualTo(500));
                Assert.That(preview.ReplacedFiles, Is.EqualTo(2));       // 02.flac, single.mp3
                Assert.That(preview.ReplacedBytes, Is.EqualTo(250));
                Assert.That(preview.FilesToUpload, Is.EqualTo(4));
                Assert.That(preview.BytesToUpload, Is.EqualTo(750));
                Assert.That(preview.SkippedEmpty, Is.EqualTo(1));
                Assert.That(preview.SkippedNewerInChannel, Is.EqualTo(1));
                Assert.That(preview.SkippedFiles, Is.EqualTo(2));
                Assert.That(preview.SkippedBytes, Is.EqualTo(300));
                Assert.That(preview.FoldersToCreate, Is.EqualTo(1));
                Assert.That(preview.ExistingFolders, Is.EqualTo(1));
                Assert.That(preview.ItemsTruncated, Is.False);
            });
            var item = preview.Items.Single(i => i.LocalPath == "/music/album/cd2/01.flac");
            Assert.That(item.TargetPath, Is.EqualTo(Path.Combine("/dest/album/cd2/", "01.flac")));
        }

        [Test]
        public async Task Preview_UsesTheLocalLengthForBytesToSend()
        {
            var tree = LocalTree();
            var source = new UploadPlanSource
            {
                LoadDestinationChildren = (path, _) => Task.FromResult(Children(Index(), path)),
                ListLocalFolder = rel => tree.TryGetValue(rel, out var items) ? items : new(),
                LocalFileLength = rel => rel == "/music/single.mp3" ? 55 : null,
            };
            var preview = await UploadPreviewPlanner.PlanAsync("/dest/", Selection(), source);
            Assert.That(preview.ReplacedBytes, Is.EqualTo(200 + 55));
        }

        [Test]
        public async Task Preview_CapsItemsButNotCounters_AndSinksEveryUpload()
        {
            var sink = new List<UploadPreviewItem>();
            var preview = await UploadPreviewPlanner.PlanAsync("/dest/", Selection(), Source(Index(), LocalTree()), maxItems: 2, filesToUpload: sink);
            Assert.That(preview.Items, Has.Count.EqualTo(2));
            Assert.That(preview.ItemsTruncated, Is.True);
            Assert.That(preview.FilesToUpload, Is.EqualTo(4));
            Assert.That(sink.Select(i => i.Action), Is.All.AnyOf("new", "replace"));
            Assert.That(sink, Has.Count.EqualTo(4));
        }

        [Test]
        public void DecideFile_AgreesWithShouldSkipFile()
        {
            var older = Indexed("/x", true, T0.AddDays(-1));
            var newer = Indexed("/x", true, T0.AddDays(1));
            var same = Indexed("/x", true, T0);
            foreach (var size in new long[] { 0, 1 })
            foreach (var existing in new[] { null, older, newer, same })
            {
                var action = FolderUploadPlanning.DecideFile(size, T0, existing);
                Assert.That(FolderUploadPlanning.IsSkip(action), Is.EqualTo(FolderUploadPlanning.ShouldSkipFile(size, T0, existing)));
            }
            Assert.That(FolderUploadPlanning.DecideFile(0, T0, newer), Is.EqualTo(UploadFileAction.SkipEmpty));
            Assert.That(FolderUploadPlanning.DecideFile(1, T0, null), Is.EqualTo(UploadFileAction.New));
            Assert.That(FolderUploadPlanning.DecideFile(1, T0, older), Is.EqualTo(UploadFileAction.Replace));
            Assert.That(FolderUploadPlanning.DecideFile(1, T0, same), Is.EqualTo(UploadFileAction.Replace));
            Assert.That(FolderUploadPlanning.DecideFile(1, T0, newer), Is.EqualTo(UploadFileAction.SkipNewerInChannel));
        }
    }
}
