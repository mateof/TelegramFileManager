using BlazorBootstrap;
using Microsoft.AspNetCore.Components;
using System.Collections.Generic;
using System.Threading.Tasks;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Services;
using TL;

namespace TelegramDownloader.Data
{
    public class TelegramService : ITelegramService
    {
        public static bool isPremium = false;
        public static int splitSizeGB = 2;
        private TransactionInfoService _tis { get; set; }
        private IDbService _db { get; set; }
        private static WTelegram.Client client = null;
        private static Messages_Chats chats = null;
        private static List<ChatViewBase> favouriteChannels = new List<ChatViewBase>();
        private static Mutex mut = new Mutex();



        public TelegramService(TransactionInfoService tis, IDbService db)
        {
            _tis = tis;
            _db = db;
            // createDownloadFolder();
            mut.WaitOne();
            if (client == null)
            {
                newClient();
            }

            mut.ReleaseMutex();
        }

        private void createDownloadFolder()
        {
            if (!Directory.Exists(Path.Combine(Path.Combine(Environment.CurrentDirectory, "download"))))
            {
                Directory.CreateDirectory(Path.Combine(Path.Combine(Environment.CurrentDirectory, "download")));
            }
        }

        private void newClient()
        {
            client = new WTelegram.Client(Convert.ToInt32(GeneralConfigStatic.tlconfig?.api_id ?? Environment.GetEnvironmentVariable("api_id")), GeneralConfigStatic.tlconfig?.hash_id ?? Environment.GetEnvironmentVariable("hash_id"), UserService.USERDATAFOLDER + "/WTelegram.session");
            if (GeneralConfigStatic.config.ShouldShowLogInTerminal)
            {
                // WTelegram.Helpers.Log = (lvl, str) => Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{"TDIWE!"[lvl]}] {str}");
            } else
            {
                WTelegram.Helpers.Log = (lvl, str) => { };
            }

        }

        public async Task<User> CallQrGenerator(Action<string> func, CancellationToken ct, bool logoutFirst = false)
        {
            return await client.LoginWithQRCode(func, logoutFirst: logoutFirst, ct: ct);
        }

        public async Task<User> GetUser()
        {
            return client.User;
        }

        public static void SetSplitSizeGB()
        {
            if (GeneralConfigStatic.config.SplitSize > 0)
            {
                splitSizeGB = GeneralConfigStatic.config.SplitSize;
            } else
            {
                if (client.User.flags.HasFlag(User.Flags.premium))
                {
                    // isPremium = true;
                    splitSizeGB = 4;
                    return;
                }
                splitSizeGB = 2;
            }
        }

        async Task<string> DoLogin(string loginInfo) // (add this method to your code)
        {
            if (client.User == null)
                switch (await client.Login(loginInfo)) // returns which config is needed to continue login
                {
                    case "phone_number": return "phone";
                    case "verification_code": return "vc";
                    case "name": return "name";    // if sign-up is required (first/last_name)
                    case "password": return "pass"; // if user has enabled 2FA
                    default: break;
                }
            await getAllChats();
            SetSplitSizeGB();
            if (client.User.flags.HasFlag(User.Flags.premium))
            {
                isPremium = true;
                // splitSizeGB = 4;
            }
            Console.WriteLine($"We are logged-in as {client.User} (id {client.User.id})");
            return "ok";
        }
        /// <summary>
        /// check if channel exist
        /// </summary>
        /// <param name="id">channel Id</param>
        /// <returns>true if exist and false if not exist</returns>
        public bool checkChannelExist(string id)
        {
            return chats == null ? false : chats.chats[Convert.ToInt64(id)] != null;
        }


