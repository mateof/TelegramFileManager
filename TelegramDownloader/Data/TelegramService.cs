#nullable disable
using BlazorBootstrap;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Syncfusion.Blazor.Inputs;
using Syncfusion.Blazor.Kanban.Internal;
using Syncfusion.Blazor.Schedule;
using Syncfusion.Blazor.Sparkline.Internal;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
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
        private ITaskPersistenceService _persistence { get; set; }
        private static WTelegram.Client client = null;
        private static Messages_Chats chats = null;
        private static List<ChatViewBase> favouriteChannels = new List<ChatViewBase>();
        private static Mutex mut = new Mutex();
        private ILogger<IFileService> _logger { get; set; }

        // Event that fires when user successfully logs in
        public static event EventHandler OnUserLoggedIn;


        public TelegramService(TransactionInfoService tis, IDbService db, ILogger<IFileService> logger, ITaskPersistenceService persistence)
        {
            _tis = tis;
            _db = db;
            _logger = logger;
            _persistence = persistence;
            // createDownloadFolder();
            mut.WaitOne();
            try
            {
                if (client == null && HasValidCredentials())
                {
                    try
                    {
                        newClient();
                    }
                    catch (WTelegram.WTException ex) when (ex.Message.Contains("session file"))
                    {
                        // Session file is corrupted - delete it and create a new one
                        _logger.LogWarning("Session file corrupted during startup, deleting: {Message}", ex.Message);
                        var sessionPath = UserService.USERDATAFOLDER + "/WTelegram.session";
                        if (File.Exists(sessionPath))
                        {
                            File.Delete(sessionPath);
                            _logger.LogInformation("Deleted corrupted session file: {Path}", sessionPath);
                        }
                        // Create new client with fresh session
                        newClient();
                        _logger.LogInformation("Telegram client initialized with new session");
                    }
                }
                else if (!HasValidCredentials())
                {
                    _logger.LogWarning("TelegramService: Credentials not configured - setup required");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TelegramService: Could not initialize client - setup may be required");
            }
            finally
            {
                mut.ReleaseMutex();
            }
        }

        private static bool HasValidCredentials()
        {
            var apiId = GeneralConfigStatic.tlconfig?.api_id ?? Environment.GetEnvironmentVariable("api_id");
            var apiHash = GeneralConfigStatic.tlconfig?.hash_id ?? Environment.GetEnvironmentVariable("hash_id");
            return !string.IsNullOrWhiteSpace(apiId) && !string.IsNullOrWhiteSpace(apiHash);
        }

        public bool IsConfigured => HasValidCredentials() && client != null;

        /// <summary>
        /// Initializes or reinitializes the Telegram client.
        /// Call this after setup is complete to create the client with new credentials.
        /// </summary>
        public void InitializeClient()
        {
            if (client != null)
            {
                _logger.LogInformation("Client already initialized");
                return;
            }

            if (!HasValidCredentials())
            {
                _logger.LogWarning("Cannot initialize client - credentials not configured");
                return;
            }

            mut.WaitOne();
            try
            {
                if (client == null) // Double-check after acquiring lock
                {
                    _logger.LogInformation("Initializing Telegram client after setup");
                    try
                    {
                        newClient();
                        _logger.LogInformation("Telegram client initialized successfully");
                    }
                    catch (WTelegram.WTException ex) when (ex.Message.Contains("session file"))
                    {
                        // Session file is corrupted - delete it and create a new one
                        _logger.LogWarning("Session file corrupted, deleting and creating new session: {Message}", ex.Message);
                        var sessionPath = UserService.USERDATAFOLDER + "/WTelegram.session";
                        if (File.Exists(sessionPath))
                        {
                            File.Delete(sessionPath);
                            _logger.LogInformation("Deleted corrupted session file: {Path}", sessionPath);
                        }
                        // Create new client with fresh session
                        newClient();
                        _logger.LogInformation("Telegram client initialized with new session");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Telegram client: {Message}", ex.Message);
                throw;
            }
            finally
            {
                mut.ReleaseMutex();
            }
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
            ApplyConfiguredParallelTransfers(client);
            if (GeneralConfigStatic.config.ShouldShowLogInTerminal)
            {
                // WTelegram.Helpers.Log = (lvl, str) => Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{"TDIWE!"[lvl]}] {str}");
            } else
            {
                WTelegram.Helpers.Log = (lvl, str) => { };
            }

        }

        // WTelegramClient requests file parts through a per-client semaphore that
        // defaults to 2 parts in flight, capping transfers at ~1MB per round-trip.
        // Tracks the value applied to each client instance (main client and the
        // media-DC clients WTelegram creates internally, which do NOT inherit the
        // main client's ParallelTransfers).
        private static readonly ConditionalWeakTable<WTelegram.Client, StrongBox<int>> appliedParallelTransfers = new();
        private const int WTELEGRAM_DEFAULT_PARALLEL_TRANSFERS = 2;

        public static int GetConfiguredParallelTransfers()
        {
            return Math.Clamp(GeneralConfigStatic.config?.ParallelTransfers ?? 4, 1, 16);
        }

        private static void ApplyConfiguredParallelTransfers(WTelegram.Client c)
        {
            if (c == null)
                return;
            int desired = GetConfiguredParallelTransfers();
            int delta;
            lock (appliedParallelTransfers)
            {
                StrongBox<int> applied = appliedParallelTransfers.GetOrCreateValue(c);
                if (applied.Value == 0)
                    applied.Value = WTELEGRAM_DEFAULT_PARALLEL_TRANSFERS;
                delta = desired - applied.Value;
                if (delta == 0)
                    return;
                applied.Value = desired;
            }
            try
            {
                // The ParallelTransfers setter adjusts the semaphore relative to its
                // CURRENT count, which is lower while parts are in flight. Applying
                // our delta on top of the current value keeps the configured maximum
                // correct even if a transfer is running on this client.
                c.ParallelTransfers = c.ParallelTransfers + delta;
            }
            catch (Exception)
            {
                // Never let a tuning failure break a transfer.
            }
        }

        /// <summary>
        /// Resolves the client instance that WTelegram's DownloadFileAsync will use
        /// for the given file DC (dc_id == 0 means the main client) and applies the
        /// configured chunk parallelism to it before the transfer starts.
        /// </summary>
        private async Task PrepareTransferClientAsync(int dcId)
        {
            try
            {
                WTelegram.Client c = dcId == 0 ? client : await client.GetClientForDC(-dcId, true);
                ApplyConfiguredParallelTransfers(c);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not prepare transfer client for DC {DcId}", dcId);
            }
        }

        #region Multi-connection downloads

        // Telegram enforces its throughput limit PER CONNECTION (~5-6 MB/s), so a
        // single MTProto connection cannot go faster regardless of how many chunks
        // are pipelined. Official clients reach high speeds by opening several
        // sessions to the file's DC and splitting the file between them. This pool
        // holds extra authorized clients per DC (bootstrapped once from the main
        // client via auth.exportAuthorization/importAuthorization, persisted as
        // session files and reused across restarts).
        private class DcDownloadPool
        {
            public readonly SemaphoreSlim initLock = new SemaphoreSlim(1, 1);
            public readonly List<WTelegram.Client> clients = new List<WTelegram.Client>();
            public bool bootstrapFailed = false;
        }

        private static readonly Dictionary<int, DcDownloadPool> downloadPools = new Dictionary<int, DcDownloadPool>();
        private const int MULTICONN_PART_SIZE = 1024 * 1024;        // upload.getFile max limit per request
        private const int MULTICONN_BLOCK_SIZE = 4 * 1024 * 1024;   // work unit assigned to a connection
        private const long MULTICONN_MIN_FILE_SIZE = 32L * 1024 * 1024;

        public static int GetConfiguredDownloadConnections()
        {
            return Math.Clamp(GeneralConfigStatic.config?.DownloadConnections ?? 4, 2, 8);
        }

        private static bool ShouldUseMultiConnection(TL.Document document)
        {
            GeneralConfig cfg = GeneralConfigStatic.config;
            return cfg != null && cfg.EnableMultiConnectionDownloads && document.size >= MULTICONN_MIN_FILE_SIZE;
        }

        private async Task<List<WTelegram.Client>> GetDownloadPoolAsync(int dcId, int count)
        {
            DcDownloadPool pool;
            lock (downloadPools)
            {
                if (!downloadPools.TryGetValue(dcId, out pool))
                    downloadPools[dcId] = pool = new DcDownloadPool();
            }
            if (pool.bootstrapFailed)
                return new List<WTelegram.Client>();
            await pool.initLock.WaitAsync();
            try
            {
                pool.clients.RemoveAll(c =>
                {
                    if (!c.Disconnected) return false;
                    try { c.Dispose(); } catch { }
                    return true;
                });
                while (pool.clients.Count < count)
                {
                    WTelegram.Client pc = await CreateDownloadPoolClientAsync(dcId, pool.clients.Count);
                    if (pc == null)
                    {
                        // Do not retry the bootstrap on every download if the DC
                        // refuses it (e.g. exportAuthorization not allowed).
                        if (pool.clients.Count == 0)
                            pool.bootstrapFailed = true;
                        break;
                    }
                    pool.clients.Add(pc);
                }
                return pool.clients.Take(count).ToList();
            }
            finally
            {
                pool.initLock.Release();
            }
        }

        private async Task<WTelegram.Client> CreateDownloadPoolClientAsync(int dcId, int index)
        {
            string sessionPath = Path.Combine(UserService.USERDATAFOLDER, $"WTelegram_dl_dc{dcId}_{index}.session");
            try
            {
                TL.Config tlConfig = await client.Help_GetConfig();
                DcOption dc = tlConfig.dc_options
                    .Where(x => x.id == dcId && (x.flags & (DcOption.Flags.ipv6 | DcOption.Flags.cdn | DcOption.Flags.tcpo_only)) == 0)
                    .OrderBy(x => (x.flags & DcOption.Flags.media_only) == 0 ? 0 : 1)
                    .FirstOrDefault();
                if (dc == null)
                {
                    _logger.LogWarning("No suitable address found for DC {Dc} - multi-connection download unavailable", dcId);
                    return null;
                }
                string apiId = GeneralConfigStatic.tlconfig?.api_id ?? Environment.GetEnvironmentVariable("api_id");
                string apiHash = GeneralConfigStatic.tlconfig?.hash_id ?? Environment.GetEnvironmentVariable("hash_id");
                WTelegram.Client pc = new WTelegram.Client(what => what switch
                {
                    "api_id" => apiId,
                    "api_hash" => apiHash,
                    "session_pathname" => sessionPath,
                    "server_address" => $"{dc.ip_address}:{dc.port}",
                    "device_model" => "TFM parallel download",
                    _ => null
                });
                try
                {
                    await pc.ConnectAsync();
                    bool authorized = false;
                    try
                    {
                        await pc.Users_GetUsers(InputUser.Self);
                        authorized = true;
                    }
                    catch (RpcException)
                    {
                        // Fresh session, or a previously created one revoked from
                        // the account's device list: (re)import the authorization.
                    }
                    if (!authorized)
                    {
                        Auth_ExportedAuthorization exported = await client.Auth_ExportAuthorization(dcId);
                        await pc.Auth_ImportAuthorization(exported.id, exported.bytes);
                        await pc.Users_GetUsers(InputUser.Self);
                    }
                    ApplyConfiguredParallelTransfers(pc);
                    _logger.LogInformation("Download pool client {Index} ready for DC {Dc}", index, dcId);
                    return pc;
                }
                catch
                {
                    try { pc.Dispose(); } catch { }
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not create download pool client {Index} for DC {Dc} - falling back to single-connection downloads", index, dcId);
                return null;
            }
        }

        /// <summary>
        /// Downloads a document by splitting it in blocks served concurrently by
        /// several pool connections, writing each part at its absolute offset.
        /// Returns false (leaving the destination empty) when the pool is not
        /// available or the transfer failed in a recoverable way, so the caller
        /// can fall back to the standard sequential download.
        /// </summary>
        private async Task<bool> TryMultiConnectionDownloadAsync(TL.Document document, FileStream dest, DownloadModel model)
        {
            long size = document.size;
            List<WTelegram.Client> pool;
            try
            {
                pool = await GetDownloadPoolAsync(document.dc_id, GetConfiguredDownloadConnections());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Download pool unavailable for DC {Dc}", document.dc_id);
                return false;
            }
            if (pool.Count < 2)
                return false;

            var location = new InputDocumentFileLocation
            {
                id = document.id,
                access_hash = document.access_hash,
                file_reference = document.file_reference,
                thumb_size = ""
            };

            _logger.LogInformation("Multi-connection download - FileName: {Name}, Size: {SizeMB:F2}MB, Connections: {Connections}",
                model.name, size / (1024.0 * 1024.0), pool.Count);

            long blockCount = (size + MULTICONN_BLOCK_SIZE - 1) / MULTICONN_BLOCK_SIZE;
            bool[] blockDone = new bool[blockCount];
            long confirmedBlocks = 0;
            long nextBlock = -1;
            object progressLock = new object();
            using CancellationTokenSource cts = new CancellationTokenSource();
            dest.SetLength(size);
            var handle = dest.SafeFileHandle;

            void ReportPart(long block, int received, bool blockCompleted)
            {
                long confirmed;
                lock (progressLock)
                {
                    if (blockCompleted)
                    {
                        blockDone[block] = true;
                        while (confirmedBlocks < blockCount && blockDone[confirmedBlocks])
                            confirmedBlocks++;
                    }
                    confirmed = Math.Min(size, confirmedBlocks * (long)MULTICONN_BLOCK_SIZE);
                }
                // Throws when the task gets canceled or paused, stopping the workers.
                model.ReportParallelProgress(confirmed, received, size);
            }

            async Task Worker(WTelegram.Client pc)
            {
                while (!cts.IsCancellationRequested)
                {
                    long block = Interlocked.Increment(ref nextBlock);
                    if (block >= blockCount)
                        return;
                    long offset = block * (long)MULTICONN_BLOCK_SIZE;
                    long end = Math.Min(size, offset + MULTICONN_BLOCK_SIZE);
                    while (offset < end)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        Upload_FileBase resp = null;
                        for (int attempt = 1; ; attempt++)
                        {
                            try
                            {
                                resp = await pc.Upload_GetFile(location, offset, limit: MULTICONN_PART_SIZE);
                                break;
                            }
                            catch (Exception) when (attempt < 3 && !cts.IsCancellationRequested)
                            {
                                await Task.Delay(1000 * attempt);
                            }
                        }
                        if (resp is not Upload_File part)
                            throw new InvalidOperationException($"Unexpected {resp?.GetType().Name} from Upload_GetFile (CDN-served files are not supported)");
                        if (part.bytes.Length == 0)
                            throw new InvalidOperationException($"Empty chunk at offset {offset}");
                        RandomAccess.Write(handle, part.bytes, offset);
                        offset += part.bytes.Length;
                        ReportPart(block, part.bytes.Length, offset >= end);
                    }
                }
            }

            async Task GuardedWorker(WTelegram.Client pc)
            {
                try { await Worker(pc); }
                catch { cts.Cancel(); throw; }
            }

            try
            {
                await Task.WhenAll(pool.Select(pc => Task.Run(() => GuardedWorker(pc))));
                await dest.FlushAsync();
                return true;
            }
            catch (Exception ex)
            {
                if (model.state == StateTask.Canceled || model.state == StateTask.Paused)
                    throw;
                _logger.LogWarning(ex, "Multi-connection download failed, falling back to sequential - FileName: {Name}", model.name);
                dest.SetLength(0);
                dest.Position = 0;
                model._transmitted = 0;
                return false;
            }
        }

        #endregion

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

            // Fire the login event to notify subscribers (like TaskResumeService)
            try
            {
                OnUserLoggedIn?.Invoke(this, EventArgs.Empty);
                _logger.LogInformation("OnUserLoggedIn event fired");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error firing OnUserLoggedIn event");
            }

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
            // Return early if client is not initialized (setup required)
            if (client == null)
            {
                _logger.LogWarning("checkAuth called but client is null - setup required");
                return "setup_required";
            }

            _logger.LogInformation("Checking authentication - IsPhone: {IsPhone}", isPhone);
            if (client.UserId != 0 && number == null)
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
                if (number == null)
                    return "phone";
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

        public bool isChannelOwner(long id)
        {
            if (chats == null)
                return false;
            var peer = chats.chats[id];
            if (peer is TL.Channel channel)
            {
                return channel.IsActive && channel.flags.HasFlag(TL.Channel.Flags.creator);
            }
            return false;
        }

        public async Task LeaveChannel(long id)
        {
            try
            {
                _logger.LogInformation("Leaving channel with ID: {Id}", id);
                var peer = chats.chats[id];
                if (peer is TL.Channel channel)
                {
                    var inputChannel = new InputChannel(channel.id, channel.access_hash);
                    await client.Channels_LeaveChannel(inputChannel);
                    _logger.LogInformation("Successfully left channel: {Id}", id);
                }
                else
                {
                    throw new Exception("The specified chat is not a channel");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error leaving channel: {Id}", id);
                throw;
            }
        }

        public async Task DeleteChannel(long id)
        {
            try
            {
                _logger.LogInformation("Deleting channel with ID: {Id}", id);
                var peer = chats.chats[id];
                if (peer is TL.Channel channel)
                {
                    if (!channel.flags.HasFlag(TL.Channel.Flags.creator))
                    {
                        throw new Exception("You are not the owner of this channel");
                    }
                    var inputChannel = new InputChannel(channel.id, channel.access_hash);
                    await client.Channels_DeleteChannel(inputChannel);
                    _logger.LogInformation("Successfully deleted channel: {Id}", id);
                }
                else
                {
                    throw new Exception("The specified chat is not a channel");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting channel: {Id}", id);
                throw;
            }
        }

        public (string? name, bool exists) GetChannelInfo(long id)
        {
            try
            {
                if (chats == null || !chats.chats.ContainsKey(id))
                {
                    return (null, false);
                }

                var peer = chats.chats[id];
                if (peer is TL.Channel channel)
                {
                    return (channel.title, true);
                }
                else if (peer is TL.Chat chat)
                {
                    return (chat.title, true);
                }

                return (peer.ToString(), true);
            }
            catch
            {
                return (null, false);
            }
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
                    cb.img64 = ""; // Images loaded via /api/channel/image/{id} endpoint
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
                    cb.img64 = ""; // Images loaded via /api/channel/image/{id} endpoint
                    allChats.Add(cb);
                }
            return allChats;
        }

        /// <summary>
        /// Gets all chats organized by Telegram folders (dialog filters)
        /// </summary>
        public async Task<ChatsWithFolders> getChatsWithFolders()
        {
            var result = new ChatsWithFolders();

            if (!checkUserLogin()) return result;

            try
            {
                // Get all chats first
                var allChats = await getAllSavedChats();
                var chatDict = allChats.ToDictionary(c => c.chat.ID);
                var chatsInFolders = new HashSet<long>();

                // Get dialog filters (folders)
                var dialogFilters = await client.Messages_GetDialogFilters();

                if (dialogFilters?.filters != null)
                {
                    foreach (var filter in dialogFilters.filters)
                    {
                        ChatFolderView folder = null;
                        InputPeer[] includePeers = null;

                        // Handle regular folders (DialogFilter)
                        if (filter is DialogFilter df)
                        {
                            folder = new ChatFolderView
                            {
                                Id = df.id,
                                Title = df.title?.text ?? df.title?.ToString() ?? "Folder",
                                IconEmoji = df.emoticon ?? "📁"
                            };
                            includePeers = df.include_peers;
                        }
                        // Handle shared folders (DialogFilterChatlist)
                        else if (filter is DialogFilterChatlist dfc)
                        {
                            folder = new ChatFolderView
                            {
                                Id = dfc.id,
                                Title = dfc.title?.text ?? dfc.title?.ToString() ?? "Shared Folder",
                                IconEmoji = dfc.emoticon ?? "🔗"
                            };
                            includePeers = dfc.include_peers;
                        }

                        if (folder != null && includePeers != null)
                        {
                            foreach (var peer in includePeers)
                            {
                                long peerId = peer switch
                                {
                                    InputPeerChannel ipc => ipc.channel_id,
                                    InputPeerChat ipchat => ipchat.chat_id,
                                    InputPeerUser ipu => ipu.user_id,
                                    _ => 0
                                };

                                if (peerId != 0 && chatDict.TryGetValue(peerId, out var chatView))
                                {
                                    folder.Chats.Add(chatView);
                                    chatsInFolders.Add(peerId);
                                }
                            }

                            // Only add folders that have chats
                            if (folder.Chats.Count > 0)
                            {
                                result.Folders.Add(folder);
                            }
                        }
                    }
                }

                // Add ungrouped chats (not in any folder)
                result.UngroupedChats = allChats
                    .Where(c => !chatsInFolders.Contains(c.chat.ID))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting chat folders");
                // Fallback: return all chats as ungrouped
                result.UngroupedChats = await getAllSavedChats();
            }

            return result;
        }

        /// <summary>
        /// Preloads channels and configuration at startup if user is logged in.
        /// This ensures channels are available for API calls without requiring UI access first.
        /// </summary>
        public async Task PreloadChannelsAsync()
        {
            if (!checkUserLogin())
            {
                _logger.LogInformation("PreloadChannelsAsync: User not logged in, skipping preload");
                return;
            }

            try
            {
                _logger.LogInformation("PreloadChannelsAsync: Starting channel preload...");

                // Load all channels into cache
                var channels = await getAllChats();
                _logger.LogInformation("PreloadChannelsAsync: Loaded {Count} channels", channels.Count);

                // Also preload folders
                var folders = await getChatsWithFolders();
                _logger.LogInformation("PreloadChannelsAsync: Loaded {FolderCount} folders with {UngroupedCount} ungrouped chats",
                    folders.Folders.Count, folders.UngroupedChats.Count);

                // Preload favourites
                var favourites = await GetFouriteChannels(true);
                _logger.LogInformation("PreloadChannelsAsync: Loaded {Count} favourite channels", favourites.Count);

                _logger.LogInformation("PreloadChannelsAsync: Channel preload completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PreloadChannelsAsync: Error preloading channels");
            }
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
            um.startDate = DateTime.Now;
            um._transmitted = 0;
            _tis.addToUploadList(um);

            // Persist the upload task
            try
            {
                um.OnProgressPersist = async (transmitted, progress, state) =>
                {
                    await _persistence.UpdateProgress(um._internalId, transmitted, progress, state);
                };
                await _persistence.PersistUpload(um, chatId, um.path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist upload task - continuing without persistence");
            }

            try
            {
                ApplyConfiguredParallelTransfers(client);
                var inputFile = await client.UploadFileAsync(file, fileName, um.ProgressCallback);
                var result = await client.SendMediaAsync(peer, caption ?? fileName, inputFile, mimeType);
                _logger.LogInformation("File upload completed - FileName: {FileName}, MessageId: {MessageId}", fileName, result.id);

                // Mark task as completed
                await _persistence.MarkCompleted(um._internalId);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "File upload failed - FileName: {FileName}, ChatId: {ChatId}", fileName, chatId);

                // Mark task as error
                await _persistence.MarkError(um._internalId, ex.Message);

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

        public async Task<byte[]?> DownloadChannelPhoto(long channelId)
        {
            if (!checkUserLogin() || chats == null) return null;

            if (chats.chats.TryGetValue(channelId, out var chat) && chat.Photo != null)
            {
                using var ms = new MemoryStream();
                // big=true for full resolution, miniThumb=false to avoid tiny thumbnail
                if (await client.DownloadProfilePhotoAsync(chat, ms, big: true, miniThumb: false) != 0)
                {
                    return ms.ToArray();
                }
            }
            return null;
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
                            else
                            {
                                totalDownloadBytes = FILESPLITSIZE;
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
                                var dcClient = await client.GetClientForDC(-ex.X, true);
                                file = await dcClient.Upload_GetFile(location, currentOffset, limit: totalDownloadBytes);
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
                catch
                {
                    throw new InvalidOperationException("Unexpected file type returned.");
                }
            }

            throw new ArgumentException("Invalid message or media type.");
        }

        /// <summary>
        /// Streams a byte range from Telegram in 512KB chunks, yielding each chunk as it
        /// arrives instead of buffering the whole range in memory. Offset must be 4KB-aligned
        /// (callers align to 512KB). Stops early if Telegram signals end of file.
        /// </summary>
        public async IAsyncEnumerable<byte[]> DownloadFileStreamChunks(Message message, long offset, long limit, [EnumeratorCancellation] CancellationToken ct = default)
        {
            _logger.LogDebug("DownloadFileStreamChunks - Offset: {Offset}, Limit: {Limit}", offset, limit);

            if (message is not Message msg || msg.media is not MessageMediaDocument doc)
                throw new ArgumentException("Invalid message or media type.");

            InputDocument inputFile = doc.document;
            long currentOffset = offset;
            long remaining = limit;

            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();

                var location = new InputDocumentFileLocation
                {
                    id = inputFile.id,
                    access_hash = inputFile.access_hash,
                    file_reference = inputFile.file_reference,
                    thumb_size = ""
                };

                Upload_FileBase file;
                try
                {
                    file = await client.Upload_GetFile(location, currentOffset, limit: FILESPLITSIZE);
                }
                catch (RpcException ex) when (ex.Code == 303 && ex.Message == "FILE_MIGRATE_X")
                {
                    var dcClient = await client.GetClientForDC(-ex.X, true);
                    file = await dcClient.Upload_GetFile(location, currentOffset, limit: FILESPLITSIZE);
                }

                if (file is not Upload_File uploadFile)
                    throw new InvalidOperationException("Unexpected file type returned.");

                if (uploadFile.bytes.Length == 0)
                    yield break;

                yield return uploadFile.bytes;

                currentOffset += uploadFile.bytes.Length;
                remaining -= uploadFile.bytes.Length;

                // Telegram returns fewer bytes than requested only at end of file
                if (uploadFile.bytes.Length < FILESPLITSIZE)
                    yield break;
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

            if (message.message.media is MessageMediaDocument { document: TL.Document document })
            {

                model._transmitted = 0;
                model._size = document.size;
                var filename = fileName ?? document.Filename;
                filename ??= $"{document.id}.{document.mime_type[(document.mime_type.IndexOf('/') + 1)..]}";
                if (model.name == null)
                    model.name = filename;
                _logger.LogInformation("Starting document download - FileName: {FileName}, Size: {SizeMB:F2}MB", filename, document.size / (1024.0 * 1024.0));
                MemoryStream dest = new MemoryStream();
                await PrepareTransferClientAsync(document.dc_id);
                await client.DownloadFileAsync(document, ms ?? dest, (PhotoSizeBase)null, model.ProgressCallback);
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
                var type = await client.DownloadFileAsync(photo, ms ?? dest, (PhotoSizeBase)null, model.ProgressCallback);
                dest.Close();
                _logger.LogInformation("Photo download completed - FileName: {FileName}", filename);
                return ms ?? dest;
            }
            return null;
        }

        public async Task<Stream> DownloadFileAndReturnWithOffset(ChatMessages message, Stream ms = null, string fileName = null, string folder = null, DownloadModel model = null, long offset = 0)
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
                model._size = document.size;
                model._transmitted = offset;
                var filename = fileName ?? document.Filename;
                filename ??= $"{document.id}.{document.mime_type[(document.mime_type.IndexOf('/') + 1)..]}";
                if (model.name == null)
                    model.name = filename;

                _logger.LogInformation("Starting document download with offset - FileName: {FileName}, Size: {SizeMB:F2}MB, Offset: {Offset}",
                    filename, document.size / (1024.0 * 1024.0), offset);

                if (offset == 0)
                {
                    MemoryStream dest = new MemoryStream();
                    await PrepareTransferClientAsync(document.dc_id);
                    await client.DownloadFileAsync(document, ms ?? dest, (PhotoSizeBase)null, model.ProgressCallback);
                    _logger.LogInformation("Document download completed - FileName: {FileName}", filename);
                    return ms ?? dest;
                }

                Stream targetStream = ms ?? new MemoryStream();
                long currentOffset = offset;
                long totalSize = document.size;

                InputDocument inputFile = document;
                var location = new InputDocumentFileLocation
                {
                    id = inputFile.id,
                    access_hash = inputFile.access_hash,
                    file_reference = inputFile.file_reference,
                    thumb_size = ""
                };

                _logger.LogInformation("Resuming download from offset {Offset} of {TotalSize} bytes", currentOffset, totalSize);

                while (currentOffset < totalSize)
                {
                    int chunkSize = FILESPLITSIZE;
                    if (currentOffset + chunkSize > totalSize)
                    {
                        long remaining = totalSize - currentOffset;
                        chunkSize = (int)((remaining + 4095) / 4096 * 4096);
                        if (chunkSize > FILESPLITSIZE)
                            chunkSize = FILESPLITSIZE;
                    }

                    Upload_FileBase file = null;
                    try
                    {
                        file = await client.Upload_GetFile(location, currentOffset, limit: chunkSize);
                    }
                    catch (RpcException ex) when (ex.Code == 303 && ex.Message == "FILE_MIGRATE_X")
                    {
                        _logger.LogWarning("DC migration required, switching to DC {DC}", -ex.X);
                        var dcClient = await client.GetClientForDC(-ex.X, true);
                        file = await dcClient.Upload_GetFile(location, currentOffset, limit: chunkSize);
                    }
                    catch (RpcException ex) when (ex.Code == 400 && ex.Message == "OFFSET_INVALID")
                    {
                        _logger.LogError(ex, "OFFSET_INVALID at offset {Offset} - file may have changed", currentOffset);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Download error at offset {Offset}", currentOffset);
                        throw;
                    }

                    if (file is Upload_File uploadFile)
                    {
                        var fileBytes = uploadFile.bytes;
                        if (fileBytes.Length == 0)
                        {
                            _logger.LogInformation("Received empty chunk - download complete at offset {Offset}", currentOffset);
                            break;
                        }

                        await targetStream.WriteAsync(fileBytes, 0, fileBytes.Length);
                        currentOffset += fileBytes.Length;
                        model._transmitted = currentOffset;
                        model.ProgressCallback(currentOffset, totalSize);
                        continue;
                    }

                    throw new InvalidOperationException("Unexpected file type returned from Telegram API.");
                }

                _logger.LogInformation("Document download with offset completed - FileName: {FileName}, FinalOffset: {Offset}",
                    filename, currentOffset);
                return targetStream;
            }
            else if (message.message.media is MessageMediaPhoto { photo: Photo photo })
            {
                _logger.LogInformation("Photo download (no resume support) - PhotoId: {PhotoId}", photo.id);
                var filename = $"{photo.id}.jpg";
                MemoryStream dest = new MemoryStream();
                var type = await client.DownloadFileAsync(photo, ms ?? dest, (PhotoSizeBase)null, model.ProgressCallback);
                dest.Close();
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
                using var dest = new FileStream($"{Path.Combine(folder != null ? folder : Path.Combine(Environment.CurrentDirectory, "local", "temp"), filename)}", FileMode.Create, FileAccess.Write);
                bool multiConnDone = false;
                if (ShouldUseMultiConnection(document))
                    multiConnDone = await TryMultiConnectionDownloadAsync(document, dest, model);
                if (!multiConnDone)
                {
                    await PrepareTransferClientAsync(document.dc_id);
                    await client.DownloadFileAsync(document, dest, (PhotoSizeBase)null, model.ProgressCallback);
                }
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
                    File.Move(Path.Combine(Environment.CurrentDirectory!, "wwwroot", "img", "telegram", filename), Path.Combine(Environment.CurrentDirectory!, "wwwroot", "img", "telegram", $"{photo.id}.{type}"), true);
                    filename = $"{photo.id}.{type}";
                }
                return filename;
            }
            return null;
        }

        /// <summary>
        /// Optimized method to get all media files from a channel using Messages_Search with filters.
        /// This is much faster than iterating through all messages because Telegram's servers do the filtering.
        /// </summary>
        /// <param name="id">Channel ID</param>
        /// <param name="lastId">Only get messages with ID greater than this (for incremental updates)</param>
        /// <param name="options">Options specifying which media types to fetch</param>
        /// <returns>List of documents found in the channel</returns>
        public async Task<List<TelegramChatDocuments>> searchAllChannelFiles(long id, int lastId = 0, Models.RefreshChannelOptions? options = null)
        {
            options ??= new Models.RefreshChannelOptions();

            var startTime = DateTime.Now;
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");
            _logger.LogInformation("Starting channel refresh - ChannelId: {ChannelId}, LastId: {LastId}", id, lastId);
            _logger.LogInformation("Selected types: {Types}", options.GetSelectionSummary());
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");

            List<TelegramChatDocuments> telegramChatDocuments = new List<TelegramChatDocuments>();
            InputPeer peer = chats.chats[id];

            int phaseNumber = 0;
            int docsCount = 0, audioCount = 0, videoCount = 0, photosCount = 0;

            // Search for documents (general files like PDF, ZIP, etc.)
            if (options.IncludeDocuments)
            {
                phaseNumber++;
                _logger.LogInformation("📁 Phase {Phase}: Searching for documents (PDF, ZIP, etc.)...", phaseNumber);
                docsCount = await SearchDocuments(peer, lastId, telegramChatDocuments);
                _logger.LogInformation("📁 Phase {Phase} complete: Found {Count} documents", phaseNumber, docsCount);
            }

            // Search for audio files
            if (options.IncludeAudio)
            {
                phaseNumber++;
                _logger.LogInformation("🎵 Phase {Phase}: Searching for audio files...", phaseNumber);
                audioCount = await SearchAudio(peer, lastId, telegramChatDocuments);
                _logger.LogInformation("🎵 Phase {Phase} complete: Found {Count} audio files", phaseNumber, audioCount);
            }

            // Search for video files
            if (options.IncludeVideo)
            {
                phaseNumber++;
                _logger.LogInformation("🎬 Phase {Phase}: Searching for video files...", phaseNumber);
                videoCount = await SearchVideo(peer, lastId, telegramChatDocuments);
                _logger.LogInformation("🎬 Phase {Phase} complete: Found {Count} video files", phaseNumber, videoCount);
            }

            // Search for photos
            if (options.IncludePhotos)
            {
                phaseNumber++;
                _logger.LogInformation("🖼️ Phase {Phase}: Searching for photos...", phaseNumber);
                photosCount = await SearchPhotos(peer, lastId, telegramChatDocuments);
                _logger.LogInformation("🖼️ Phase {Phase} complete: Found {Count} photos", phaseNumber, photosCount);
            }

            var elapsed = DateTime.Now - startTime;
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");
            _logger.LogInformation("✅ Refresh complete - Total: {Total} files", telegramChatDocuments.Count);
            _logger.LogInformation("   📁 Documents: {Docs} | 🎵 Audio: {Audio} | 🎬 Video: {Video} | 🖼️ Photos: {Photos}",
                docsCount, audioCount, videoCount, photosCount);
            _logger.LogInformation("⏱️ Time elapsed: {Elapsed:mm\\:ss\\.fff}", elapsed);
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");

            return telegramChatDocuments;
        }

        private async Task<int> SearchDocuments(InputPeer peer, int lastId, List<TelegramChatDocuments> results)
        {
            int totalCount = 0;
            int fetchedCount = 0;
            int addedCount = 0;
            int batchNumber = 0;
            int initialResultsCount = results.Count;
            HashSet<int> existingIds = results.Select(r => r.id).ToHashSet();

            for (int offset_id = 0; ;)
            {
                batchNumber++;
                var searchResult = await client.Messages_Search<InputMessagesFilterDocument>(
                    peer: peer,
                    q: "",
                    offset_id: offset_id,
                    limit: 100);

                if (searchResult.Messages.Length == 0) break;

                // Get total count from first batch
                if (totalCount == 0)
                {
                    totalCount = searchResult.Count;
                    _logger.LogInformation("   📊 Total documents in channel: {Total}", totalCount);
                }

                int batchAdded = 0;
                foreach (var msgBase in searchResult.Messages.OfType<Message>())
                {
                    fetchedCount++;

                    // Skip messages we already have (incremental update)
                    if (lastId > 0 && msgBase.id <= lastId)
                    {
                        _logger.LogInformation("   ⏭️ Reached lastId ({LastId}), stopping. Fetched: {Fetched}, Added: {Added}",
                            lastId, fetchedCount, addedCount);
                        return results.Count - initialResultsCount;
                    }

                    // Skip if already added (from another filter)
                    if (existingIds.Contains(msgBase.id)) continue;

                    if (msgBase.media is MessageMediaDocument mediaDoc &&
                        mediaDoc.document is TL.Document doc)
                    {
                        if (!string.IsNullOrEmpty(doc.Filename))
                        {
                            results.Add(new TelegramChatDocuments
                            {
                                id = msgBase.id,
                                documentType = DocumentType.Document,
                                name = doc.Filename,
                                fileSize = doc.size,
                                extension = Path.GetExtension(doc.Filename),
                                creationDate = msgBase.date,
                                modifiedDate = msgBase.edit_date < msgBase.date ? msgBase.date : msgBase.edit_date
                            });
                            existingIds.Add(msgBase.id);
                            addedCount++;
                            batchAdded++;
                        }
                    }
                }

                double progress = totalCount > 0 ? (double)fetchedCount / totalCount * 100 : 0;
                _logger.LogInformation("   📦 Batch {Batch}: Fetched {Fetched}/{Total} ({Progress:F1}%) - Added {BatchAdded} docs (Total added: {Added})",
                    batchNumber, fetchedCount, totalCount, progress, batchAdded, addedCount);

                offset_id = searchResult.Messages[^1].ID;

                // Small delay to avoid rate limiting
                await Task.Delay(100);
            }

            return results.Count - initialResultsCount;
        }

        private async Task<int> SearchAudio(InputPeer peer, int lastId, List<TelegramChatDocuments> results)
        {
            int totalCount = 0;
            int fetchedCount = 0;
            int addedCount = 0;
            int batchNumber = 0;
            int initialResultsCount = results.Count;
            HashSet<int> existingIds = results.Select(r => r.id).ToHashSet();

            for (int offset_id = 0; ;)
            {
                batchNumber++;
                var searchResult = await client.Messages_Search<InputMessagesFilterMusic>(
                    peer: peer,
                    q: "",
                    offset_id: offset_id,
                    limit: 100);

                if (searchResult.Messages.Length == 0) break;

                // Get total count from first batch
                if (totalCount == 0)
                {
                    totalCount = searchResult.Count;
                    _logger.LogInformation("   📊 Total audio files in channel: {Total}", totalCount);
                }

                int batchAdded = 0;
                foreach (var msgBase in searchResult.Messages.OfType<Message>())
                {
                    fetchedCount++;

                    // Skip messages we already have (incremental update)
                    if (lastId > 0 && msgBase.id <= lastId)
                    {
                        _logger.LogInformation("   ⏭️ Reached lastId ({LastId}), stopping. Fetched: {Fetched}, Added: {Added}",
                            lastId, fetchedCount, addedCount);
                        return results.Count - initialResultsCount;
                    }

                    // Skip if already added (from another filter)
                    if (existingIds.Contains(msgBase.id)) continue;

                    if (msgBase.media is MessageMediaDocument mediaDoc &&
                        mediaDoc.document is TL.Document doc)
                    {
                        string filename = doc.Filename;
                        // If no filename, create one from audio attributes
                        if (string.IsNullOrEmpty(filename))
                        {
                            var audioAttr = doc.attributes.OfType<TL.DocumentAttributeAudio>().FirstOrDefault();
                            if (audioAttr != null)
                            {
                                filename = !string.IsNullOrEmpty(audioAttr.title)
                                    ? $"{audioAttr.performer} - {audioAttr.title}.mp3"
                                    : $"audio_{doc.id}.mp3";
                            }
                            else
                            {
                                filename = $"audio_{doc.id}.mp3";
                            }
                        }

                        results.Add(new TelegramChatDocuments
                        {
                            id = msgBase.id,
                            documentType = DocumentType.Document,
                            name = filename,
                            fileSize = doc.size,
                            extension = Path.GetExtension(filename),
                            creationDate = msgBase.date,
                            modifiedDate = msgBase.edit_date < msgBase.date ? msgBase.date : msgBase.edit_date
                        });
                        existingIds.Add(msgBase.id);
                        addedCount++;
                        batchAdded++;
                    }
                }

                double progress = totalCount > 0 ? (double)fetchedCount / totalCount * 100 : 0;
                _logger.LogInformation("   🎵 Batch {Batch}: Fetched {Fetched}/{Total} ({Progress:F1}%) - Added {BatchAdded} audio (Total added: {Added})",
                    batchNumber, fetchedCount, totalCount, progress, batchAdded, addedCount);

                offset_id = searchResult.Messages[^1].ID;

                // Small delay to avoid rate limiting
                await Task.Delay(100);
            }

            return results.Count - initialResultsCount;
        }

        private async Task<int> SearchVideo(InputPeer peer, int lastId, List<TelegramChatDocuments> results)
        {
            int totalCount = 0;
            int fetchedCount = 0;
            int addedCount = 0;
            int batchNumber = 0;
            int initialResultsCount = results.Count;
            HashSet<int> existingIds = results.Select(r => r.id).ToHashSet();

            for (int offset_id = 0; ;)
            {
                batchNumber++;
                var searchResult = await client.Messages_Search<InputMessagesFilterVideo>(
                    peer: peer,
                    q: "",
                    offset_id: offset_id,
                    limit: 100);

                if (searchResult.Messages.Length == 0) break;

                // Get total count from first batch
                if (totalCount == 0)
                {
                    totalCount = searchResult.Count;
                    _logger.LogInformation("   📊 Total video files in channel: {Total}", totalCount);
                }

                int batchAdded = 0;
                foreach (var msgBase in searchResult.Messages.OfType<Message>())
                {
                    fetchedCount++;

                    // Skip messages we already have (incremental update)
                    if (lastId > 0 && msgBase.id <= lastId)
                    {
                        _logger.LogInformation("   ⏭️ Reached lastId ({LastId}), stopping. Fetched: {Fetched}, Added: {Added}",
                            lastId, fetchedCount, addedCount);
                        return results.Count - initialResultsCount;
                    }

                    // Skip if already added (from another filter)
                    if (existingIds.Contains(msgBase.id)) continue;

                    if (msgBase.media is MessageMediaDocument mediaDoc &&
                        mediaDoc.document is TL.Document doc)
                    {
                        string filename = doc.Filename;
                        // If no filename, create one
                        if (string.IsNullOrEmpty(filename))
                        {
                            filename = $"video_{doc.id}.mp4";
                        }

                        results.Add(new TelegramChatDocuments
                        {
                            id = msgBase.id,
                            documentType = DocumentType.Document,
                            name = filename,
                            fileSize = doc.size,
                            extension = Path.GetExtension(filename),
                            creationDate = msgBase.date,
                            modifiedDate = msgBase.edit_date < msgBase.date ? msgBase.date : msgBase.edit_date
                        });
                        existingIds.Add(msgBase.id);
                        addedCount++;
                        batchAdded++;
                    }
                }

                double progress = totalCount > 0 ? (double)fetchedCount / totalCount * 100 : 0;
                _logger.LogInformation("   🎬 Batch {Batch}: Fetched {Fetched}/{Total} ({Progress:F1}%) - Added {BatchAdded} video (Total added: {Added})",
                    batchNumber, fetchedCount, totalCount, progress, batchAdded, addedCount);

                offset_id = searchResult.Messages[^1].ID;

                // Small delay to avoid rate limiting
                await Task.Delay(100);
            }

            return results.Count - initialResultsCount;
        }

        private async Task<int> SearchPhotos(InputPeer peer, int lastId, List<TelegramChatDocuments> results)
        {
            int totalCount = 0;
            int fetchedCount = 0;
            int addedCount = 0;
            int batchNumber = 0;
            int initialResultsCount = results.Count;
            HashSet<int> existingIds = results.Select(r => r.id).ToHashSet();

            for (int offset_id = 0; ;)
            {
                batchNumber++;
                var searchResult = await client.Messages_Search<InputMessagesFilterPhotos>(
                    peer: peer,
                    q: "",
                    offset_id: offset_id,
                    limit: 100);

                if (searchResult.Messages.Length == 0) break;

                // Get total count from first batch
                if (totalCount == 0)
                {
                    totalCount = searchResult.Count;
                    _logger.LogInformation("   📊 Total photos in channel: {Total}", totalCount);
                }

                int batchAdded = 0;
                foreach (var msgBase in searchResult.Messages.OfType<Message>())
                {
                    fetchedCount++;

                    // Skip messages we already have (incremental update)
                    if (lastId > 0 && msgBase.id <= lastId)
                    {
                        _logger.LogInformation("   ⏭️ Reached lastId ({LastId}), stopping. Fetched: {Fetched}, Added: {Added}",
                            lastId, fetchedCount, addedCount);
                        return results.Count - initialResultsCount;
                    }

                    // Skip if already added
                    if (existingIds.Contains(msgBase.id)) continue;

                    if (msgBase.media is MessageMediaPhoto mediaPhoto &&
                        mediaPhoto.photo is Photo photo)
                    {
                        results.Add(new TelegramChatDocuments
                        {
                            id = msgBase.id,
                            documentType = DocumentType.Photo,
                            name = $"{photo.id}.jpg",
                            fileSize = photo.LargestPhotoSize?.FileSize ?? 0,
                            extension = ".jpg",
                            creationDate = msgBase.date,
                            modifiedDate = msgBase.edit_date < msgBase.date ? msgBase.date : msgBase.edit_date
                        });
                        existingIds.Add(msgBase.id);
                        addedCount++;
                        batchAdded++;
                    }
                }

                double progress = totalCount > 0 ? (double)fetchedCount / totalCount * 100 : 0;
                _logger.LogInformation("   📷 Batch {Batch}: Fetched {Fetched}/{Total} ({Progress:F1}%) - Added {BatchAdded} photos (Total added: {Added})",
                    batchNumber, fetchedCount, totalCount, progress, batchAdded, addedCount);

                offset_id = searchResult.Messages[^1].ID;

                // Small delay to avoid rate limiting
                await Task.Delay(100);
            }

            return results.Count - initialResultsCount;
        }

        public async Task<TL.Channel?> CreateChannel(string title, string about)
        {
            try
            {
                _logger.LogInformation("Creating channel with title: {Title}", title);

                // Create a new channel (broadcast = true for channel, false for megagroup/supergroup)
                var updates = await client.Channels_CreateChannel(
                    title: title,
                    about: about,
                    broadcast: true,  // true = channel, false = megagroup
                    megagroup: false,
                    for_import: false
                );

                // Extract the created channel from the updates
                if (updates is Updates updatesObj)
                {
                    var channel = updatesObj.chats.Values.OfType<TL.Channel>().FirstOrDefault();
                    if (channel != null)
                    {
                        _logger.LogInformation("Channel created successfully: {ChannelId} - {Title}", channel.ID, channel.Title);

                        // Refresh the chat list to include the new channel
                        chats = null;
                        await getAllChats();

                        return channel;
                    }
                }

                _logger.LogWarning("Channel creation returned unexpected result type");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating channel: {Title}", title);
                throw;
            }
        }
    }
}
