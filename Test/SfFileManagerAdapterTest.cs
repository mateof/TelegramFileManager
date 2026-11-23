using System.Reflection;
using Syncfusion.Blazor.FileManager;
using TelegramDownloader.Data;
using TelegramDownloader.Models.FileSystem;
using TelegramDownloader.Shared.SyncfusionAdapters;

namespace Test
{
    /// <summary>
    /// The Blazor UI talks to the neutral service layer through
    /// <see cref="SfFileManagerMapper"/> / <see cref="SfFileServiceExtensions"/>:
    /// every entry field must survive both directions unchanged.
    /// </summary>
    public class SfFileManagerAdapterTest
    {
        private static readonly PropertyInfo[] FsProps = typeof(FsEntry).GetProperties();

        /// <summary>Distinct, non-default value per property so swaps are caught.</summary>
        private static object SampleValue(PropertyInfo p, int seed)
        {
            var t = p.PropertyType;
            if (t == typeof(string)) return $"{p.Name}-{seed}";
            if (t == typeof(bool)) return true;
            if (t == typeof(long)) return 1000L + seed;
            if (t == typeof(DateTime)) return new DateTime(2020, 1, 1).AddMinutes(seed);
            throw new NotSupportedException($"Add a sample value for {t} ({p.Name})");
        }

        private static FsEntry FullFsEntry()
        {
            var e = new FsEntry();
            var i = 0;
            foreach (var p in FsProps) p.SetValue(e, SampleValue(p, i++));
            return e;
        }

        private static FileManagerDirectoryContent FullSfEntry()
        {
            var e = new FileManagerDirectoryContent();
            var i = 0;
            foreach (var p in FsProps) typeof(FileManagerDirectoryContent).GetProperty(p.Name)!.SetValue(e, SampleValue(p, i++));
            return e;
        }

        private static void AssertSameEntryFields(object expected, object actual)
        {
            foreach (var p in FsProps)
            {
                var e = expected.GetType().GetProperty(p.Name)!.GetValue(expected);
                var a = actual.GetType().GetProperty(p.Name)!.GetValue(actual);
                Assert.That(a, Is.EqualTo(e), p.Name);
            }
        }

        [Test]
        public void EveryFsEntryProperty_ExistsOnTheSyncfusionType_WithTheSameType()
        {
            foreach (var p in FsProps)
            {
                var sf = typeof(FileManagerDirectoryContent).GetProperty(p.Name);
                Assert.That(sf, Is.Not.Null, p.Name);
                Assert.That(sf!.PropertyType, Is.EqualTo(p.PropertyType), p.Name);
            }
        }

        [Test]
        public void FsToSfToFs_KeepsEveryProperty()
        {
            var original = FullFsEntry();
            var back = SfFileManagerMapper.ToFs(SfFileManagerMapper.ToSf(original));
            Assert.That(back, Is.Not.SameAs(original));
            AssertSameEntryFields(original, back);
        }

        [Test]
        public void SfToFsToSf_KeepsEveryEntryProperty()
        {
            var original = FullSfEntry();
            var back = SfFileManagerMapper.ToSf(SfFileManagerMapper.ToFs(original));
            AssertSameEntryFields(original, back);
        }

        [Test]
        public void FsToSf_LeavesTheRequestOnlyMembersEmpty()
        {
            var sf = SfFileManagerMapper.ToSf(FullFsEntry());
            Assert.Multiple(() =>
            {
                Assert.That(sf.Action, Is.Null);
                Assert.That(sf.Names, Is.Null);
                Assert.That(sf.NewName, Is.Null);
                Assert.That(sf.TargetData, Is.Null);
                Assert.That(sf.TargetPath, Is.Null);
                Assert.That(sf.Data, Is.Null);
                Assert.That(sf.Permission, Is.Null);
            });
        }

