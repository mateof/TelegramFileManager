using BlazorBootstrap;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Syncfusion.Blazor.Inputs;
using Syncfusion.Blazor.Kanban.Internal;
using Syncfusion.Blazor.Schedule;
using Syncfusion.Blazor.Sparkline.Internal;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Threading.Channels;
using System.Threading.Tasks;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Services;
using TL;
using TL.Layer23;
using TL.Methods;
using WTelegram;

namespace TelegramDownloader.Data
{
    public class TelegramService : ITelegramService
    {
        public static bool isPremium = false;
        public static int splitSizeGB = 2;
        private const int FILESPLITSIZE = 524288; // 512 * 1024;
        private TransactionInfoService _tis { get; set; }
        private IDbService _db { get; set; }
        private static WTelegram.Client client = null;
        private static Messages_Chats chats = null;
        private static List<ChatViewBase> favouriteChannels = new List<ChatViewBase>();
        private static Mutex mut = new Mutex();
        private ILogger<IFileService> _logger { get; set; }


        public TelegramService(TransactionInfoService tis, IDbService db, ILogger<IFileService> logger)
        {
            _tis = tis;
            _db = db;
            _logger = logger;
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
            _logger.LogInformation("Starting login process");
            if (client.User == null)
                switch (await client.Login(loginInfo)) // returns which config is needed to continue login
                {
                    case "phone_number":
                        _logger.LogInformation("Login requires phone number");
                        return "phone";
                    case "verification_code":
                        _logger.LogInformation("Login requires verification code");
                        return "vc";
                    case "name":
                        _logger.LogInformation("Login requires name (sign-up)");
                        return "name";    // if sign-up is required (first/last_name)
                    case "password":
                        _logger.LogInformation("Login requires 2FA password");
                        return "pass"; // if user has enabled 2FA
                    default: break;
                }
            await getAllChats();
            SetSplitSizeGB();
            if (client.User.flags.HasFlag(User.Flags.premium))
            {
                isPremium = true;
                // splitSizeGB = 4;
            }
            _logger.LogInformation("Login successful - User: {UserId}, Premium: {IsPremium}", client.User.id, isPremium);
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
            _logger.LogInformation("Checking authentication - IsPhone: {IsPhone}", isPhone);
            if (client.UserId != null && number == null)
            {
                UserData ud = await UserService.getUserDataFromFile();
                if (ud != null)
                {
                    try
                    {
                        _logger.LogInformation("Attempting auto-login with saved user data");
                        return await DoLogin(ud.phone);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Auto-login failed, resetting client");
                        client.Dispose();
                        newClient();
                        return "phone";
                    }

                }
                else
                {
                    _logger.LogInformation("No saved user data found, requesting phone");
                    return "phone";
                }

            }
            if (isPhone && number != null)
            {
                _logger.LogInformation("Saving new phone number for authentication");
                await UserService.setUserDataToFile(new UserData(number));
            }
            try
            {
                return await DoLogin(number);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login failed for number, resetting client");
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
            _logger.LogInformation("User logging off");
            await client.Auth_LogOut();
            UserService.deleteUserDataToFile();
            client.Dispose();
            newClient();
            _logger.LogInformation("User logged off successfully");
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
            if (chats == null)
                return "";
            var peer = chats.chats[id];
            return peer.Title;
        }

        public bool isMyChat(long id)
        {
            if (chats == null)
                return false;
            var peer = chats.chats[id];
            if (peer is TL.Channel channel)
            {
                // Si es un canal, checkea la propiedad 'creator'
                bool canPost = channel.admin_rights != null;
                return canPost;
            }
            return true;
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
            _logger.LogInformation("Starting file upload - FileName: {FileName}, Size: {SizeMB:F2}MB, ChatId: {ChatId}",
                fileName, file.Length / (1024.0 * 1024.0), chatId);
            um = um ?? new UploadModel();
            um.tis = _tis;

            InputPeer peer = chats.chats[Convert.ToInt64(chatId)];
            um.name = fileName;
            um._size = file.Length;
            um._transmitted = 0;
            _tis.addToUploadList(um);
            try
            {
                var inputFile = await client.UploadFileAsync(file, fileName, um.ProgressCallback);
                var result = await client.SendMediaAsync(peer, caption ?? fileName, inputFile, mimeType);
                _logger.LogInformation("File upload completed - FileName: {FileName}, MessageId: {MessageId}", fileName, result.id);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "File upload failed - FileName: {FileName}, ChatId: {ChatId}", fileName, chatId);
                throw;
            }
        }

