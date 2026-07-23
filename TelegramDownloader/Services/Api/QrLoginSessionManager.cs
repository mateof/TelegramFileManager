using System.Collections.Concurrent;
using QRCoder;
using TelegramDownloader.Data;
using TelegramDownloader.Models.Api;

namespace TelegramDownloader.Services.Api
{
    /// <summary>
    /// Keeps the state of QR login attempts started through the REST API.
    ///
    /// The Telegram QR flow is long-lived and callback based: the library hands
    /// out a fresh <c>tg://login</c> URL every ~30s and, if the account has
    /// two-factor authentication, asks for the password after the phone accepts
    /// the code. A mobile client cannot hold that callback, so a session is kept
    /// server-side and polled through
    /// <c>GET /api/v1/auth/qr/{sessionId}</c>.
    /// </summary>
    public class QrLoginSessionManager : IDisposable
    {
        /// <summary>Sessions with no polling for this long are discarded.</summary>
        public static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(10);

        private readonly ConcurrentDictionary<string, QrSession> _sessions = new();
        private readonly ILogger<QrLoginSessionManager> _logger;

        public QrLoginSessionManager(ILogger<QrLoginSessionManager> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Starts a QR login in the background and returns the session as soon as
        /// the first QR URL is available (or the timeout elapses).
        /// </summary>
        public async Task<QrLoginDto> StartAsync(ITelegramService telegram, bool logoutFirst = false)
        {
            PruneExpired();

            var session = new QrSession();
            _sessions[session.Id] = session;

            void OnPasswordNeeded(object? sender, EventArgs e)
            {
                session.Status = "password_required";
                session.LoginUrl = null;
                session.QrImageBase64 = null;
            }

            TelegramService.QrPasswordNeeded += OnPasswordNeeded;

            session.Worker = Task.Run(async () =>
            {
                try
                {
                    var user = await telegram.CallQrGenerator(
                        url =>
                        {
                            session.LoginUrl = url;
                            session.QrImageBase64 = RenderQr(url);
                            session.Touch();
                        },
                        session.Cancellation.Token,
                        logoutFirst);

                    session.Status = user != null ? "authenticated" : "error";
                    if (user == null)
                        session.Error = "Telegram did not return a user";
                }
                catch (OperationCanceledException)
                {
                    session.Status = "cancelled";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "QR login session {SessionId} failed", session.Id);
                    session.Status = "error";
                    session.Error = ex.Message;
                }
                finally
                {
                    TelegramService.QrPasswordNeeded -= OnPasswordNeeded;
                }
            });

            // Give the library a moment to emit the first URL so the very first
            // response already carries a QR the client can render.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (session.LoginUrl == null && session.Status == "waiting" && DateTime.UtcNow < deadline)
                await Task.Delay(100);

            return session.ToDto();
        }

        /// <summary>Returns the current state of a session, or null when unknown.</summary>
        public QrLoginDto? Get(string sessionId)
        {
            PruneExpired();
            if (!_sessions.TryGetValue(sessionId, out var session)) return null;
            session.Touch();
            return session.ToDto();
        }

        /// <summary>
        /// Supplies the two-factor password a session is waiting for. Returns
        /// false when the session does not exist.
        /// </summary>
        public bool ProvidePassword(string sessionId, ITelegramService telegram, string password)
        {
            if (!_sessions.TryGetValue(sessionId, out var session)) return false;
            session.Touch();
            telegram.ProvideQrLoginPassword(password);
            session.Status = "waiting";
            return true;
        }

        /// <summary>Cancels a pending session. Returns false when unknown.</summary>
        public bool Cancel(string sessionId)
        {
            if (!_sessions.TryRemove(sessionId, out var session)) return false;
            session.Cancel();
            return true;
        }

        private void PruneExpired()
        {
            var cutoff = DateTime.UtcNow - SessionLifetime;
            foreach (var kvp in _sessions)
            {
                if (kvp.Value.LastSeenUtc < cutoff)
                {
                    if (_sessions.TryRemove(kvp.Key, out var stale))
                        stale.Cancel();
                }
            }
        }

        private static string RenderQr(string data)
        {
            using var generator = new QRCodeGenerator();
            using var qrData = generator.CreateQrCode(data, QRCodeGenerator.ECCLevel.Q);
            using var png = new PngByteQRCode(qrData);
            return Convert.ToBase64String(png.GetGraphic(20));
        }

        public void Dispose()
        {
            foreach (var session in _sessions.Values)
                session.Cancel();
            _sessions.Clear();
            GC.SuppressFinalize(this);
        }

        private class QrSession
        {
            public string Id { get; } = Guid.NewGuid().ToString("N");
            public CancellationTokenSource Cancellation { get; } = new();
            public Task? Worker { get; set; }
            public string Status { get; set; } = "waiting";
            public string? LoginUrl { get; set; }
            public string? QrImageBase64 { get; set; }
            public string? Error { get; set; }
            public DateTime LastSeenUtc { get; private set; } = DateTime.UtcNow;

            public void Touch() => LastSeenUtc = DateTime.UtcNow;

            public void Cancel()
            {
                try
                {
                    if (!Cancellation.IsCancellationRequested)
                        Cancellation.Cancel();
                }
                catch (ObjectDisposedException) { }
                Status = Status == "authenticated" ? Status : "cancelled";
            }

            public QrLoginDto ToDto() => new()
            {
                SessionId = Id,
                LoginUrl = LoginUrl,
                QrImageBase64 = QrImageBase64,
                Status = Status,
                Error = Error
            };
        }
    }
}
