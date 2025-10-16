// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Registry
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterCacheRefreshBackgroundServiceTests
    {
        private readonly ISearchParameterStatusManager _searchParameterStatusManager;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly IOptions<CoreFeatureConfiguration> _coreFeatureConfiguration;
        private readonly SearchParameterCacheRefreshBackgroundService _service;

        public SearchParameterCacheRefreshBackgroundServiceTests()
        {
            _searchParameterStatusManager = Substitute.For<ISearchParameterStatusManager>();
            _searchParameterOperations = Substitute.For<ISearchParameterOperations>();
            _coreFeatureConfiguration = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            _coreFeatureConfiguration.Value.Returns(new CoreFeatureConfiguration
            {
                SearchParameterCacheRefreshIntervalSeconds = 60,
            });

            _service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                _coreFeatureConfiguration,
                NullLogger<SearchParameterCacheRefreshBackgroundService>.Instance);
        }

        [Fact]
        public async Task Handle_WhenSearchParametersInitializedNotificationReceived_ShouldSetInitializedFlag()
        {
            // Arrange
            var notification = new SearchParametersInitializedNotification();

            // Act
            await _service.Handle(notification, CancellationToken.None);

            // Assert
            // The method should complete without throwing - the flag is set internally
            // We can't directly assert on the private field, but the test verifies the method works
        }

        [Fact]
        public void Constructor_WithValidConfiguration_ShouldUseConfiguredRefreshInterval()
        {
            // Arrange
            var config = new CoreFeatureConfiguration
            {
                SearchParameterCacheRefreshIntervalSeconds = 300,
            };
            var options = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            options.Value.Returns(config);

            // Act & Assert - Should not throw
            var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                options,
                NullLogger<SearchParameterCacheRefreshBackgroundService>.Instance);

            Assert.NotNull(service);
        }

        [Fact]
        public void Constructor_WithZeroRefreshInterval_ShouldUseDefaultInterval()
        {
            // Arrange
            var config = new CoreFeatureConfiguration
            {
                SearchParameterCacheRefreshIntervalSeconds = 0,
            };
            var options = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            options.Value.Returns(config);

            var mockLogger = Substitute.For<ILogger<SearchParameterCacheRefreshBackgroundService>>();

            // Act
            var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                options,
                mockLogger);

            // Assert
            Assert.NotNull(service);

            // Verify that the constructor logged the correct default interval (1 second) by checking the Log method was called
            mockLogger.Received(1).Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString().Contains("SearchParameter cache refresh background service initialized with 00:00:01 interval.")),
                null,
                Arg.Any<Func<object, Exception, string>>());
        }

        [Fact]
        public void Constructor_WithNegativeRefreshInterval_ShouldUseDefaultInterval()
        {
            // Arrange - Test with negative value
            var config = new CoreFeatureConfiguration
            {
                SearchParameterCacheRefreshIntervalSeconds = -5,
            };
            var options = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            options.Value.Returns(config);

            var mockLogger = Substitute.For<ILogger<SearchParameterCacheRefreshBackgroundService>>();

            // Act
            var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                options,
                mockLogger);

            // Assert
            Assert.NotNull(service);

            // Verify that the constructor logged the correct default interval (1 second) by checking the Log method was called
            mockLogger.Received(1).Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString().Contains("SearchParameter cache refresh background service initialized with 00:00:01 interval.")),
                null,
                Arg.Any<Func<object, Exception, string>>());
        }

        [Fact]
        public void Constructor_WithNullConfiguration_ShouldThrow()
        {
            // Act & Assert - Should throw ArgumentNullException when configuration is null
            Assert.Throws<ArgumentNullException>(() => new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                null,
                NullLogger<SearchParameterCacheRefreshBackgroundService>.Instance));
        }

        [Fact]
        public void Constructor_WithNullSearchParameterOperations_ShouldThrow()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                null,
                _coreFeatureConfiguration,
                NullLogger<SearchParameterCacheRefreshBackgroundService>.Instance));
        }

        [Fact]
        public void Constructor_WithNullSearchParameterStatusManager_ShouldThrow()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SearchParameterCacheRefreshBackgroundService(
                null,
                _searchParameterOperations,
                _coreFeatureConfiguration,
                NullLogger<SearchParameterCacheRefreshBackgroundService>.Instance));
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrow()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                _coreFeatureConfiguration,
                null));
        }

        [Fact]
        public async Task OnRefreshTimer_WhenCacheIsStale_ShouldCallGetAndApplySearchParameterUpdates()
        {
            // Arrange
            _searchParameterStatusManager.ClearReceivedCalls(); // Clear any previous calls
            _searchParameterOperations.ClearReceivedCalls();

            _searchParameterStatusManager.EnsureCacheFreshnessAsync(Arg.Any<CancellationToken>())
                .Returns(true); // Cache is stale

            // Set initialized to true to allow timer to run
            await _service.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);

            // Wait for the timer to fire at least once and allow async operations to complete
            await Task.Delay(200);

            // Assert - use at least 1 call since timer might fire multiple times in test environment
            await _searchParameterStatusManager.Received().EnsureCacheFreshnessAsync(Arg.Any<CancellationToken>());
            await _searchParameterOperations.Received().GetAndApplySearchParameterUpdates(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task OnRefreshTimer_WhenCacheIsFresh_ShouldNotCallGetAndApplySearchParameterUpdates()
        {
            // Arrange
            _searchParameterStatusManager.ClearReceivedCalls(); // Clear any previous calls
            _searchParameterOperations.ClearReceivedCalls();

            _searchParameterStatusManager.EnsureCacheFreshnessAsync(Arg.Any<CancellationToken>())
                .Returns(false); // Cache is fresh

            // Set initialized to true to allow timer to run
            await _service.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);

            // Wait for the timer to fire at least once and allow async operations to complete
            await Task.Delay(200);

            // Assert - verify the timer is working and EnsureCacheFreshnessAsync was called
            await _searchParameterStatusManager.Received().EnsureCacheFreshnessAsync(Arg.Any<CancellationToken>());

            // But GetAndApplySearchParameterUpdates should never be called when cache is fresh
            await _searchParameterOperations.DidNotReceive().GetAndApplySearchParameterUpdates(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ExecuteAsync_WhenCancellationRequested_ShouldStopGracefully()
        {
            // Arrange
            using var cancellationTokenSource = new CancellationTokenSource();
            var mockLogger = Substitute.For<ILogger<SearchParameterCacheRefreshBackgroundService>>();

            var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                _coreFeatureConfiguration,
                mockLogger);

            // Act
            var executeTask = service.StartAsync(cancellationTokenSource.Token);

            // Allow some time for service to start
            await Task.Delay(100);

            // Cancel the service
            cancellationTokenSource.Cancel();

            // Wait for the service to stop
            await executeTask;

            // Assert - Verify that stopping was logged
            mockLogger.Received().Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString().Contains("SearchParameterCacheRefreshBackgroundService stopping due to cancellation request.") ||
                                    o.ToString().Contains("SearchParameterCacheRefreshBackgroundService was cancelled before initialization completed.")),
                null,
                Arg.Any<Func<object, Exception, string>>());
        }

        [Fact]
        public async Task OnRefreshTimer_WhenServiceProviderDisposed_ShouldHandleGracefully()
        {
            // Arrange
            var mockLogger = Substitute.For<ILogger<SearchParameterCacheRefreshBackgroundService>>();

            // Set up the status manager to throw ObjectDisposedException to simulate the service provider being disposed
            _searchParameterStatusManager.EnsureCacheFreshnessAsync(Arg.Any<CancellationToken>())
                .Returns<Task<bool>>(_ => throw new ObjectDisposedException("IServiceProvider"));

            var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                _coreFeatureConfiguration,
                mockLogger);

            // Act - Initialize and let timer run
            await service.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);

            // Wait for timer to fire and handle the exception
            await Task.Delay(200);

            // Assert - Verify that ObjectDisposedException was handled and logged appropriately
            mockLogger.Received().Log(
                LogLevel.Debug,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString().Contains("SearchParameter cache refresh encountered disposed service during shutdown.")),
                null,
                Arg.Any<Func<object, Exception, string>>());

            service.Dispose();
        }

        [Fact]
        public async Task OnRefreshTimer_WhenOperationCanceled_ShouldHandleGracefully()
        {
            // Arrange
            var mockLogger = Substitute.For<ILogger<SearchParameterCacheRefreshBackgroundService>>();

            // Set up the status manager to throw OperationCanceledException
            _searchParameterStatusManager.EnsureCacheFreshnessAsync(Arg.Any<CancellationToken>())
                .Returns<Task<bool>>(_ => throw new OperationCanceledException());

            var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                _coreFeatureConfiguration,
                mockLogger);

            // Act - Initialize and let timer run
            await service.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);

            // Wait for timer to fire and handle the exception
            await Task.Delay(200);

            // Assert - Verify that OperationCanceledException was handled and logged appropriately
            mockLogger.Received().Log(
                LogLevel.Debug,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString().Contains("SearchParameter cache refresh was canceled during operation.")),
                null,
                Arg.Any<Func<object, Exception, string>>());

            service.Dispose();
        }

        [Fact]
        public async Task Handle_WhenServiceAlreadyCancelled_ShouldNotStartTimer()
        {
            // Arrange
            using var cancellationTokenSource = new CancellationTokenSource();
            var mockLogger = Substitute.For<ILogger<SearchParameterCacheRefreshBackgroundService>>();

            var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                _coreFeatureConfiguration,
                mockLogger);

            // Start the service and then immediately cancel it
            var executeTask = service.StartAsync(cancellationTokenSource.Token);
            cancellationTokenSource.Cancel();
            await executeTask;

            // Act - Try to handle the notification after cancellation
            await service.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);

            // Assert - Timer should not fire, so no calls should be made
            await Task.Delay(200); // Wait to see if timer would fire
            await _searchParameterStatusManager.DidNotReceive().EnsureCacheFreshnessAsync(Arg.Any<CancellationToken>());

            service.Dispose();
        }
    }
}