        public async Task deleteFile(string chatId, int idMessage)
        {
            _logger.LogInformation("Deleting file from Telegram - ChatId: {ChatId}, MessageId: {MessageId}", chatId, idMessage);
            InputPeer peer = chats.chats[Convert.ToInt64(chatId)];
            await client.DeleteMessages(peer, idMessage);
            _logger.LogInformation("File deleted successfully - MessageId: {MessageId}", idMessage);
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
                        if (msg.media is MessageMediaDocument { document: TL.Document document })
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
                    if (msg.media is MessageMediaDocument { document: TL.Document document })
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
                    if (msg.media is MessageMediaDocument { document: TL.Document document })
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
                    if (msg.media is MessageMediaDocument { document: TL.Document document })
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

        public async Task<List<ChatMessages>> getAllFileMessages(long id, int lastId = 0)
        {
            _logger.LogInformation("Fetching all file messages - ChannelId: {ChannelId}, LastId: {LastId}", id, lastId);
            List<ChatMessages> cm = new List<ChatMessages>();
            InputPeer peer = chats.chats[id];
            for (int offset_id = 0; ;)
            {
                var messages = await client.Messages_GetHistory(peer, offset_id);
                Thread.Sleep(500);
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
                        if (msg.media is MessageMediaDocument { document: TL.Document document })
                        {
                            cm2.isDocument = true;
                        }
                        if (lastId != 0 && lastId == cm2.message.id)
                        {
                            _logger.LogInformation("Reached lastId, returning {Count} messages", cm.Count);
                            return cm;
                        }
                        cm.Add(cm2);
                    }
                }
                offset_id = messages.Messages[^1].ID;
                _logger.LogDebug("Fetching telegram files - OffsetId: {OffsetId}, MessagesCount: {Count}", offset_id, cm.Count);
            }
            _logger.LogInformation("Finished fetching all file messages - Total: {Count}", cm.Count);
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
                    if (msg.media is MessageMediaDocument { document: TL.Document document })
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
        public async Task<Byte[]> DownloadFileStream(Message message, long offset, int limit)
        {
            _logger.LogDebug("DownloadFileStream - Offset: {Offset}, Limit: {Limit}", offset, limit);
            int totalLimit = limit;
            long currentOffset = offset;
            Byte[] response;
            if (message is Message msg && msg.media is MessageMediaDocument doc)
            {
                try
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        while (totalLimit > 0)
                        {
                            int totalDownloadBytes = FILESPLITSIZE;
                            if (totalLimit >= FILESPLITSIZE)
                            {
                                totalDownloadBytes = FILESPLITSIZE;
                               
                            }
                            //else if (totalLimit >= 4096)
                            //{
                            //    // Busca el mayor múltiplo de 4096 menor o igual a totalLimit
                            //    totalDownloadBytes = (totalLimit / 4096) * 4096;
                            //}
                            else
                            {
                                // Último trozo, menor de 4096
                                totalDownloadBytes = FILESPLITSIZE; // totalLimit;
                                //totalDownloadBytes = totalLimit;
                            }

                            InputDocument inputFile = doc.document;
                            var location = new InputDocumentFileLocation
                            {
                                id = inputFile.id,
                                access_hash = inputFile.access_hash,
                                file_reference = inputFile.file_reference,
                                thumb_size = ""
                            };

                            Upload_FileBase file = null;
                            try
                            {
                                file = await client.Upload_GetFile(location, currentOffset, limit: totalDownloadBytes);
                            }
                            catch (RpcException ex) when (ex.Code == 303 && ex.Message == "FILE_MIGRATE_X")
                            {
                                client = await client.GetClientForDC(-ex.X, true);
                                file = await client.Upload_GetFile(location, currentOffset, limit: totalDownloadBytes);
                            }
                            catch (RpcException ex) when (ex.Code == 400 && ex.Message == "OFFSET_INVALID")
                            {
                                _logger.LogError(ex, "OFFSET_INVALID - CurrentOffset: {Offset}, TotalLimit: {Limit}", currentOffset, totalLimit);
                                throw;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Download Error - CurrentOffset: {Offset}, TotalLimit: {Limit}", currentOffset, totalLimit);
                                throw;
                            }

                            if (file is Upload_File uploadFile)
                            {
                                //uploadFile.WriteTL(new BinaryWriter(memoryStream));
                                var fileBytes = uploadFile.bytes;
                                memoryStream.Write(fileBytes, 0, fileBytes.Length);
                                currentOffset += (totalDownloadBytes);
                                totalLimit -= (totalDownloadBytes);
                                continue;
                            }
                            throw new InvalidOperationException("Unexpected file type returned.");
                        }

                        return memoryStream.ToArray();
                    }
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException("Unexpected file type returned.");
                }
            }
                
