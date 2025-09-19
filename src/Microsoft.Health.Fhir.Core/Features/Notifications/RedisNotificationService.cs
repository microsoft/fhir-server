// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using EnsureThat;
using Microsoft.Azure.StackExchangeRedis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using StackExchange.Redis;

namespace Microsoft.Health.Fhir.Core.Features.Notifications
{
    /// <summary>
    /// Redis-based notification service using Microsoft Entra ID authentication.
    ///
    /// Prerequisites:
    /// - Azure Cache for Redis has Microsoft Entra authentication enabled
    /// - The managed identity is added as a Redis user (Data Contributor/Owner)
    /// - Connect over TLS port 6380
    /// - Entra auth is supported on Basic/Standard/Premium tiers (not Enterprise)
    /// </summary>
    public class RedisNotificationService : INotificationService, IDisposable
    {
        private readonly RedisConfiguration _configuration;
        private readonly ILogger<RedisNotificationService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;
        private ConnectionMultiplexer _connection;
        private ISubscriber _subscriber;
        private bool _disposed;
        private bool _initialized;

        public RedisNotificationService(
            IOptions<RedisConfiguration> configuration,
            ILogger<RedisNotificationService> logger)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _configuration = configuration.Value;
            _logger = logger;

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
            };

            InitializeAsync().GetAwaiter().GetResult();
        }

        private async Task InitializeAsync()
        {
            if (!_configuration.Enabled)
            {
                _logger.LogInformation("Redis notifications are disabled.");
                _initialized = true;
                return;
            }

            // Validate configuration before attempting connection
            try
            {
                _configuration.Validate();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis configuration validation failed: {Message}", ex.Message);
                throw;
            }

            _logger.LogInformation(
                "Initializing Redis connection with Microsoft Entra ID authentication to {Host}:{Port}",
                _configuration.Host,
                _configuration.Port);

            try
            {
                // Create configuration options using Microsoft Entra integration
                var configurationOptions = await CreateManagedIdentityConfigurationAsync();

                // Apply common configuration settings
                configurationOptions.AbortOnConnectFail = _configuration.Configuration.AbortOnConnectFail;
                configurationOptions.ConnectRetry = _configuration.Configuration.ConnectRetry;
                configurationOptions.ConnectTimeout = _configuration.Configuration.ConnectTimeout;
                configurationOptions.SyncTimeout = _configuration.Configuration.SyncTimeout;
                configurationOptions.AsyncTimeout = _configuration.Configuration.AsyncTimeout;

                // Connect using Microsoft Entra-enabled configuration
                _connection = await ConnectionMultiplexer.ConnectAsync(configurationOptions);

                // Verify connection is working by testing it with a real command
                await VerifyConnectionAsync();

                _subscriber = _connection.GetSubscriber();
                _initialized = true;

                _logger.LogInformation(
                    "Redis notification service initialized successfully to {Host}:{Port} with Microsoft Entra ID (Connected: {IsConnected})",
                    _configuration.Host,
                    _configuration.Port,
                    _connection.IsConnected);
            }
            catch (RedisConnectionException ex) when (IsAuthenticationError(ex))
            {
                _logger.LogError(
                    ex,
                    "Redis authentication failed. Ensure Microsoft Entra ID authentication is enabled on Redis Cache and Managed Identity has proper permissions. Host: {Host}:{Port}",
                    _configuration.Host,
                    _configuration.Port);
                throw new InvalidOperationException($"Redis authentication failed for {_configuration.Host}:{_configuration.Port}. Check Microsoft Entra ID configuration and permissions.", ex);
            }
            catch (RedisConnectionException ex)
            {
                _logger.LogError(
                    ex,
                    "Redis connection failed to {Host}:{Port}. Error: {Message}",
                    _configuration.Host,
                    _configuration.Port,
                    ex.Message);
                throw new InvalidOperationException($"Unable to connect to Redis at {_configuration.Host}:{_configuration.Port}. Check network connectivity and configuration.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to initialize Redis notification service to {Host}:{Port}. Error: {Error}",
                    _configuration.Host,
                    _configuration.Port,
                    ex.Message);
                throw;
            }
        }

        private static bool IsAuthenticationError(RedisConnectionException ex)
        {
            // Check for common authentication-related error messages
            var message = ex.Message ?? string.Empty;
            return message.Contains("NOAUTH", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates Redis configuration using Microsoft Azure StackExchangeRedis integration.
        /// This handles token acquisition, refresh, and authentication automatically.
        /// </summary>
        private async Task<ConfigurationOptions> CreateManagedIdentityConfigurationAsync()
        {
            var options = new ConfigurationOptions
            {
                AbortOnConnectFail = false,
                ClientName = "FHIRServer-ManagedIdentity",
                Ssl = _configuration.UseSsl,
                DefaultDatabase = 0,
            };

            var port = _configuration.Port == 0 ? 6380 : _configuration.Port;
            options.EndPoints.Add(_configuration.Host, port);

            // Managed Identity / Workload Identity credential
            var credOptions = new DefaultAzureCredentialOptions
            {
                ExcludeVisualStudioCredential = true,
                ExcludeVisualStudioCodeCredential = true,
                ExcludeInteractiveBrowserCredential = true,
                ManagedIdentityClientId = string.IsNullOrEmpty(_configuration.ManagedIdentityClientId) ? null : _configuration.ManagedIdentityClientId, // null for system-assigned
            };
            var credential = new DefaultAzureCredential(credOptions);

            // Wire Microsoft Entra auth into StackExchange.Redis and enable automatic token refresh
            await options.ConfigureForAzureWithTokenCredentialAsync(credential);

            _logger.LogInformation("Configured Redis for Microsoft Entra ID auth (Managed Identity). Host={Host} Port={Port}", _configuration.Host, port);
            return options;
        }

        private async Task VerifyConnectionAsync()
        {
            if (_connection == null || !_connection.IsConnected)
            {
                throw new InvalidOperationException("Redis connection was not established properly");
            }

            // Test the connection with a simple PING command to verify authentication
            var database = _connection.GetDatabase();
            try
            {
                _logger.LogDebug("Verifying Redis connection with PING command");
                var pingResult = await database.PingAsync();
                _logger.LogInformation("Redis PING successful: {PingTime}ms - Microsoft Entra ID authentication verified", pingResult.TotalMilliseconds);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("NOAUTH", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(ex, "Redis authentication failed - NOAUTH error during PING. This indicates Microsoft Entra ID is not configured properly on Redis Cache");

                // Convert server-side NOAUTH error to connection exception
                throw new RedisConnectionException(ConnectionFailureType.AuthenticationFailure, "Redis authentication required - NOAUTH error received. Check Microsoft Entra ID configuration on Redis Cache.", ex);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("WRONGPASS", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(ex, "Redis authentication failed - WRONGPASS error. This indicates Microsoft Entra ID token authentication is not configured properly");
                throw new RedisConnectionException(ConnectionFailureType.AuthenticationFailure, "Redis authentication failed - wrong password. Ensure Microsoft Entra ID authentication is enabled on Redis Cache.", ex);
            }
            catch (RedisServerException ex)
            {
                _logger.LogError(ex, "Redis server error during connection verification: {Message}", ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during Redis connection verification: {Message}", ex.Message);
                throw;
            }
        }

        public async Task PublishAsync<T>(string channel, T message, CancellationToken cancellationToken = default)
            where T : class
        {
            EnsureArg.IsNotNullOrWhiteSpace(channel, nameof(channel));
            EnsureArg.IsNotNull(message, nameof(message));

            if (!_configuration.Enabled || !_initialized || _subscriber == null)
            {
                _logger.LogDebug("Redis notifications are disabled or not initialized. Skipping publish to channel: {Channel}", channel);
                return;
            }

            try
            {
                var serializedMessage = JsonSerializer.Serialize(message, _jsonOptions);
                await _subscriber.PublishAsync(RedisChannel.Literal(channel), serializedMessage);

                _logger.LogDebug("Published notification to channel: {Channel}", channel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish notification to channel: {Channel}", channel);
                throw;
            }
        }

        public async Task SubscribeAsync<T>(string channel, NotificationHandler<T> handler, CancellationToken cancellationToken = default)
            where T : class
        {
            EnsureArg.IsNotNullOrWhiteSpace(channel, nameof(channel));
            EnsureArg.IsNotNull(handler, nameof(handler));

            if (!_configuration.Enabled || !_initialized || _subscriber == null)
            {
                _logger.LogDebug("Redis notifications are disabled or not initialized. Skipping subscribe to channel: {Channel}", channel);
                return;
            }

            try
            {
                await _subscriber.SubscribeAsync(RedisChannel.Literal(channel), async (redisChannel, message) =>
                {
                    try
                    {
                        var deserializedMessage = JsonSerializer.Deserialize<T>(message, _jsonOptions);
                        if (deserializedMessage != null)
                        {
                            await handler(deserializedMessage, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error handling notification from channel: {Channel}", channel);
                    }
                });

                _logger.LogInformation("Subscribed to notifications on channel: {Channel}", channel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to subscribe to channel: {Channel}", channel);
                throw;
            }
        }

        public async Task UnsubscribeAsync(string channel, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNullOrWhiteSpace(channel, nameof(channel));

            if (!_configuration.Enabled || !_initialized || _subscriber == null)
            {
                return;
            }

            try
            {
                await _subscriber.UnsubscribeAsync(RedisChannel.Literal(channel));
                _logger.LogInformation("Unsubscribed from notifications on channel: {Channel}", channel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unsubscribe from channel: {Channel}", channel);
                throw;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _connection?.Dispose();
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
