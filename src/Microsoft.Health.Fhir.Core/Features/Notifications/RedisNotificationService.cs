// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using StackExchange.Redis;

namespace Microsoft.Health.Fhir.Core.Features.Notifications
{
    public class RedisNotificationService : INotificationService, IDisposable
    {
        private readonly RedisConfiguration _configuration;
        private readonly ILogger<RedisNotificationService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;
        private ConnectionMultiplexer _connection;
        private ISubscriber _subscriber;
        private bool _disposed;

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
            if (!_configuration.Enabled || string.IsNullOrEmpty(_configuration.ConnectionString))
            {
                _logger.LogInformation("Redis notifications are disabled or connection string is not configured.");
                return;
            }

            try
            {
                var configurationOptions = ConfigurationOptions.Parse(_configuration.ConnectionString);
                configurationOptions.AbortOnConnectFail = _configuration.Configuration.AbortOnConnectFail;
                configurationOptions.ConnectRetry = _configuration.Configuration.ConnectRetry;
                configurationOptions.ConnectTimeout = _configuration.Configuration.ConnectTimeout;
                configurationOptions.SyncTimeout = _configuration.Configuration.SyncTimeout;
                configurationOptions.AsyncTimeout = _configuration.Configuration.AsyncTimeout;

                _connection = await ConnectionMultiplexer.ConnectAsync(configurationOptions);
                _subscriber = _connection.GetSubscriber();

                _logger.LogInformation("Redis notification service initialized successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Redis notification service.");
                throw;
            }
        }

        public async Task PublishAsync<T>(string channel, T message, CancellationToken cancellationToken = default)
            where T : class
        {
            EnsureArg.IsNotNullOrWhiteSpace(channel, nameof(channel));
            EnsureArg.IsNotNull(message, nameof(message));

            if (!_configuration.Enabled || _subscriber == null)
            {
                _logger.LogDebug("Redis notifications are disabled. Skipping publish to channel: {Channel}", channel);
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

            if (!_configuration.Enabled || _subscriber == null)
            {
                _logger.LogDebug("Redis notifications are disabled. Skipping subscribe to channel: {Channel}", channel);
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

            if (!_configuration.Enabled || _subscriber == null)
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