            throw new ArgumentException("Invalid message or media type.");
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

            if (message.message.media is MessageMediaDocument { document: TL.Document document })
            {

                model._transmitted = 0;
                model._size = document.size;
                var filename = fileName ?? document.Filename; // use document original filename, or build a name from document ID & MIME type:
                filename ??= $"{document.id}.{document.mime_type[(document.mime_type.IndexOf('/') + 1)..]}";
                if (model.name == null)
                    model.name = filename;
                // _tis.addToDownloadList(model);
                _logger.LogInformation("Starting document download - FileName: {FileName}, Size: {SizeMB:F2}MB", filename, document.size / (1024.0 * 1024.0));
                // using var fileStream = File.Create(filename);
                MemoryStream dest = new MemoryStream();
                // using var dest = new FileStream($"{Path.Combine(folder != null ? folder : Path.Combine(Environment.CurrentDirectory, "download"), filename)}", FileMode.Create, FileAccess.Write);
                await client.DownloadFileAsync(document, ms ?? dest, null, model.ProgressCallback);
                _logger.LogInformation("Document download completed - FileName: {FileName}", filename);
                return ms ?? dest;
            }
            else if (message.message.media is MessageMediaPhoto { photo: Photo photo })
            {
                var filename = $"{photo.id}.jpg";
                if (File.Exists(Path.Combine(Environment.CurrentDirectory!, "wwwroot", "img", "telegram", filename)))
                {
                    _logger.LogDebug("Photo already exists locally - FileName: {FileName}", filename);
                    return null;
                }
                _logger.LogInformation("Starting photo download - FileName: {FileName}", filename);
                MemoryStream dest = new MemoryStream();
                // using var dest = new FileStream($"{Path.Combine(Environment.CurrentDirectory!, "wwwroot", "img", "telegram", filename)}", FileMode.Create, FileAccess.Write);
                var type = await client.DownloadFileAsync(photo, ms ?? dest, (PhotoSizeBase)null, model.ProgressCallback);
                dest.Close(); // necessary for the renaming
                _logger.LogInformation("Photo download completed - FileName: {FileName}", filename);
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
                model.startDate = DateTime.Now;
            }

            model.m = message;
            model.channel = message.user;