        public async Task<string> checkAuth(string number, bool isPhone = false)
        {
            if (client.UserId != null && number == null)
            {
                UserData ud = await UserService.getUserDataFromFile();
                if (ud != null)
                {
                    try
                    {
                        return await DoLogin(ud.phone);
                    }
                    catch (Exception ex)
                    {
                        client.Dispose();
                        newClient();
                        return "phone";
                    }

                }
                else
                {
                    return "phone";
                }

            }
            if (isPhone && number != null)
            {
                // newClient();
                await UserService.setUserDataToFile(new UserData(number));
            }
            try
            {
                return await DoLogin(number);
            }
            catch (Exception ex)
            {
                client.Dispose();
                newClient();
                return null;
            }

        }

        public async Task sendVerificationCode(string vc)
        {
            await DoLogin(vc);
        }

        public bool checkUserLogin()
        {

            return client.User != null;
        }

        public async Task logOff()
        {
            await client.Auth_LogOut();
            UserService.deleteUserDataToFile();
            client.Dispose();
            newClient();
        }

        public async Task<InvitationInfo?> getInvitationHash(long id)
        {
            var peer = chats.chats[id];
            var invites = await client.Messages_GetExportedChatInvites(peer, client.User);
            if (invites != null && invites.invites.Count() > 0)
            {
                
                if (invites.invites.FirstOrDefault() is ChatInviteExported invi)
                {
                    return buildInvitationInfo(invi);
                }
            }
            var invitation = await client.Messages_ExportChatInvite(peer);
            if(invitation is ChatInviteExported inv)
            {
                return buildInvitationInfo(inv);
            }
            return null;
        }

        private InvitationInfo buildInvitationInfo(ChatInviteExported invi)
        {
            var hash = invi.link.Split("/").LastOrDefault();
            if (hash[0] == '+')
                return new InvitationInfo(hash.Substring(1), invi.link);
            return new InvitationInfo(hash, invi.link);
        }

        public async Task joinChatInvitationHash(string? hash)
        {
            await client.Messages_ImportChatInvite(hash);
        }

        public bool isInChat(long id)
        {
            try
            {
                var _chat = chats.chats[id];
                return true;
            } catch(Exception)
            {
                return false;
            }
        }

        public string getChatName(long id)
        {
            List<ChatMessages> cm = new List<ChatMessages>();
            var peer = chats.chats[id];
            return peer.Title;
        }

        public async Task<List<ChatViewBase>> GetFouriteChannels(bool mustRefresh = true)
        {

            
            if (mustRefresh)
            {
                favouriteChannels = new List<ChatViewBase>();
                foreach (ChatViewBase cvb in await getAllSavedChats())
                {
                    if (GeneralConfigStatic.config.FavouriteChannels.Contains(cvb.chat.ID))
                    {
                        favouriteChannels.Add(cvb);
                    }
                }
            }
                
            return favouriteChannels;
        }


        public async Task<List<ChatViewBase>> getAllChats()
        {
            if (!checkUserLogin()) return new List<ChatViewBase>();
            List<ChatViewBase> allChats = new List<ChatViewBase>();
            chats = await client.Messages_GetAllChats();
            await GetFouriteChannels();
            foreach (var (id, chat) in chats.chats)
                if (chat.IsActive)
                {
                    ChatViewBase cb = new ChatViewBase();
                    cb.chat = chat;
                    cb.img64 = ""; // chat.Photo == null ? "" : await getPhotoThumb(chat);
                    allChats.Add(cb);
                }
            return allChats;
        }

        public async Task<List<ChatViewBase>> getAllSavedChats()
        {
            List<ChatViewBase> allChats = new List<ChatViewBase>();
            if (chats == null)
            {
                return await getAllChats();
            }
            foreach (var (id, chat) in chats.chats)
                if (chat.IsActive)
                {
                    ChatViewBase cb = new ChatViewBase();
                    cb.chat = chat;
                    cb.img64 = ""; // chat.Photo == null ? "" : await getPhotoThumb(chat);
                    allChats.Add(cb);
                }
            return allChats;
        }

