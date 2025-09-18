// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Notifications.Models;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.Core.Features.Notifications
{
    public class NotificationBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly RedisConfiguration _redisConfiguration;
        private readonly ILogger<NotificationBackgroundService> _logger;
        private readonly SemaphoreSlim _processingGate = new SemaphoreSlim(1, 1);
        private volatile bool _isProcessingQueued = false;
        private CancellationTokenSource _currentDelayTokenSource;
        private readonly object _delayLock = new object();

        public NotificationBackgroundService(
            IServiceProvider serviceProvider,
            IOptions<RedisConfiguration> redisConfiguration,
            ILogger<NotificationBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _redisConfiguration = redisConfiguration.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_redisConfiguration.Enabled)
            {
                _logger.LogInformation("Redis notifications are disabled. Notification background service will not start.");
                return;
            }

            _logger.LogInformation("Starting notification background service.");

            using var scope = _serviceProvider.CreateScope();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            // Subscribe to search parameter change notifications
            await notificationService.SubscribeAsync<SearchParameterChangeNotification>(
                _redisConfiguration.NotificationChannels.SearchParameterUpdates,
                HandleSearchParameterChangeNotification,
                stoppingToken);

            // Keep the service running
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private async Task HandleSearchParameterChangeNotification(
            SearchParameterChangeNotification notification,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Received search parameter change notification from instance {InstanceId} at {Timestamp}. ChangeType: {ChangeType}, Source: {TriggerSource}",
                notification.InstanceId,
                notification.Timestamp,
                notification.ChangeType,
                notification.TriggerSource);

            // Get the current instance ID for comparison
            using var scope = _serviceProvider.CreateScope();
            var unifiedPublisher = scope.ServiceProvider.GetRequiredService<IUnifiedNotificationPublisher>();
            var currentInstanceId = unifiedPublisher.InstanceId;

            // Skip processing if notification is from the same instance
            if (string.Equals(notification.InstanceId, currentInstanceId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug(
                    "Skipping search parameter change notification from same instance {InstanceId}. ChangeType: {ChangeType}",
                    notification.InstanceId,
                    notification.ChangeType);
                return;
            }

            // Use debouncing for search parameter processing
            var debounceConfig = new DebounceConfig
            {
                DelayMs = _redisConfiguration.SearchParameterNotificationDelayMs,
                ProcessingAction = ProcessSearchParameterUpdate,
                ProcessingName = "search parameter updates",
            };

            await ProcessWithOptionalDebouncing(debounceConfig, cancellationToken);
        }

        /// <summary>
        /// Processes a notification with optional debouncing and queueing.
        /// Debouncing is applied when DelayMs > 0, otherwise processes immediately.
        /// </summary>
        /// <param name="debounceConfig">Configuration for processing. Cannot be null.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task ProcessWithOptionalDebouncing(DebounceConfig debounceConfig, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(debounceConfig);
            debounceConfig.Validate();

            if (debounceConfig.DelayMs <= 0)
            {
                // Process immediately without debouncing or semaphore management
                _logger.LogDebug("Processing {ProcessingName} immediately (no debouncing)", debounceConfig.ProcessingName);
                await ProcessWithRetry(debounceConfig, cancellationToken);
                return;
            }

            // Check if processing is currently happening
            if (!await _processingGate.WaitAsync(0, cancellationToken))
            {
                _logger.LogInformation(
                    "{ProcessingName} is currently processing. Queueing new notification.",
                    debounceConfig.ProcessingName);

                // Queue this notification by setting the flag and updating delay token
                _isProcessingQueued = true;

                // Cancel only the delay, not the active processing
                lock (_delayLock)
                {
                    _ = _currentDelayTokenSource?.CancelAsync();
                    _currentDelayTokenSource?.Dispose();
                    _currentDelayTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                }

                return;
            }

            try
            {
                // Start the debounced processing
                await ProcessWithDebounceAndQueue(debounceConfig, cancellationToken);
            }
            finally
            {
                _processingGate.Release();
            }
        }

        /// <summary>
        /// Generic method for debouncing and queueing any type of processing.
        /// </summary>
        /// <param name="debounceConfig">Configuration for the debounced processing.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        private async Task ProcessWithDebounceAndQueue(DebounceConfig debounceConfig, CancellationToken cancellationToken)
        {
            do
            {
                _isProcessingQueued = false;

                // Set up delay cancellation token
                lock (_delayLock)
                {
                    _currentDelayTokenSource?.Dispose();
                    _currentDelayTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                }

                var delayToken = _currentDelayTokenSource.Token;

                try
                {
                    _logger.LogDebug(
                        "Starting debounce delay of {DelayMs}ms for {ProcessingName}.",
                        debounceConfig.DelayMs,
                        debounceConfig.ProcessingName);
                    await Task.Delay(debounceConfig.DelayMs, delayToken);
                }
                catch (OperationCanceledException) when (delayToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    // Delay was cancelled by a new notification, continue the loop to restart delay
                    _logger.LogDebug(
                        "Debounce delay for {ProcessingName} was cancelled by newer notification, restarting delay.",
                        debounceConfig.ProcessingName);
                    continue;
                }

                // Process the actual update (this cannot be cancelled by new notifications)
                await ProcessWithRetry(debounceConfig, cancellationToken);
            }
            while (_isProcessingQueued); // Continue processing if more notifications were queued during processing
        }

        /// <summary>
        /// Generic method for executing processing actions with retry logic and error handling.
        /// </summary>
        /// <param name="debounceConfig">Configuration for the processing.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        private async Task ProcessWithRetry(DebounceConfig debounceConfig, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation(
                    "Processing {ProcessingName} after {DelayMs}ms delay.",
                    debounceConfig.ProcessingName,
                    debounceConfig.DelayMs);

                await debounceConfig.ProcessingAction(cancellationToken);

                _logger.LogInformation("Successfully processed {ProcessingName}.", debounceConfig.ProcessingName);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Only log cancellation if it's from service shutdown
                _logger.LogDebug("{ProcessingName} processing was cancelled due to service shutdown.", debounceConfig.ProcessingName);
                throw; // Re-throw to properly handle service shutdown
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Only log cancellation if it's from service shutdown
                _logger.LogDebug("{ProcessingName} processing was cancelled due to service shutdown.", debounceConfig.ProcessingName);
                throw; // Re-throw to properly handle service shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process {ProcessingName}.", debounceConfig.ProcessingName);
            }
        }

        /// <summary>
        /// Specific processing logic for search parameter updates.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        private async Task ProcessSearchParameterUpdate(CancellationToken cancellationToken)
        {
            using var statusScope = _serviceProvider.CreateScope();
            var searchParameterOperations = statusScope.ServiceProvider.GetRequiredService<ISearchParameterOperations>();

            // Apply the latest search parameter updates from other instances
            // Use only the service cancellation token, not the delay token
            await searchParameterOperations.GetAndApplySearchParameterUpdates(cancellationToken, true);
        }

        public override void Dispose()
        {
            lock (_delayLock)
            {
                _ = _currentDelayTokenSource?.CancelAsync();
                _currentDelayTokenSource?.Dispose();
            }

            _processingGate?.Dispose();
            base.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
