using MongoDB.Driver;
using Newtonsoft.Json;
using Serilog;
using Serilog.Events;
using TelegramDownloader.Models;

namespace TelegramDownloader.Services
{
    public interface ISetupService
    {
        Task<SetupStatus> GetSetupStatusAsync();
        SetupStatus GetCachedStatus();
        Task<(bool Success, string? Error)> TestMongoConnection(string connectionString);
        Task<(bool Success, string? Error)> TestTelegramCredentials(string apiId, string apiHash);
        Task SaveConfiguration(string? mongoConnectionString, string? apiId, string? apiHash);
        void ReconfigureSerilogWithMongo(string connectionString);
        void MarkSetupComplete();
        bool IsSetupComplete { get; }
    }

    public enum SetupStep
    {
        Complete,
        MongoDbRequired,
        TelegramRequired
    }

    public class SetupStatus
    {
        public SetupStep CurrentStep { get; set; }
        public bool MongoDbConfigured { get; set; }
        public bool MongoDbConnected { get; set; }
        public bool TelegramConfigured { get; set; }
        public string? MongoDbError { get; set; }
        public string? TelegramError { get; set; }
    }

    public class SetupService : ISetupService
    {
        private readonly ILogger<SetupService> _logger;
        private static SetupStatus _cachedStatus = new SetupStatus();
        private static bool _isSetupComplete = false;
        private static bool _hasBeenChecked = false;

        public bool IsSetupComplete => _isSetupComplete;

        public SetupService(ILogger<SetupService> logger)
        {
            _logger = logger;
        }

        public SetupStatus GetCachedStatus()
        {
            // If we've already completed setup, return cached status
            if (_isSetupComplete)
            {
                return _cachedStatus;
            }

            // Quick sync check without MongoDB connection test
            var status = new SetupStatus();

            var mongoConnectionString = GeneralConfigStatic.tlconfig?.mongo_connection_string
                ?? Environment.GetEnvironmentVariable("connectionString");
            var apiId = GeneralConfigStatic.tlconfig?.api_id
                ?? Environment.GetEnvironmentVariable("api_id");
            var apiHash = GeneralConfigStatic.tlconfig?.hash_id
                ?? Environment.GetEnvironmentVariable("hash_id");

            status.MongoDbConfigured = !string.IsNullOrWhiteSpace(mongoConnectionString);
            status.TelegramConfigured = !string.IsNullOrWhiteSpace(apiId) && !string.IsNullOrWhiteSpace(apiHash);

            // If previously checked and both configured, assume connected
            if (_hasBeenChecked && status.MongoDbConfigured && status.TelegramConfigured)
            {
                status.MongoDbConnected = true;
                status.CurrentStep = SetupStep.Complete;
                return status;
            }

            // Determine step based on configuration only (no connection test)
            if (!status.MongoDbConfigured)
            {
                status.CurrentStep = SetupStep.MongoDbRequired;
            }
            else if (!status.TelegramConfigured)
            {
                status.CurrentStep = SetupStep.TelegramRequired;
            }
            else
            {
                status.CurrentStep = SetupStep.Complete;
            }

            return status;
        }

        public async Task<SetupStatus> GetSetupStatusAsync()
        {
            // Return cached status if setup is already complete
            if (_isSetupComplete)
            {
                return _cachedStatus;
            }

            var status = new SetupStatus();

            // Check MongoDB configuration
            var mongoConnectionString = GeneralConfigStatic.tlconfig?.mongo_connection_string
                ?? Environment.GetEnvironmentVariable("connectionString");

            status.MongoDbConfigured = !string.IsNullOrWhiteSpace(mongoConnectionString);

            if (status.MongoDbConfigured)
            {
                // Test MongoDB connection asynchronously
                var testResult = await TestMongoConnection(mongoConnectionString!);
                status.MongoDbConnected = testResult.Success;
                status.MongoDbError = testResult.Error;
            }

            // Check Telegram configuration
            var apiId = GeneralConfigStatic.tlconfig?.api_id
                ?? Environment.GetEnvironmentVariable("api_id");
            var apiHash = GeneralConfigStatic.tlconfig?.hash_id
                ?? Environment.GetEnvironmentVariable("hash_id");

            status.TelegramConfigured = !string.IsNullOrWhiteSpace(apiId) && !string.IsNullOrWhiteSpace(apiHash);

            // Determine current step
            if (!status.MongoDbConfigured || !status.MongoDbConnected)
            {
                status.CurrentStep = SetupStep.MongoDbRequired;
            }
            else if (!status.TelegramConfigured)
            {
                status.CurrentStep = SetupStep.TelegramRequired;
            }
            else
            {
                status.CurrentStep = SetupStep.Complete;
                _isSetupComplete = true;
            }

            _cachedStatus = status;
            _hasBeenChecked = true;
            return status;
        }

