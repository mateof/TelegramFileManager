using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;

namespace Test
{
    /// <summary>
    /// Folder size updates in one operation: ancestor ids taken from
    /// <c>FilterId</c>, confirmed against <c>ParentId</c>, and the single
    /// <c>UpdateMany</c> filter. Also the indexed equivalents of the computed
    /// path predicates.
    /// </summary>
    public class FolderAncestryTest
    {
        private static readonly string Root = ObjectId.GenerateNewId().ToString();
        private static readonly string A = ObjectId.GenerateNewId().ToString();
        private static readonly string B = ObjectId.GenerateNewId().ToString();
        private static readonly string C = ObjectId.GenerateNewId().ToString();

        /// <summary>The old loop: one lookup per level, folder first.</summary>
        private static List<string> OldWalk(string folderId, IReadOnlyDictionary<string, string?> parentById)
        {
            var folders = new List<string>();
            while (!string.IsNullOrEmpty(folderId))
            {
                folders.Add(folderId);
                folderId = parentById[folderId]!;
            }
            return folders;
        }

        private static Dictionary<string, string?> Tree() => new()
        {
            [Root] = "",
            [A] = Root,
            [B] = A,
            [C] = B,
        };

        [Test]
        public void ParseFilterId_ReturnsAncestorsRootFirst_AndDropsNonIds()
        {
            Assert.That(FolderAncestry.ParseFilterId($"{Root}/{A}/{B}/"), Is.EqualTo(new[] { Root, A, B }));
            Assert.That(FolderAncestry.ParseFilterId(""), Is.Empty);
            Assert.That(FolderAncestry.ParseFilterId(null), Is.Empty);
            Assert.That(FolderAncestry.ParseFilterId($"{Root}/garbage\\x/{A}/"), Is.EqualTo(new[] { Root, A }));
        }

        [Test]
        public void ResolveChain_WithCompleteFilterId_EqualsOldWalk()
        {
            var tree = Tree();
            // FilterId of a file inside C is root/A/B/C/.
            var candidates = FolderAncestry.CandidateIds(C, $"{Root}/{A}/{B}/{C}/");
            var read = candidates.Where(tree.ContainsKey).ToDictionary(id => id, id => tree[id]);

            var resolved = FolderAncestry.ResolveChain(C, read);

            Assert.That(resolved.MissingId, Is.Null);
            Assert.That(resolved.Chain, Is.EqualTo(OldWalk(C, tree)));
            Assert.That(resolved.Chain, Is.EqualTo(new[] { C, B, A, Root }));
        }

        [Test]
        public void ResolveChain_WithStaleFilterId_ReportsWhereTheWalkMustContinue()
        {
            var tree = Tree();
            // Stale FilterId (left by an old move): A is missing from it.
            var candidates = FolderAncestry.CandidateIds(C, $"{Root}/{B}/");
            var read = candidates.Where(tree.ContainsKey).ToDictionary(id => id, id => tree[id]);

            var resolved = FolderAncestry.ResolveChain(C, read);

            Assert.That(resolved.Chain, Is.EqualTo(new[] { C, B }));
            Assert.That(resolved.MissingId, Is.EqualTo(A));
            // Finishing the walk from there gives the old result.
            var rest = OldWalk(resolved.MissingId!, tree);
            Assert.That(resolved.Chain.Concat(rest), Is.EqualTo(OldWalk(C, tree)));
        }

        [Test]
        public void ResolveChain_StopsOnCycles()
        {
            var cyclic = new Dictionary<string, string?> { [A] = B, [B] = A };
            var resolved = FolderAncestry.ResolveChain(A, cyclic);
            Assert.That(resolved.Chain, Is.EqualTo(new[] { A, B }));
            Assert.That(resolved.MissingId, Is.Null);
        }

        [Test]
        public void ResolveChain_EmptyFolderId_IsEmpty()
        {
            var resolved = FolderAncestry.ResolveChain("", Tree());
            Assert.That(resolved.Chain, Is.Empty);
            Assert.That(resolved.MissingId, Is.Null);
        }

        [Test]
        public void AncestorUpdateFilter_IsOneInOverIdsOfFolders()
        {
            var filter = FolderAncestry.AncestorUpdateFilter(new[] { C, B, A, Root });
            var serializer = BsonSerializer.SerializerRegistry.GetSerializer<BsonFileManagerModel>();
            var rendered = filter.Render(new RenderArgs<BsonFileManagerModel>(serializer, BsonSerializer.SerializerRegistry));

            var expected = new BsonDocument
            {
                { "_id", new BsonDocument("$in", new BsonArray { new ObjectId(C), new ObjectId(B), new ObjectId(A), new ObjectId(Root) }) },
                { "IsFile", false }
            };
            Assert.That(rendered, Is.EqualTo(expected));
        }

        private static BsonFileManagerModel Doc(string id, string filterId, string filterPath, string name)
            => new() { Id = id, FilterId = filterId, FilterPath = filterPath, Name = name };

        private static List<BsonFileManagerModel> SampleDocs() => new()
        {
            Doc(Root, "", "", "Files"),
            Doc(A, $"{Root}/", "/", "music"),
            Doc(B, $"{Root}/{A}/", "/music/", "rock"),
            Doc(C, $"{Root}/{A}/{B}/", "/music/rock/", "a/b"), // name with a slash
        };

        [TestCase("")]
        [TestCase("/")]
        [TestCase("x/")]
        [TestCase("not-an-id/")]
        public void SplitFolderFilterIdPath_MatchesComputedPredicate_Misc(string path)
            => AssertSplitMatchesPredicate(path);

        [Test]
        public void SplitFolderFilterIdPath_MatchesComputedPredicate_ForEveryDoc()
        {
            foreach (var d in SampleDocs())
            {
                AssertSplitMatchesPredicate(d.FilterId + d.Id + "/");
                AssertSplitMatchesPredicate(d.FilterId + d.Id);          // no trailing slash
                AssertSplitMatchesPredicate("z" + d.FilterId + d.Id + "/"); // wrong prefix
                AssertSplitMatchesPredicate(d.FilterId + d.Id.ToUpperInvariant() + "/"); // server renders lowercase
            }
        }

        private static void AssertSplitMatchesPredicate(string path)
        {
            var docs = SampleDocs();
            var computed = docs.Where(x => x.FilterId + x.Id + "/" == path).Select(x => x.Id).ToList();
            var split = FolderAncestry.SplitFolderFilterIdPath(path);
            var indexed = split == null
                ? new List<string>()
                : docs.Where(x => x.Id == split.Value.Id && x.FilterId == split.Value.FilterId).Select(x => x.Id).ToList();
            Assert.That(indexed, Is.EqualTo(computed), path);
        }

        [Test]
        public void FilterPathNameSplits_MatchComputedPredicate()
        {
            var docs = SampleDocs();
            var paths = new List<string> { "", "/", "Files/", "/music/", "/music/rock/", "/music/rock/a/b/", "/music/rock/a/b", "/nope/" };
            foreach (var path in paths)
            {
                var computed = docs.Where(x => x.FilterPath + x.Name + "/" == path).Select(x => x.Id).ToList();
                var splits = FolderAncestry.FilterPathNameSplits(path);
                var indexed = docs.Where(x => splits.Any(s => s.FilterPath == x.FilterPath && s.Name == x.Name)).Select(x => x.Id).ToList();
                Assert.That(indexed, Is.EqualTo(computed), path);
            }
        }
    }
}
