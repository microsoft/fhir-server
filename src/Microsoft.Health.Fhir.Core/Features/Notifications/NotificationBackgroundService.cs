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

            // Check if processing is currently happening
            if (!await _processingGate.WaitAsync(0, cancellationToken))
            {
                _logger.LogInformation("Search parameter update is currently processing. Queueing new notification.");

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
                await ProcessWithDebounceAndQueue(cancellationToken);
            }
            finally
            {
                _processingGate.Release();
            }
        }

        private async Task ProcessWithDebounceAndQueue(CancellationToken cancellationToken)
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
                    _logger.LogDebug("Starting debounce delay of {DelayMs}ms for search parameter updates.", _redisConfiguration.SearchParameterNotificationDelayMs);
                    await Task.Delay(_redisConfiguration.SearchParameterNotificationDelayMs, delayToken);
                }
                catch (OperationCanceledException) when (delayToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    // Delay was cancelled by a new notification, continue the loop to restart delay
                    _logger.LogDebug("Debounce delay was cancelled by newer notification, restarting delay.");
                    continue;
                }

                // Process the actual update (this cannot be cancelled by new notifications)
                await ProcessSearchParameterUpdateWithRetry(cancellationToken);
            }
            while (_isProcessingQueued); // Continue processing if more notifications were queued during processing
        }

        private async Task ProcessSearchParameterUpdateWithRetry(CancellationToken cancellationToken)
        {
            using var statusScope = _serviceProvider.CreateScope();

            try
            {
                var searchParameterOperations = statusScope.ServiceProvider.GetRequiredService<ISearchParameterOperations>();

                _logger.LogInformation("Processing search parameter updates after {DelayMs}ms delay.", _redisConfiguration.SearchParameterNotificationDelayMs);

                // Apply the latest search parameter updates from other instances
                // Use only the service cancellation token, not the delay token
                await searchParameterOperations.GetAndApplySearchParameterUpdates(cancellationToken, true);

                _logger.LogInformation("Successfully applied search parameter updates.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Only log cancellation if it's from service shutdown
                _logger.LogDebug("Search parameter update processing was cancelled due to service shutdown.");
                throw; // Re-throw to properly handle service shutdown
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Only log cancellation if it's from service shutdown
                _logger.LogDebug("Search parameter update processing was cancelled due to service shutdown.");
                throw; // Re-throw to properly handle service shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply search parameter updates.");
            }
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
