using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// Write side of the first-run wizard (the read side is
    /// <c>GET /api/v1/system/setup</c>). Works before MongoDB is configured.
    ///
    /// These endpoints only work while setup is incomplete: once it is
    /// complete every one of them answers <c>409 conflict</c>, so they cannot
    /// be used to repoint a configured server.
    /// </summary>
    [Route("api/v1/system/setup")]
    [Tags("System")]
    public class SetupController : ApiV1ControllerBase
    {
        private readonly ISetupService _setup;
        private readonly IDbService _db;
        private readonly ILogQueryService _logs;
        private readonly ITelegramService _telegram;
        private readonly ILogger<SetupController> _logger;

        public SetupController(
            ISetupService setup,
            IDbService db,
            ILogQueryService logs,
            ITelegramService telegram,
            ILogger<SetupController> logger)
        {
            _setup = setup;
            _db = db;
            _logs = logs;
            _telegram = telegram;
            _logger = logger;
        }

        /// <summary>Checks that a MongoDB connection string works, without saving it.</summary>
        [HttpPost("test-mongo")]
        [ProducesResponseType(typeof(ApiResult<SetupTestResultDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> TestMongo([FromBody] TestMongoRequest request)
        {
            if (await SetupAlreadyComplete() is IActionResult done) return done;
            if (string.IsNullOrWhiteSpace(request?.ConnectionString))
                return BadRequestResult("connectionString is required");

            var (ok, error) = await _setup.TestMongoConnection(request.ConnectionString.Trim());
            return OkResult(new SetupTestResultDto { Success = ok, Error = error });
        }

        /// <summary>Validates the format of the Telegram API id and hash, without saving them.</summary>
        /// <remarks>Telegram itself is only contacted when a user signs in.</remarks>
        [HttpPost("test-telegram")]
        [ProducesResponseType(typeof(ApiResult<SetupTestResultDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> TestTelegram([FromBody] TestTelegramRequest request)
        {
            if (await SetupAlreadyComplete() is IActionResult done) return done;
            var (ok, error) = await _setup.TestTelegramCredentials(request?.ApiId?.Trim() ?? string.Empty, request?.ApiHash?.Trim() ?? string.Empty);
            return OkResult(new SetupTestResultDto { Success = ok, Error = error });
        }

        /// <summary>Saves the wizard configuration (MongoDB and/or Telegram).</summary>
        /// <remarks>
        /// Does what the web wizard does, step by step: each part present in
        /// the body is validated first and then written to
        /// <c>Configuration/config.json</c>. Saving MongoDB also reconnects the
        /// database, the log store and the logger and loads the stored
        /// settings; saving Telegram initialises the Telegram client. Either
        /// part can be sent alone. The response is the new setup status.
        /// </remarks>
        [HttpPost]
        [ProducesResponseType(typeof(ApiResult<SetupStatusDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Save([FromBody] SaveSetupRequest request)
        {
            if (await SetupAlreadyComplete() is IActionResult done) return done;
            if (request == null)
                return BadRequestResult("A setup body is required");

            var hasMongo = !string.IsNullOrWhiteSpace(request.MongoConnectionString);
            var hasTelegram = !string.IsNullOrWhiteSpace(request.ApiId) || !string.IsNullOrWhiteSpace(request.ApiHash);
            if (!hasMongo && !hasTelegram)
                return BadRequestResult("Provide mongoConnectionString and/or apiId + apiHash");

            try
            {
                if (hasMongo)
                {
                    var connection = request.MongoConnectionString!.Trim();
                    var (ok, error) = await _setup.TestMongoConnection(connection);
                    if (!ok)
                        return BadRequestResult("The MongoDB connection failed", detail: error);

                    await _setup.SaveConfiguration(connection, null, null);
                    try
                    {
                        _db.ReinitializeConnection(connection);
                        _logs.ReinitializeConnection(connection);
                        await GeneralConfigStatic.Load(_db);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to reinitialise the database connection after setup");
                        return ErrorResult("The connection works but the services could not be initialised", ex);
                    }
                    _setup.ReconfigureSerilogWithMongo(connection);
                }

                if (hasTelegram)
                {
                    var apiId = request.ApiId?.Trim() ?? string.Empty;
                    var apiHash = request.ApiHash?.Trim() ?? string.Empty;
                    var (ok, error) = await _setup.TestTelegramCredentials(apiId, apiHash);
                    if (!ok)
                        return BadRequestResult("The Telegram credentials are not valid", detail: error);

                    await _setup.SaveConfiguration(null, apiId, apiHash);
                    try
                    {
                        _telegram.InitializeClient();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to initialise the Telegram client after setup");
                        return ErrorResult("The credentials were saved but the Telegram client could not be initialised", ex);
                    }
                }

                return OkResult(await ReadStatus(), "Setup saved");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving the setup");
                return ErrorResult("Could not save the setup", ex);
            }
        }

        /// <summary>Marks the wizard as finished.</summary>
        /// <remarks>
        /// Needs a reachable MongoDB and Telegram credentials (otherwise
        /// <c>400 setup_required</c>). Optional: the server also considers the
        /// setup complete as soon as a status read (including the one at the
        /// end of <c>POST /api/v1/system/setup</c>) finds everything
        /// configured, after which this endpoint answers <c>409</c> like the
        /// others. Clients can treat that <c>409</c> as success.
        /// </remarks>
        [HttpPost("complete")]
        [ProducesResponseType(typeof(ApiResult<SetupStatusDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Complete()
        {
            if (_setup.IsSetupComplete)
                return ConflictResult("Setup is already complete");

            var status = await _setup.GetSetupStatusAsync();
            if (!status.MongoDbConnected || !status.TelegramConfigured)
                return BadRequestResult("MongoDB and Telegram must be configured first", ApiErrorCodes.SetupRequired);

            _setup.MarkSetupComplete();
            return OkResult(ToDto(await _setup.GetSetupStatusAsync()), "Setup complete");
        }

        /// <summary>
        /// 409 once the setup is complete. The completion flag is only set after
        /// a status read, so a configured server that has not been asked yet is
        /// checked here (one MongoDB ping) instead of being treated as new.
        /// </summary>
        private async Task<IActionResult?> SetupAlreadyComplete()
        {
            if (_setup.IsSetupComplete)
                return ConflictResult("Setup is already complete");
            var status = await _setup.GetSetupStatusAsync();
            return status.CurrentStep == SetupStep.Complete
                ? ConflictResult("Setup is already complete")
                : null;
        }

        private async Task<SetupStatusDto> ReadStatus() => ToDto(await _setup.GetSetupStatusAsync());

        private static SetupStatusDto ToDto(SetupStatus status) => new SetupStatusDto
        {
            CurrentStep = status.CurrentStep.ToString(),
            MongoDbConfigured = status.MongoDbConfigured,
            MongoDbConnected = status.MongoDbConnected,
            TelegramConfigured = status.TelegramConfigured,
            MongoDbError = status.MongoDbError
        };
    }

    /// <summary>Body of <c>POST /api/v1/system/setup/test-mongo</c>.</summary>
    public class TestMongoRequest
    {
        public string ConnectionString { get; set; } = string.Empty;
    }

    /// <summary>Body of <c>POST /api/v1/system/setup/test-telegram</c>.</summary>
    public class TestTelegramRequest
    {
        public string? ApiId { get; set; }
        public string? ApiHash { get; set; }
    }

    /// <summary>Body of <c>POST /api/v1/system/setup</c>. Send either part or both.</summary>
    public class SaveSetupRequest
    {
        public string? MongoConnectionString { get; set; }
        public string? ApiId { get; set; }
        public string? ApiHash { get; set; }
    }

    /// <summary>Outcome of a setup test.</summary>
    public class SetupTestResultDto
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
    }
}