        [Test]
        public void Nulls_StayNull()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SfFileManagerMapper.ToFs((FileManagerDirectoryContent)null!), Is.Null);
                Assert.That(SfFileManagerMapper.ToSf((FsEntry)null!), Is.Null);
                Assert.That(SfFileManagerMapper.ToFs((IEnumerable<FileManagerDirectoryContent>)null!), Is.Null);
                Assert.That(SfFileManagerMapper.ToSf((IEnumerable<FsEntry>)null!), Is.Null);
                Assert.That(SfFileManagerMapper.ToSfResponse((FsListing)null!), Is.Null);
            });
        }

        [Test]
        public void Listing_ConvertsCwdAndFiles_InOrder()
        {
            var listing = new FsListing
            {
                CWD = new FsEntry { Id = "root", Name = "Root", FilterPath = "" },
                Files = new List<FsEntry>
                {
                    new() { Id = "1", Name = "b", IsFile = true, Size = 5 },
                    new() { Id = "2", Name = "a", IsFile = false, Type = "folder" }
                }
            };

            var sf = SfFileManagerMapper.ToSfResponse(listing);

            Assert.That(sf.CWD.Id, Is.EqualTo("root"));
            Assert.That(sf.Files.Select(f => f.Id), Is.EqualTo(new[] { "1", "2" }));
            AssertSameEntryFields(listing.Files[0], sf.Files[0]);
            Assert.That(sf.Error, Is.Null);
            Assert.That(sf.Details, Is.Null);
        }

        [Test]
        public void ListOfEntries_IsAnsweredAsFilesOnly()
        {
            var sf = SfFileManagerMapper.ToSfResponse(new List<FsEntry> { new() { Id = "x" } });
            Assert.That(sf.CWD, Is.Null);
            Assert.That(sf.Files.Single().Id, Is.EqualTo("x"));
        }

        /// <summary>The component's event args have non-public setters: fill them like the component does.</summary>
        private static T With<T>(T target, params (string Name, object Value)[] values)
        {
            foreach (var (name, value) in values)
            {
                var prop = typeof(T).GetProperty(name)!;
                var setter = prop.GetSetMethod(true);
                if (setter != null) { setter.Invoke(target, new[] { value }); continue; }
                var field = typeof(T).GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException($"Cannot set {name}");
                field.SetValue(target, value);
            }
            return target;
        }

        [Test]
        public void MoveArgs_CarryFilesTargetAndMode()
        {
            var args = With(new ItemsMoveEventArgs<FileManagerDirectoryContent>(),
                ("Path", "/from/"),
                ("TargetPath", "/to/"),
                ("TargetData", new FileManagerDirectoryContent { Id = "t", Name = "to", FilterId = "r/", FilterPath = "/" }),
                ("Files", new List<FileManagerDirectoryContent> { FullSfEntry() }),
                ("IsCopy", true));

            var req = SfFileManagerMapper.ToFs(args);

            Assert.That(req.Path, Is.EqualTo("/from/"));
            Assert.That(req.TargetPath, Is.EqualTo("/to/"));
            Assert.That(req.IsCopy, Is.True);
            Assert.That(req.TargetData.Id, Is.EqualTo("t"));
            Assert.That(req.TargetData.FilterId, Is.EqualTo("r/"));
            AssertSameEntryFields(args.Files[0], req.Files.Single());
        }

        [Test]
        public void DeleteArgs_CarryPathAndFiles()
        {
            var args = With(new ItemsDeleteEventArgs<FileManagerDirectoryContent>(),
                ("Path", "/a/"),
                ("Files", new List<FileManagerDirectoryContent> { new FileManagerDirectoryContent { Name = "x", FilterPath = "/a/", ParentId = "p" } }));

            var req = SfFileManagerMapper.ToFs(args);

            Assert.That(req.Path, Is.EqualTo("/a/"));
            Assert.That(req.Files.Single().Name, Is.EqualTo("x"));
            Assert.That(req.Files.Single().ParentId, Is.EqualTo("p"));
        }

        [Test]
        public void CreateFolderArgs_CarryPathNameAndParent()
        {
            var args = With(new FolderCreateEventArgs<FileManagerDirectoryContent>(),
                ("Path", "/a/"),
                ("FolderName", "new"),
                ("ParentFolder", new FileManagerDirectoryContent { Id = "p", Name = "a", FilterId = "r/", FilterPath = "/" }));

            var req = SfFileManagerMapper.ToFs(args);

            Assert.That(req.Path, Is.EqualTo("/a/"));
            Assert.That(req.FolderName, Is.EqualTo("new"));
            Assert.That(req.ParentFolder.Id, Is.EqualTo("p"));
            Assert.That(req.ParentFolder.FilterId, Is.EqualTo("r/"));
        }

        // ---- extensions over IFileService -------------------------------------------------

        public class FakeFileService : DispatchProxy
        {
            public Func<MethodInfo, object?[], object?> Handler { get; set; } = (_, _) => null;
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args ?? Array.Empty<object?>());
        }

        private static (IFileService Service, FakeFileService Fake) Fake()
        {
            var service = DispatchProxy.Create<IFileService, FakeFileService>();
            return (service, (FakeFileService)(object)service);
        }

        [Test]
        public async Task GetFilesPath_GivesTheComponentItsOwnFolderBack_WhenTheServiceEchoesIt()
        {
            var (fs, fake) = Fake();
            List<FsEntry>? received = null;
            fake.Handler = (m, a) =>
            {
                Assert.That(m.Name, Is.EqualTo(nameof(IFileService.GetFilesPath)));
                received = (List<FsEntry>)a[2]!;
                return Task.FromResult(new FsListing { CWD = received[0], Files = new List<FsEntry> { new() { Id = "c" } } });
            };
            var folder = new FileManagerDirectoryContent { Id = "f", Name = "a", FilterPath = "/", Action = "read" };

            var response = await fs.SfGetFilesPath("db", "/a/", new List<FileManagerDirectoryContent> { folder });

            Assert.That(received!.Single().Id, Is.EqualTo("f"));
            Assert.That(response.CWD, Is.SameAs(folder));
            Assert.That(response.Files.Single().Id, Is.EqualTo("c"));
        }

        [Test]
        public async Task GetFilesPath_ConvertsAServiceBuiltFolder()
        {
            var (fs, fake) = Fake();
            fake.Handler = (_, a) =>
            {
                Assert.That(a[2], Is.Null);
                Assert.That(a[3], Is.EqualTo("coll"));
                return Task.FromResult(new FsListing { CWD = new FsEntry { Id = "root" }, Files = new List<FsEntry>() });
            };

            var response = await fs.SfGetFilesPath("db", "/", null, "coll");

            Assert.That(response.CWD.Id, Is.EqualTo("root"));
            Assert.That(response.Files, Is.Empty);
        }

        [Test]
        public async Task CopyOrMove_PassesConvertedArguments()
        {
            var (fs, fake) = Fake();
            object?[]? seen = null;
            fake.Handler = (_, a) => { seen = a; return Task.FromResult(new FsListing { Files = new List<FsEntry> { new() { PreviousName = "x" } } }); };

            var response = await fs.SfCopyOrMoveItems("db",
                new[] { new FileManagerDirectoryContent { Id = "1", Name = "x", IsFile = true } },
                "/to/", new FileManagerDirectoryContent { Id = "t" }, false);

            Assert.That(((FsEntry[])seen![1]!).Single().Id, Is.EqualTo("1"));
            Assert.That(seen[2], Is.EqualTo("/to/"));
            Assert.That(((FsEntry)seen[3]!).Id, Is.EqualTo("t"));
            Assert.That(seen[4], Is.EqualTo(false));
            Assert.That(response.Files.Single().PreviousName, Is.EqualTo("x"));
        }

        [Test]
        public async Task UploadFromServer_KeepsTheFieldsThePersistedTaskStores()
        {
            var (fs, fake) = Fake();
            List<FsEntry>? seen = null;
            fake.Handler = (_, a) => { seen = (List<FsEntry>)a[2]!; return Task.FromResult("task"); };
            var item = new FileManagerDirectoryContent
            {
                Name = "a.mp3", FilterPath = "/music/", IsFile = true, Size = 10,
                DateModified = new DateTime(2024, 5, 1), Type = ".mp3"
            };

            var id = await fs.SfAddUploadFileFromServer("db", "/", new List<FileManagerDirectoryContent> { item });

            Assert.That(id, Is.EqualTo("task"));
            AssertSameEntryFields(item, seen!.Single());
        }
    }
}
