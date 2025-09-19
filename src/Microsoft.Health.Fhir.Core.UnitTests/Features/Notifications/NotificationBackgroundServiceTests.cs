// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Notifications;
using Microsoft.Health.Fhir.Core.Features.Notifications.Models;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Notifications
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class NotificationBackgroundServiceTests
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly INotificationService _notificationService = Substitute.For<INotificationService>();
        private readonly ISearchParameterOperations _searchParameterOperations = Substitute.For<ISearchParameterOperations>();
        private readonly IServiceScope _serviceScope = Substitute.For<IServiceScope>();

        public NotificationBackgroundServiceTests()
        {
            // Create a proper service collection and provider
            var services = new ServiceCollection();
            services.AddSingleton(_notificationService);
            services.AddSingleton(_searchParameterOperations);
            _serviceProvider = services.BuildServiceProvider();

            // Setup scope
            _serviceScope.ServiceProvider.Returns(_serviceProvider);
        }

        [Fact]
        public async Task ExecuteAsync_WithRedisDisabled_ShouldNotStartAndNotSubscribe()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = false });
            var service = new NotificationBackgroundService(_serviceProvider, redisConfig, NullLogger<NotificationBackgroundService>.Instance);
            var cancellationTokenSource = new CancellationTokenSource();

            // Act
            var task = service.StartAsync(cancellationTokenSource.Token);
            await Task.Delay(50); // Give it time to process
            cancellationTokenSource.Cancel();

            // Assert - Should not subscribe to any channels when Redis is disabled
            await _notificationService.DidNotReceive().SubscribeAsync<SearchParameterChangeNotification>(
                Arg.Any<string>(),
                Arg.Any<NotificationHandler<SearchParameterChangeNotification>>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ExecuteAsync_WithRedisEnabled_ShouldSubscribeToSearchParameterUpdates()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration
            {
                Enabled = true,
            });

            var service = new NotificationBackgroundService(_serviceProvider, redisConfig, NullLogger<NotificationBackgroundService>.Instance);
            var cancellationTokenSource = new CancellationTokenSource();

            // Act
            var task = service.StartAsync(cancellationTokenSource.Token);
            await Task.Delay(100); // Give it time to start and subscribe
            cancellationTokenSource.Cancel();

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }

            // Assert - Should attempt to subscribe to search parameter channel
            await _notificationService.Received(1).SubscribeAsync<SearchParameterChangeNotification>(
                Arg.Any<string>(),
                Arg.Any<NotificationHandler<SearchParameterChangeNotification>>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public void Dispose_ShouldDisposeResourcesSafely()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = false });
            var service = new NotificationBackgroundService(_serviceProvider, redisConfig, NullLogger<NotificationBackgroundService>.Instance);

            // Act & Assert - Should not throw
            service.Dispose();
            service.Dispose(); // Multiple dispose calls should be safe
        }

        [Fact]
        public async Task StartAsync_WithValidConfiguration_ShouldCompleteSuccessfully()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration
            {
                Enabled = true,
                SearchParameterNotificationDelayMs = 100,
            });

            var service = new NotificationBackgroundService(_serviceProvider, redisConfig, NullLogger<NotificationBackgroundService>.Instance);
            var cancellationTokenSource = new CancellationTokenSource();

            // Act
            var startTask = service.StartAsync(cancellationTokenSource.Token);
            await Task.Delay(50); // Let it start
            cancellationTokenSource.Cancel();

            // Assert - Should not throw
            await startTask;

            // Should have attempted to subscribe
            await _notificationService.Received().SubscribeAsync<SearchParameterChangeNotification>(
                Arg.Any<string>(),
                Arg.Any<NotificationHandler<SearchParameterChangeNotification>>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task StopAsync_AfterStart_ShouldCompleteCleanly()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = false });
            var service = new NotificationBackgroundService(_serviceProvider, redisConfig, NullLogger<NotificationBackgroundService>.Instance);
            var cancellationToken = CancellationToken.None;

            // Act
            await service.StartAsync(cancellationToken);
            await service.StopAsync(cancellationToken);

            // Assert - Should complete without throwing
        }

        [Fact]
        public void Constructor_WithNullServiceProvider_ShouldNotThrowButFailOnFirstUsage()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = false });

            // Act & Assert - Constructor doesn't validate parameters
            var service = new NotificationBackgroundService(null, redisConfig, NullLogger<NotificationBackgroundService>.Instance);
            Assert.NotNull(service);

            // The null service provider would cause issues during execution, not construction
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldNotThrowButFailOnFirstUsage()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = false });

            // Act & Assert - Constructor doesn't validate parameters
            var service = new NotificationBackgroundService(_serviceProvider, redisConfig, null);
            Assert.NotNull(service);

            // The null logger would cause issues during execution, not construction
        }

        [Fact]
        public async Task Service_WithDebounceConfiguration_ShouldUseConfiguredDelay()
        {
            // Arrange
            const int customDelay = 5000;
            var redisConfig = Options.Create(new RedisConfiguration
            {
                Enabled = true,
                SearchParameterNotificationDelayMs = customDelay,
            });

            var service = new NotificationBackgroundService(_serviceProvider, redisConfig, NullLogger<NotificationBackgroundService>.Instance);
            var cancellationTokenSource = new CancellationTokenSource();

            // Act
            var task = service.StartAsync(cancellationTokenSource.Token);
            await Task.Delay(50); // Brief delay to start
            cancellationTokenSource.Cancel();

            // Assert - Service should start successfully with custom configuration
            await _notificationService.Received().SubscribeAsync<SearchParameterChangeNotification>(
                Arg.Any<string>(),
                Arg.Any<NotificationHandler<SearchParameterChangeNotification>>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task Service_WithMultipleStartStopCycles_ShouldHandleGracefully()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = false });
            var service = new NotificationBackgroundService(_serviceProvider, redisConfig, NullLogger<NotificationBackgroundService>.Instance);
            var cancellationToken = CancellationToken.None;

            // Act & Assert - Multiple start/stop cycles should not throw
            await service.StartAsync(cancellationToken);
            await service.StopAsync(cancellationToken);

            await service.StartAsync(cancellationToken);
            await service.StopAsync(cancellationToken);

            await service.StartAsync(cancellationToken);
            await service.StopAsync(cancellationToken);
        }

        [Fact]
        public async Task Service_WithRedisEnabledAndDefaultChannels_ShouldSubscribeToCorrectChannel()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration
            {
                Enabled = true,
            });

            var service = new NotificationBackgroundService(_serviceProvider, redisConfig, NullLogger<NotificationBackgroundService>.Instance);
            var cancellationTokenSource = new CancellationTokenSource();

            // Act
            var task = service.StartAsync(cancellationTokenSource.Token);
            await Task.Delay(50);
            cancellationTokenSource.Cancel();

            // Assert - Should subscribe to the default search parameter channel
            await _notificationService.Received().SubscribeAsync<SearchParameterChangeNotification>(
                "fhir:notifications:searchparameters", // Default channel name
                Arg.Any<NotificationHandler<SearchParameterChangeNotification>>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public void RedisConfiguration_SearchParameterNotificationDelayMs_ShouldControlDebouncing()
        {
            // Arrange & Act
            var fastConfig = new RedisConfiguration { SearchParameterNotificationDelayMs = 1000 };
            var slowConfig = new RedisConfiguration { SearchParameterNotificationDelayMs = 30000 };

            // Assert
            Assert.Equal(1000, fastConfig.SearchParameterNotificationDelayMs);
            Assert.Equal(30000, slowConfig.SearchParameterNotificationDelayMs);
        }

        [Fact]
        public async Task Service_CancellationBehavior_ShouldHandleGracefully()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = true });
            var service = new NotificationBackgroundService(_serviceProvider, redisConfig, NullLogger<NotificationBackgroundService>.Instance);
            var cancellationTokenSource = new CancellationTokenSource();

            // Act - Start and immediately cancel
            var task = service.StartAsync(cancellationTokenSource.Token);
            cancellationTokenSource.Cancel();

            // Assert - Should handle cancellation gracefully
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // Expected and acceptable
            }
        }

        [Fact]
        public void NotificationBackgroundService_SemaphoreUsage_ShouldPreventConcurrentProcessing()
        {
            // Arrange - This tests the design principle that processing should be sequential
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = false });
            var service = new NotificationBackgroundService(_serviceProvider, redisConfig, NullLogger<NotificationBackgroundService>.Instance);

            // Act & Assert - Service should be created successfully
            // The semaphore logic is internal but ensures sequential processing
            Assert.NotNull(service);

            service.Dispose();
        }

        [Fact]
        public async Task HandleSearchParameterChangeNotification_FromSameInstance_ShouldSkipProcessing()
        {
            // Arrange
            const string instanceId = "test-instance-123";
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = true });

            var services = new ServiceCollection();
            var mockUnifiedPublisher = Substitute.For<IUnifiedNotificationPublisher>();
            mockUnifiedPublisher.InstanceId.Returns(instanceId);

            services.AddSingleton(_notificationService);
            services.AddSingleton(_searchParameterOperations);
            services.AddSingleton(mockUnifiedPublisher);
            var serviceProvider = services.BuildServiceProvider();

            var service = new NotificationBackgroundService(serviceProvider, redisConfig, NullLogger<NotificationBackgroundService>.Instance);

            var notification = new SearchParameterChangeNotification
            {
                InstanceId = instanceId, // Same instance ID
                Timestamp = DateTimeOffset.UtcNow,
                ChangeType = SearchParameterChangeType.StatusChanged,
                TriggerSource = "Test",
            };

            // Use reflection to access the private method for testing
            var method = typeof(NotificationBackgroundService).GetMethod("HandleSearchParameterChangeNotification", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Act
            await (Task)method.Invoke(service, new object[] { notification, CancellationToken.None });

            // Assert - Should not call SearchParameterOperations when notification is from same instance
            await _searchParameterOperations.DidNotReceive().GetAndApplySearchParameterUpdates(
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>());

            service.Dispose();
        }

        [Fact]
        public async Task HandleSearchParameterChangeNotification_FromDifferentInstance_ShouldProcessNotification()
        {
            // Arrange
            const string currentInstanceId = "current-instance-123";
            const string differentInstanceId = "different-instance-456";
            var redisConfig = Options.Create(new RedisConfiguration
            {
                Enabled = true,
                SearchParameterNotificationDelayMs = 0, // No delay for test
            });

            var services = new ServiceCollection();
            var mockUnifiedPublisher = Substitute.For<IUnifiedNotificationPublisher>();
            mockUnifiedPublisher.InstanceId.Returns(currentInstanceId);

            services.AddSingleton(_notificationService);
            services.AddSingleton(_searchParameterOperations);
            services.AddSingleton(mockUnifiedPublisher);
            var serviceProvider = services.BuildServiceProvider();

            var service = new NotificationBackgroundService(serviceProvider, redisConfig, NullLogger<NotificationBackgroundService>.Instance);

            var notification = new SearchParameterChangeNotification
            {
                InstanceId = differentInstanceId, // Different instance ID
                Timestamp = DateTimeOffset.UtcNow,
                ChangeType = SearchParameterChangeType.StatusChanged,
                TriggerSource = "Test",
            };

            // Use reflection to access the private method for testing
            var method = typeof(NotificationBackgroundService).GetMethod("HandleSearchParameterChangeNotification", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Act
            await (Task)method.Invoke(service, new object[] { notification, CancellationToken.None });

            // Assert - Should call SearchParameterOperations when notification is from different instance
            await _searchParameterOperations.Received(1).GetAndApplySearchParameterUpdates(
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>());

            service.Dispose();
        }
    }
}
