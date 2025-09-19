// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Notifications.Models;
using Microsoft.Health.Fhir.Core.Messages.Search;

namespace Microsoft.Health.Fhir.Core.Features.Notifications
{
    /// <summary>
    /// Implementation of unified notification publisher that can optionally broadcast to Redis for cross-instance sync.
    /// </summary>
    public class UnifiedNotificationPublisher : IUnifiedNotificationPublisher
    {
        private readonly IMediator _mediator;
        private readonly INotificationService _notificationService;
        private readonly RedisConfiguration _redisConfiguration;
        private readonly ILogger<UnifiedNotificationPublisher> _logger;

        public UnifiedNotificationPublisher(
            IMediator mediator,
            INotificationService notificationService,
            IOptions<RedisConfiguration> redisConfiguration,
            ILogger<UnifiedNotificationPublisher> logger)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(notificationService, nameof(notificationService));
            EnsureArg.IsNotNull(redisConfiguration, nameof(redisConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _mediator = mediator;
            _notificationService = notificationService;
            _redisConfiguration = redisConfiguration.Value;
            _logger = logger;
        }

        /// <summary>
        /// Gets the unique identifier for this FHIR server instance.
        /// </summary>
        public string InstanceId => Environment.MachineName;

        /// <summary>
        /// Standard MediatR publish - no Redis.
        /// </summary>
        /// <typeparam name="TNotification">The notification type.</typeparam>
        /// <param name="notification">The notification to publish.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : class, INotification
        {
            await _mediator.Publish(notification, cancellationToken);
        }

        /// <summary>
        /// Redis publish with fallback to MediatR local publish
        /// </summary>
        /// <typeparam name="TNotification">The notification type.</typeparam>
        /// <param name="notification">The notification to publish.</param>
        /// <param name="enableRedisNotification">Whether to also broadcast via Redis for cross-instance sync.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task PublishAsync<TNotification>(
            TNotification notification,
            bool enableRedisNotification,
            CancellationToken cancellationToken = default)
            where TNotification : class, INotification
        {
            // Optionally publish via Redis
            if (enableRedisNotification && _redisConfiguration.Enabled)
            {
                await PublishToRedis(notification, cancellationToken);
            }
            else
            {
                await _mediator.Publish(notification, cancellationToken);
            }
        }

        private async Task PublishToRedis<TNotification>(TNotification notification, CancellationToken cancellationToken)
            where TNotification : class, INotification
        {
            var redisNotification = ConvertToRedisNotification(notification);
            if (redisNotification == null)
            {
                // Fail fast for unsupported notification types when Redis is enabled
                throw new NotSupportedException(
                    $"Notification type '{typeof(TNotification).Name}' does not support Redis publishing. " +
                    $"Either disable Redis notifications for this type or implement Redis conversion support.");
            }

            try
            {
                await _notificationService.PublishAsync(
                    _redisConfiguration.NotificationChannels.SearchParameterUpdates,
                    redisNotification,
                    cancellationToken);

                _logger.LogDebug("Published Redis notification for {NotificationType}", typeof(TNotification).Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish Redis notification for {NotificationType}", typeof(TNotification).Name);

                // Don't throw - Redis failures shouldn't break local processing
                await _mediator.Publish(notification, cancellationToken);
            }
        }

        private SearchParameterChangeNotification ConvertToRedisNotification<TNotification>(TNotification notification)
            where TNotification : class, INotification
        {
            return notification switch
            {
                SearchParametersUpdatedNotification updatedNotification => new SearchParameterChangeNotification
                {
                    InstanceId = InstanceId,
                    Timestamp = DateTimeOffset.UtcNow,
                    ChangeType = SearchParameterChangeType.StatusChanged,
                    AffectedParameterUris = updatedNotification.SearchParameters
                        .Select(sp => sp.Url?.ToString())
                        .Where(url => !string.IsNullOrEmpty(url))
                        .ToList(),
                    TriggerSource = "UnifiedNotificationPublisher",
                },
                _ => null, // Only handle SearchParametersUpdatedNotification for now
            };
        }
    }
}
