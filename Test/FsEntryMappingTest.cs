using MongoDB.Bson;
using TelegramDownloader.Models;
using TelegramDownloader.Models.FileSystem;
using TelegramDownloader.Models.Persistence;

namespace Test
{
    /// <summary>
    /// The neutral <see cref="FsEntry"/> mappings of the index model produce the
    /// same values (and the same MongoDB documents) the Syncfusion-based ones did.
    /// </summary>
    public class FsEntryMappingTest
    {
        private static BsonFileManagerModel Stored() => new()
        {
            Id = "65a000000000000000000001",
            Name = "song.mp3",
            CaseSensitive = true,
            DateCreated = new DateTime(2024, 1, 2, 3, 4, 5),
            DateModified = new DateTime(2024, 2, 3, 4, 5, 6),
            FilterId = "root/dir/",
            FilterPath = "/Music/",
            FilePath = "/Music/song.mp3",
            HasChild = false,
            IsFile = true,
            ParentId = "dir",
            ShowHiddenItems = true,
            Size = 1234,
            Type = ".mp3",
            MessageId = 77
        };

        [Test]
        public void ToFsEntry_CopiesTheEntryFields()
        {
            var m = Stored();
            var e = m.ToFsEntry();
            Assert.Multiple(() =>
            {
                Assert.That(e.Id, Is.EqualTo(m.Id));
                Assert.That(e.Name, Is.EqualTo(m.Name));
                Assert.That(e.CaseSensitive, Is.EqualTo(m.CaseSensitive));
                Assert.That(e.DateCreated, Is.EqualTo(m.DateCreated));
                Assert.That(e.DateModified, Is.EqualTo(m.DateModified));
                Assert.That(e.FilterId, Is.EqualTo(m.FilterId));
                Assert.That(e.FilterPath, Is.EqualTo(m.FilterPath));
                Assert.That(e.HasChild, Is.EqualTo(m.HasChild));
                Assert.That(e.IsFile, Is.EqualTo(m.IsFile));
                Assert.That(e.ParentId, Is.EqualTo(m.ParentId));
                Assert.That(e.ShowHiddenItems, Is.EqualTo(m.ShowHiddenItems));
                Assert.That(e.Size, Is.EqualTo(m.Size));
                Assert.That(e.Type, Is.EqualTo(m.Type));
                // Never set by the old conversion either.
                Assert.That(e.Path, Is.Null);
                Assert.That(e.PreviousName, Is.Null);
            });
        }

        [Test]
        public void ToFsEntryInCopy_IsTheReducedCopyAnswer()
        {
            var m = Stored();
            var e = m.ToFsEntryInCopy();
            Assert.Multiple(() =>
            {
                Assert.That(e.Name, Is.EqualTo(m.Name));
                Assert.That(e.PreviousName, Is.EqualTo(m.Name));
                Assert.That(e.FilterPath, Is.EqualTo(m.FilterPath));
                Assert.That(e.DateCreated, Is.EqualTo(m.DateCreated));
                Assert.That(e.DateModified, Is.EqualTo(m.DateModified));
                Assert.That(e.HasChild, Is.EqualTo(m.HasChild));
                Assert.That(e.Size, Is.EqualTo(m.Size));
                Assert.That(e.Type, Is.EqualTo(m.Type));
                Assert.That(e.Id, Is.Null);
                Assert.That(e.FilterId, Is.Null);
                Assert.That(e.ParentId, Is.Null);
                Assert.That(e.IsFile, Is.False);
            });
        }

        [Test]
        public void NewFolder_UnderTheRoot_ProducesTheSameDocument()
        {
            var root = new FsEntry { Id = "root", Name = "Root", FilterId = "", FilterPath = "" };

            var folder = new BsonFileManagerModel("/", "Music", root);

            var doc = folder.ToBsonDocument();
            Assert.Multiple(() =>
            {
                Assert.That(doc["ParentId"].AsString, Is.EqualTo("root"));
                Assert.That(doc["Name"].AsString, Is.EqualTo("Music"));
                Assert.That(doc["FilePath"].AsString, Is.EqualTo("/Music"));
                Assert.That(doc["FilterId"].AsString, Is.EqualTo("root/"));
                Assert.That(doc["FilterPath"].AsString, Is.EqualTo("/"));
                Assert.That(doc["IsFile"].AsBoolean, Is.False);
                Assert.That(doc["HasChild"].AsBoolean, Is.True);
                Assert.That(doc["Size"].ToInt64(), Is.EqualTo(0));
                Assert.That(doc["Type"].AsString, Is.EqualTo("folder"));
                Assert.That(doc["CaseSensitive"].AsBoolean, Is.False);
                Assert.That(doc["ShowHiddenItems"].AsBoolean, Is.False);
            });
        }

        [Test]
        public void NewFolder_Nested_ChainsTheParentIdsAndPath()
        {
            var parent = new FsEntry { Id = "dir", Name = "Music", FilterId = "root\\", FilterPath = "/" };

            var folder = new BsonFileManagerModel("\\Music", "Rock", parent);

            Assert.Multiple(() =>
            {
                Assert.That(folder.ParentId, Is.EqualTo("dir"));
                Assert.That(folder.FilePath, Is.EqualTo("/Music/Rock"));
                Assert.That(folder.FilterId, Is.EqualTo("root/dir/"));
                Assert.That(folder.FilterPath, Is.EqualTo("/Music/"));
            });
        }

        [Test]
        public void PersistedSourceItems_RoundTrip()
        {
            var items = new List<PersistedSourceItem>
            {
                new() { Name = "a.mp3", FilterPath = "/m/", IsFile = true, Size = 3, DateModified = new DateTime(2024, 1, 1), Type = ".mp3" },
                new() { Name = "sub", FilterPath = "/m/", IsFile = false }
            };

            var contents = TaskPersistenceMapper.ToSourceContents(items);

            Assert.That(contents.Select(c => (c.Name, c.FilterPath, c.IsFile, c.Size, c.DateModified, c.Type)),
                Is.EqualTo(items.Select(i => (i.Name, i.FilterPath, i.IsFile, i.Size, i.DateModified, i.Type))));
        }
    }
}