        public async Task<Message> uploadFile(string chatId, Stream file, string fileName, string mimeType = null, UploadModel um = null, string caption = null)
        {
            um = um ?? new UploadModel();
            um.tis = _tis;

            InputPeer peer = chats.chats[Convert.ToInt64(chatId)];
            um.name = fileName;
            um._size = file.Length;
            um._transmitted = 0;
            _tis.addToUploadList(um);
            var inputFile = await client.UploadFileAsync(file, fileName, um.ProgressCallback);
            return await client.SendMediaAsync(peer, caption ?? fileName, inputFile, mimeType);
        }

        public async Task deleteFile(string chatId, int idMessage)
        {
            InputPeer peer = chats.chats[Convert.ToInt64(chatId)];
            await client.DeleteMessages(peer, idMessage);
        }

        public async Task<Message> getMessageFile(string chatId, int idMessage)
        {
            var peer = chats.chats[Convert.ToInt64(chatId)];
            List<InputMessageID> ids = new List<InputMessageID>();
            ids.Add(new InputMessageID { id = idMessage });
            //var mm = await client.Messages_GetMessages(ids.ToArray());
            var mm = await client.GetMessages(peer, ids.ToArray());
            // var mm = await client.Messages_GetHistory(peer, limit: 1);
            return (Message)mm.Messages.FirstOrDefault();
            //var i = await client.Messages_GetHistory(peer, min_id: idMessage, max_id: idMessage, limit: 1);
            //var ms = await client.GetMessages(peer, ids.ToArray());
            //Message m = (Message)ms.Messages.FirstOrDefault();
            //return m;
        }

        public async Task<List<ChatMessages>> getAllMessages(long id, Boolean onlyFiles = false)
        {
            List<ChatMessages> cm = new List<ChatMessages>();
            InputPeer peer = chats.chats[id];
            for (int offset_id = 0; ;)
            {
                var messages = await client.Messages_GetHistory(peer, offset_id);
                if (messages.Messages.Length == 0) break;
                foreach (MessageBase msgBase in messages.Messages)
                {
                    if (msgBase is Message msg)
                    {
                        ChatMessages cm2 = new ChatMessages();
                        cm2.message = msg;
                        cm2.isDocument = false;
                        if (msg.media is MessageMediaDocument { document: Document document })
                        {
                            cm2.isDocument = true;
                        }
                        if (onlyFiles)
                        {
                            if (cm2.isDocument)
                                cm.Add(cm2);
                        } else
                        {
                            cm2.htmlMessage = client.EntitiesToHtml(msg.message, msg.entities);
                            cm2.user = messages.UserOrChat(msg.From ?? msg.Peer);
                            cm.Add(cm2);
                        }
                            
                        
                    }
                }
                offset_id = messages.Messages[^1].ID;
            }
            return cm;
        }

        public async Task<List<ChatMessages>> getAllMediaMessages(long id, Boolean onlyFiles = false)
        {
            List<ChatMessages> cm = new List<ChatMessages>();
            InputPeer peer = chats.chats[id];
            int size = 100;
            int page = 1;

            var messages = await client.Messages_GetHistory(peer, add_offset: page -1, limit: size);
            if (messages.Messages.Length == 0) return await Task.FromResult(cm);
            foreach (MessageBase msgBase in messages.Messages)
            {
                if (msgBase is Message msg)
                {
                    ChatMessages cm2 = new ChatMessages();
                    cm2.message = msg;
                    cm2.isDocument = false;
                    if (msg.media is MessageMediaDocument { document: Document document })
                    {
                        cm2.isDocument = true;
                    }
                    if (onlyFiles)
                    {
                        if (cm2.isDocument)
                            cm.Add(cm2);
                    }
                    else
                    {
                        cm2.htmlMessage = client.EntitiesToHtml(msg.message, msg.entities);
                        cm2.user = messages.UserOrChat(msg.From ?? msg.Peer);
                        cm.Add(cm2);
                    }
                }
            }

            int totalMessages = messages.Count;
            int gettedMessages = messages.Messages.Length;
            int maxParalellTasks = 50;
            int totalTasks = 0;
            page++;
            List<Task<List<ChatMessages>>> tasks = new List<Task<List<ChatMessages>>>();

            while (gettedMessages < totalMessages)
            {
                int sizeGetted = gettedMessages + size >= totalMessages ? totalMessages - gettedMessages : size;

                tasks.Add(getPaginatedMessagesAsync(peer, page, sizeGetted, onlyFiles));
                page++;
                totalTasks++;
                gettedMessages += sizeGetted;

                if (totalTasks == maxParalellTasks || gettedMessages == totalMessages)
                {
                    var results = await Task.WhenAll(tasks);
                    totalTasks = 0;

                    foreach (var result in results)
                    {
                        cm.AddRange(result);
                    }
                }
            }

            return await Task.FromResult(cm);
        }