        public void MarkSetupComplete()
        {
            _isSetupComplete = true;
            _hasBeenChecked = true;
            _cachedStatus = new SetupStatus
            {
                CurrentStep = SetupStep.Complete,
                MongoDbConfigured = true,
                MongoDbConnected = true,
                TelegramConfigured = true
            };
        }

        public async Task<(bool Success, string? Error)> TestMongoConnection(string connectionString)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    return (false, "Connection string is empty");
                }

                var settings = MongoClientSettings.FromConnectionString(connectionString);
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
                settings.ConnectTimeout = TimeSpan.FromSeconds(5);

                var client = new MongoClient(settings);
                var database = client.GetDatabase("admin");

                // Try to run a simple command to verify connection
                var command = new MongoDB.Bson.BsonDocument("ping", 1);
                await database.RunCommandAsync<MongoDB.Bson.BsonDocument>(command);

                _logger.LogInformation("MongoDB connection test successful");
                return (true, null);
            }
            catch (MongoConfigurationException ex)
            {
                _logger.LogWarning(ex, "Invalid MongoDB connection string");
                return (false, $"Invalid connection string format: {ex.Message}");
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "MongoDB connection timeout");
                return (false, "Connection timeout. Please verify the server is running and accessible.");
            }
            catch (MongoAuthenticationException ex)
            {
                _logger.LogWarning(ex, "MongoDB authentication failed");
                return (false, $"Authentication failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MongoDB connection test failed");
                return (false, $"Connection failed: {ex.Message}");
            }
        }

        public async Task<(bool Success, string? Error)> TestTelegramCredentials(string apiId, string apiHash)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(apiId))
                {
                    return (false, "API ID is required");
                }

                if (string.IsNullOrWhiteSpace(apiHash))
                {
                    return (false, "API Hash is required");
                }

                if (!int.TryParse(apiId, out int parsedApiId) || parsedApiId <= 0)
                {
                    return (false, "API ID must be a valid positive number");
                }

                if (apiHash.Length < 20)
                {
                    return (false, "API Hash appears to be too short. Please check your credentials.");
                }

                // We can't fully validate Telegram credentials without attempting to connect,
                // but we can do basic format validation
                _logger.LogInformation("Telegram credentials format validation successful");
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telegram credentials validation failed");
                return (false, $"Validation failed: {ex.Message}");
            }
        }

        public async Task SaveConfiguration(string? mongoConnectionString, string? apiId, string? apiHash)
        {
            var configPath = "./Configuration/config.json";
            TLConfig config;

            // Load existing config or create new
            if (File.Exists(configPath))
            {
                var json = await File.ReadAllTextAsync(configPath);
                config = JsonConvert.DeserializeObject<TLConfig>(json) ?? new TLConfig();
            }
            else
            {
                config = new TLConfig();
                // Ensure directory exists
                Directory.CreateDirectory("./Configuration");
            }

            // Update values if provided
            if (!string.IsNullOrWhiteSpace(mongoConnectionString))
            {
                config.mongo_connection_string = mongoConnectionString;
            }

            if (!string.IsNullOrWhiteSpace(apiId))
            {
                config.api_id = apiId;
            }

            if (!string.IsNullOrWhiteSpace(apiHash))
            {
                config.hash_id = apiHash;
            }

            // Save to file
            var updatedJson = JsonConvert.SerializeObject(config, Formatting.Indented);
            await File.WriteAllTextAsync(configPath, updatedJson);

            // Update static config
            GeneralConfigStatic.tlconfig = config;

            _logger.LogInformation("Configuration saved successfully");
        }

        public void ReconfigureSerilogWithMongo(string connectionString)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    _logger.LogWarning("Cannot reconfigure Serilog: connection string is empty");
                    return;
                }

                _logger.LogInformation("Reconfiguring Serilog to use MongoDB...");

                // Create new logger configuration with MongoDB sink
                var newLogger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                    .Enrich.FromLogContext()
                    .WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}")
                    .WriteTo.MongoDB(
                        databaseUrl: connectionString,
                        collectionName: "logs")
                    .CreateLogger();

                // Replace the global logger
                var oldLogger = Log.Logger;
                Log.Logger = newLogger;

                // Dispose old logger
                if (oldLogger is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                _logger.LogInformation("Serilog reconfigured successfully to use MongoDB");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reconfigure Serilog with MongoDB: {Message}", ex.Message);
            }
        }
    }
}
