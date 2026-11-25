using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services.Api;
using TL;

namespace Test
{
    /// <summary>Windows of a chat history, searches and the attachments of several messages.</summary>
    public class ChatMessagesTest
    {
        [TestCase(null, null, null, 0, 0)]
        [TestCase(500, null, null, 500, 0)]
        [TestCase(null, 500, null, 500, -50)]
        [TestCase(null, null, 500, 501, -25)] // 501: the message itself is in the window
        public void AWindowIsAskedToTelegramByOffset(int? before, int? after, int? around, int offsetId, int addOffset) =>
            Assert.That(new ChatWindow(before, after, around, 50).ToTelegram(), Is.EqualTo((offsetId, addOffset)));

        [Test]
        public void TheNewestWindowHasNothingNewer()
        {
            Assert.That(new ChatWindow(null, null, null, 50).More(new[] { 9, 8 }), Is.EqualTo((true, false)));
            Assert.That(new ChatWindow(null, null, null, 50).More(Array.Empty<int>()), Is.EqualTo((false, false)));
        }

        [Test]
        public void AnEmptyPageEndsItsDirection()
        {
            Assert.That(new ChatWindow(5, null, null, 50).More(Array.Empty<int>()).Older, Is.False);
            Assert.That(new ChatWindow(5, null, null, 50).More(new[] { 4 }).Older, Is.True);
            Assert.That(new ChatWindow(null, 5, null, 50).More(new[] { 6 }).Newer, Is.True);
            Assert.That(new ChatWindow(null, 5, null, 50).More(Array.Empty<int>()).Newer, Is.False);
        }

        [Test]
        public void AfterTheLastMessageTelegramAnswersOlderOnesThatAreDropped()
        {
            var window = new ChatWindow(null, 10, null, 4);
            Assert.That(new[] { 11, 10, 9, 8 }.Where(window.Contains), Is.EqualTo(new[] { 11 }));
            Assert.That(window.More(new[] { 10, 9, 8 }).Newer, Is.False);
        }

        [Test]
        public void AroundAMessageBothSidesAreKnown()
        {
            var window = new ChatWindow(null, null, 10, 4);
            Assert.That(window.More(new[] { 12, 11, 10, 9 }), Is.EqualTo((true, true)));
            Assert.That(window.More(new[] { 10, 9 }), Is.EqualTo((true, false)));
            Assert.That(window.More(new[] { 11, 10 }), Is.EqualTo((false, true)));
        }

        // Ids that end in 0 stand for service messages (a member joined...)
        private static ChatMessagesPage Page(int total, params int[] ids) => new()
        {
            Total = total,
            Ids = ids.ToList(),
            Messages = ids.Where(i => i % 10 != 0).Select(i => new ChatMessages { message = new Message { id = i, message = "m" + i } }).ToList()
        };

        [Test]
        public void TheEndsOfAWindowCountTheServiceMessages()
        {
            var dto = ApiChatWindowDto.From(new ChatWindow(null, null, null, 50), Page(0, 30, 29, 20));
            Assert.That(dto.Items.Select(i => i.Id), Is.EqualTo(new[] { 29 }));
            Assert.That((dto.OldestId, dto.NewestId, dto.HasOlder, dto.HasNewer), Is.EqualTo(((int?)20, (int?)30, true, false)));
        }

        [Test]
        public void ASearchGoesOnUntilItHasEverything()
        {
            Assert.That(ApiChatSearchDto.From(Page(3, 9, 8, 7), 0).NextOffsetId, Is.Null, "all in the first page");
            Assert.That(ApiChatSearchDto.From(Page(5, 9, 8, 7), 0).NextOffsetId, Is.EqualTo(7));
            Assert.That(ApiChatSearchDto.From(Page(3, 5, 4), 7).NextOffsetId, Is.EqualTo(4));
            Assert.That(ApiChatSearchDto.From(Page(5), 4).NextOffsetId, Is.Null);
        }

        [Test]
        public void KindsAreNamesNotNumbers()
        {
            Assert.That(ChatMessageKinds.TryParse("Photos", out var kind) && kind == ChatMessageKind.Photos, Is.True);
            Assert.That(ChatMessageKinds.TryParse("1", out _), Is.False);
            Assert.That(ChatMessageKinds.TryParse(null, out _), Is.False);
        }

        [Test]
        public void ASongBringsItsTagsAndLength()
        {
            var dto = ApiChatMessageDto.FromMessage(new ChatMessages
            {
                message = new Message
                {
                    id = 5,
                    media = new MessageMediaDocument
                    {
                        document = new Document
                        {
                            mime_type = "audio/mpeg",
                            size = 10,
                            attributes = new DocumentAttribute[]
                            {
                                new DocumentAttributeFilename { file_name = "a.mp3" },
                                new DocumentAttributeAudio { duration = 61, title = "Song", performer = "", flags = DocumentAttributeAudio.Flags.has_title }
                            }
                        }
                    }
                }
            });
            Assert.That((dto.MediaType, dto.FileName, dto.Duration, dto.Title, dto.Performer), Is.EqualTo(("audio", "a.mp3", (int?)61, "Song", (string?)null)));
        }

        [Test]
        public void MessagesThatShareAFileNameDoNotOverwriteEachOther()
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool OnDisk(string n) => n == "b.mp4";
            Assert.That(new[] { "a.mp4", "A.mp4", "a.mp4", "b.mp4", "c", "c" }.Select(n => MessageDownloads.UniqueName(n, taken, OnDisk)),
                Is.EqualTo(new[] { "a.mp4", "A (2).mp4", "a (3).mp4", "b (2).mp4", "c", "c (2)" }));
        }

        [Test]
        public void AFileKnowsItsMessageAlsoWhenItIsSplit()
        {
            var whole = new BsonFileManagerModel { Id = "1", Name = "a.bin", IsFile = true, MessageId = 7 };
            var split = new BsonFileManagerModel { Id = "2", Name = "b.bin", IsFile = true, isSplit = true, ListMessageId = new List<int> { 8, 9 } };
            var folder = new BsonFileManagerModel { Id = "3", Name = "c", IsFile = false };
            Assert.That(new[] { whole, split, folder }.Select(f => ApiFileDto.FromBson(f, "1", "").FirstMessageId), Is.EqualTo(new int?[] { 7, 8, null }));
        }
    }
}