        private async Task<List<ChatMessages>> getPaginatedMessagesAsync(InputPeer peer, int page, int size, Boolean onlyFiles = false)
        {
            List<ChatMessages> cm = new List<ChatMessages>();
            var messages = await client.Messages_GetHistory(peer, add_offset: (page - 1) * 100, limit: size);
            if (messages.Messages.Length == 0) return cm;
            foreach (MessageBase msgBase in messages.Messages)
            {
                if (msgBase is Message msg)
                {
                    ChatMessages cm2 = new ChatMessages();
                    cm2.message = msg;
                    cm2.isDocument = false;
                    if (msg.media is MessageMediaDocument { document: Document document })
                    {
                        cm2.isDocument = true;
                    }
                    if (onlyFiles)
                    {
                        if (cm2.isDocument)
                            cm.Add(cm2);
                    }
                    else
                    {
                        cm2.htmlMessage = client.EntitiesToHtml(msg.message, msg.entities);
                        cm2.user = messages.UserOrChat(msg.From ?? msg.Peer);
                        cm.Add(cm2);
                    }


                }
            }
            return cm;
        }

        public async Task<GridDataProviderResult<ChatMessages>> getPaginatedMessages(long id, int page, int size, Boolean onlyFiles = false)
        {
            List<ChatMessages> cm = new List<ChatMessages>();
            InputPeer peer = chats.chats[id];

            var messages = await client.Messages_GetHistory(peer, add_offset: (page - 1) * 100, limit: size);
            if (messages.Messages.Length == 0) return await Task.FromResult(new GridDataProviderResult<ChatMessages> { Data = cm, TotalCount = messages.Count });
            foreach (MessageBase msgBase in messages.Messages)
            {
                if (msgBase is Message msg)
                {
                    ChatMessages cm2 = new ChatMessages();
                    cm2.message = msg;
                    cm2.isDocument = false;
                    if (msg.media is MessageMediaDocument { document: Document document })
                    {
                        cm2.isDocument = true;
                    }
                    if (onlyFiles)
                    {
                        if (cm2.isDocument)
                            cm.Add(cm2);
                    }
                    else
                    {
                        cm2.htmlMessage = client.EntitiesToHtml(msg.message, msg.entities);
                        cm2.user = messages.UserOrChat(msg.From ?? msg.Peer);
                        cm.Add(cm2);
                    }


                }
            }
            return await Task.FromResult(new GridDataProviderResult<ChatMessages> { Data = cm, TotalCount = messages.Count });
        }