            if (message.message.media is MessageMediaDocument { document: TL.Document document })
            {

                model._transmitted = 0;
                model._size = document.size;
                var filename = fileName ?? document.Filename;
                filename ??= $"{document.id}.{document.mime_type[(document.mime_type.IndexOf('/') + 1)..]}";
                model.name = filename;
                if (shouldAddToList)
                    _tis.addToDownloadList(model);
                _logger.LogInformation("Starting file download to disk - FileName: {FileName}, Size: {SizeMB:F2}MB", filename, document.size / (1024.0 * 1024.0));
                // using var fileStream = File.Create(filename);
                using var dest = new FileStream($"{Path.Combine(folder != null ? folder : Path.Combine(Environment.CurrentDirectory, "local", "temp"), filename)}", FileMode.Create, FileAccess.Write);
                await client.DownloadFileAsync(document, dest, null, model.ProgressCallback);

                _logger.LogInformation("File download to disk completed - FileName: {FileName}", filename);
            }
            else if (message.message.media is MessageMediaPhoto { photo: Photo photo })
            {
                var filename = $"{photo.id}.jpg";
                if (File.Exists(Path.Combine(Environment.CurrentDirectory!, "wwwroot", "img", "telegram", filename)))
                {
                    _logger.LogDebug("Photo already exists - FileName: {FileName}", filename);
                    return filename;
                }
                _logger.LogInformation("Starting photo download to disk - FileName: {FileName}", filename);
                using var dest = new FileStream($"{Path.Combine(Environment.CurrentDirectory!, "wwwroot", "img", "telegram", filename)}", FileMode.Create, FileAccess.Write);
                var type = await client.DownloadFileAsync(photo, dest, progress: model.ProgressCallback);
                dest.Close();
                _logger.LogInformation("Photo download to disk completed - FileName: {FileName}", filename);
                if (type is not Storage_FileType.unknown and not Storage_FileType.partial)
                {
                    File.Move(Path.Combine(Environment.CurrentDirectory!, "wwwroot", "img", "telegram", filename), Path.Combine(Environment.CurrentDirectory!, "wwwroot", "img", "telegram", $"{photo.id}.{type}"), true); // rename extension
                    filename = $"{photo.id}.{type}";
                }
                return filename;


            }
            return null;
        }

        public async Task<List<TelegramChatDocuments>> searchAllChannelFiles(long id, int lastId = 0)
        {
            List<TelegramChatDocuments> telegramChatDocuments = new List<TelegramChatDocuments>();
            List<ChatMessages> result = await getAllFileMessages(id, lastId);

            foreach (var msg in result)
                if (msg.message is Message msgBase)
                {
                    if (msgBase.media is MessageMediaDocument mediaDoc &&
                        mediaDoc.document is TL.Document doc)
                    {
                        TelegramChatDocuments tcd = new TelegramChatDocuments();
                        tcd.id = msgBase.id;
                        tcd.documentType = DocumentType.Document;
                        tcd.name = doc.Filename;
                        tcd.fileSize = doc.size;
                        tcd.extension = Path.GetExtension(doc.Filename);
                        tcd.creationDate = msgBase.date;
                        tcd.modifiedDate = msgBase.edit_date < msgBase.date ? msgBase.date : msgBase.edit_date;
                        telegramChatDocuments.Add(tcd);
                    }
                }
            return telegramChatDocuments;
        }

        //public async Task<List<TelegramChatDocuments>> searchAllChannelFiles(long id)
        //{
        //    InputPeer channel = chats.chats[id];
        //    List<TelegramChatDocuments> telegramChatDocuments = new List<TelegramChatDocuments>();
        //    for (int offset_id = 0; ;)
        //    {
        //        Messages_MessagesBase result = await client.Messages_Search<InputMessagesFilterDocument>(
        //        peer: channel,
        //        text: "",
        //        offset_id: offset_id,
        //        limit: 0);
        //        if (result.Messages.Length == 0) break;
        //        foreach (var msgBase in result.Messages.OfType<Message>())
        //        {
        //            if (msgBase.media is MessageMediaDocument mediaDoc &&
        //                mediaDoc.document is TL.Document doc)
        //            {
        //                TelegramChatDocuments tcd = new TelegramChatDocuments();
        //                tcd.id = msgBase.id;
        //                tcd.documentType = DocumentType.Document;
        //                tcd.name = doc.Filename;
        //                tcd.extension = Path.GetExtension(doc.Filename);
        //                tcd.creationDate = msgBase.date;
        //                tcd.modifiedDate = msgBase.edit_date;
        //                telegramChatDocuments.Add(tcd);
        //            }
        //        }
        //        offset_id = result.Messages[^1].ID;
        //    }

        //    for (int offset_id = 0; ;)
        //    {
        //        Messages_MessagesBase result = await client.Messages_Search<InputMessagesFilterPhotos>(
        //        peer: channel,
        //        text: "",
        //        offset_id: offset_id,
        //        limit: 0);
        //        if (result.Messages.Length == 0) break;
        //        foreach (var msgBase in result.Messages.OfType<Message>())
        //        {
        //            if (msgBase.media is MessageMediaPhoto mediaPhoto &&
        //                mediaPhoto.photo is Photo photo)
        //            {
        //                TelegramChatDocuments tcd = new TelegramChatDocuments();
        //                tcd.documentType = DocumentType.Photo;
        //                tcd.id = msgBase.id;
        //                // Nombre "falso" porque la foto no trae filename
        //                tcd.name = $"{photo.id}.jpg";
        //                tcd.extension = ".jpg";
        //                tcd.creationDate = msgBase.date;
        //                tcd.modifiedDate = msgBase.edit_date;
        //                telegramChatDocuments.Add(tcd);
        //            }
        //        }
        //        offset_id = result.Messages[^1].ID;
        //    }

        //    return telegramChatDocuments;
        //}
    }
}