        public async Task<List<ChatMessages>> getAllFileMessages(long id)
        {
            List<ChatMessages> cm = new List<ChatMessages>();
            InputPeer peer = chats.chats[id];
            for (int offset_id = 0; ;)
            {
                var messages = await client.Messages_GetHistory(peer, offset_id);
                if (messages.Messages.Length == 0) break;
                foreach (MessageBase msgBase in messages.Messages)
                {
                    if (msgBase is Message msg)
                    {
                        ChatMessages cm2 = new ChatMessages();
                        cm2.htmlMessage = client.EntitiesToHtml(msg.message, msg.entities);
                        cm2.message = msg;

                        cm2.user = messages.UserOrChat(msg.From ?? msg.Peer);
                        cm2.isDocument = false;
                        if (msg.media is MessageMediaDocument { document: Document document })
                        {
                            cm2.isDocument = true;
                        }

                        cm.Add(cm2);
                    }
                }
                offset_id = messages.Messages[^1].ID;
            }
            return cm;
        }

        public async Task<List<ChatMessages>> getChatHistory(long id, int limit = 30, int addOffset = 0)
        {
            List<ChatMessages> cm = new List<ChatMessages>();
            var peer = chats.chats[id];

            var mess = await client.Messages_GetHistory(peer, limit: limit, add_offset: addOffset);
            //foreach(var m in mess.Messages)
            //{
            //    if(m is Message msg)
            //    {
            //        msg = TL.HtmlText.EntitiesToHtml(client, msg.message, msg.
            //    }
            //}
            foreach (MessageBase mb in mess.Messages)
            {
                if (mb is Message msg)
                {
                    ChatMessages cm2 = new ChatMessages();
                    cm2.htmlMessage = client.EntitiesToHtml(msg.message, msg.entities);
                    cm2.message = msg;

                    cm2.user = mess.UserOrChat(mb.From ?? mb.Peer);
                    cm2.isDocument = false;
                    if (msg.media is MessageMediaDocument { document: Document document })
                    {
                        cm2.isDocument = true;
                    }

                    cm.Add(cm2);
                }

            }

            return cm;
        }

        public async Task<string> getPhotoThumb(ChatBase chat)
        {
            MemoryStream ms = new MemoryStream();
            if (await client.DownloadProfilePhotoAsync(chat, ms, false, true) != 0)
            {
                return Convert.ToBase64String(ms.ToArray());
            };
            return "";

        }

        public async Task<string> downloadPhotoThumb(Photo thumb)
        {
            MemoryStream ms = new MemoryStream();
            if (await client.DownloadFileAsync(thumb, ms) != 0)
            {
                return $"data:image/jpeg;base64,{Convert.ToBase64String(ms.ToArray())}";
            };
            return "";

        }

        public async Task AddFavouriteChannel(long id)
        {
            if (!GeneralConfigStatic.config.FavouriteChannels.Contains(id))
            {
                GeneralConfigStatic.AddFavouriteChannel(id);
                await GeneralConfigStatic.SaveChanges(_db, GeneralConfigStatic.config);
                await GetFouriteChannels();
            }
        }

        public async Task RemoveFavouriteChannel(long id)
        {
            if (GeneralConfigStatic.config.FavouriteChannels.Contains(id))
            {
                GeneralConfigStatic.DeleteFavouriteChannel(id);
                await GeneralConfigStatic.SaveChanges(_db, GeneralConfigStatic.config);
                await GetFouriteChannels();
            }
        }

        public async Task<Stream> DownloadFileAndReturn(ChatMessages message, Stream ms = null, string fileName = null, string folder = null, DownloadModel model = null)
        {
            if (model == null)
            {
                model = new DownloadModel();
                model.tis = _tis;
            }
                
            model.m = message;
            model.channel = message.user;

            if (message.message.media is MessageMediaDocument { document: Document document })
            {

                model._transmitted = 0;
                model._size = document.size;
                var filename = fileName ?? document.Filename; // use document original filename, or build a name from document ID & MIME type:
                filename ??= $"{document.id}.{document.mime_type[(document.mime_type.IndexOf('/') + 1)..]}";
                if (model.name == null)
                    model.name = filename;
                // _tis.addToDownloadList(model);
                Console.WriteLine("Downloading " + filename);
                // using var fileStream = File.Create(filename);
                MemoryStream dest = new MemoryStream();
                // using var dest = new FileStream($"{Path.Combine(folder != null ? folder : Path.Combine(Environment.CurrentDirectory, "download"), filename)}", FileMode.Create, FileAccess.Write);
                await client.DownloadFileAsync(document, ms ?? dest, null, model.ProgressCallback);
                return ms ?? dest;
                // fileStream.CopyTo(dest);
                Console.WriteLine("Download finished");
            }
            else if (message.message.media is MessageMediaPhoto { photo: Photo photo })
            {
                var filename = $"{photo.id}.jpg";
                if (File.Exists(Path.Combine(Environment.CurrentDirectory!, "wwwroot", "img", "telegram", filename)))
                    return null;
                Console.WriteLine("Downloading " + filename);
                MemoryStream dest = new MemoryStream();
                // using var dest = new FileStream($"{Path.Combine(Environment.CurrentDirectory!, "wwwroot", "img", "telegram", filename)}", FileMode.Create, FileAccess.Write);
                var type = await client.DownloadFileAsync(photo, ms ?? dest, null, model.ProgressCallback);
                dest.Close(); // necessary for the renaming
                Console.WriteLine("Download finished");
                //if (type is not Storage_FileType.unknown and not Storage_FileType.partial)
                //{
                //    File.Move(Path.Combine(Environment.CurrentDirectory!, "wwwroot", "img", "telegram", filename), Path.Combine(Environment.CurrentDirectory!, "wwwroot", "img", "telegram", $"{photo.id}.{type}"), true); // rename extension
                //    filename = $"{photo.id}.{type}";
                //}
                return ms ?? dest;


            }
            return null;
        }

        public async Task<string> DownloadFile(ChatMessages message, string fileName = null, string folder = null, DownloadModel model = null, bool shouldAddToList = false)
        {
            if (model == null)
            {
                model = new DownloadModel();
                model.tis = _tis;
            }
                
            model.m = message;
            model.channel = message.user;

            if (message.message.media is MessageMediaDocument { document: Document document })
            {

                model._transmitted = 0;
                model._size = document.size;
                var filename = fileName ?? document.Filename;
                filename ??= $"{document.id}.{document.mime_type[(document.mime_type.IndexOf('/') + 1)..]}";
                model.name = filename;
                if (shouldAddToList)
                    _tis.addToDownloadList(model);
                Console.WriteLine("Downloading " + filename);
                // using var fileStream = File.Create(filename);
                using var dest = new FileStream($"{Path.Combine(folder != null ? folder : Path.Combine(Environment.CurrentDirectory, "local", "temp"), filename)}", FileMode.Create, FileAccess.Write);
                await client.DownloadFileAsync(document, dest, null, model.ProgressCallback);

                // fileStream.CopyTo(dest);
                Console.WriteLine("Download finished");
            }
            else if (message.message.media is MessageMediaPhoto { photo: Photo photo })
            {
                var filename = $"{photo.id}.jpg";
                if (File.Exists(Path.Combine(Environment.CurrentDirectory!, "wwwroot", "img", "telegram", filename)))
                    return filename;
                Console.WriteLine("Downloading " + filename);
                using var dest = new FileStream($"{Path.Combine(Environment.CurrentDirectory!, "wwwroot", "img", "telegram", filename)}", FileMode.Create, FileAccess.Write);
                var type = await client.DownloadFileAsync(photo, dest, progress: model.ProgressCallback);
                dest.Close();
                Console.WriteLine("Download finished");
                if (type is not Storage_FileType.unknown and not Storage_FileType.partial)
                {
                    File.Move(Path.Combine(Environment.CurrentDirectory!, "wwwroot", "img", "telegram", filename), Path.Combine(Environment.CurrentDirectory!, "wwwroot", "img", "telegram", $"{photo.id}.{type}"), true); // rename extension
                    filename = $"{photo.id}.{type}";
                }
                return filename;


            }
            return null;
        }

    }
}
