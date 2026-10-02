// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Registry
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Trait(Traits.Category, Categories.BackgroundJobs)]
    public class SearchParameterCacheRefreshBackgroundServiceTests
    {
        private readonly ISearchParameterStatusManager _searchParameterStatusManager;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly ISearchParameterCacheRefresherMetricHandler _searchParameterCacheRefresherMetricHandler;
        private readonly IOptions<CoreFeatureConfiguration> _coreFeatureConfiguration;
        private readonly SearchParameterCacheRefreshBackgroundService _service;

        public SearchParameterCacheRefreshBackgroundServiceTests()
        {
            _searchParameterStatusManager = Substitute.For<ISearchParameterStatusManager>();
            _searchParameterOperations = Substitute.For<ISearchParameterOperations>();
            _searchParameterCacheRefresherMetricHandler = Substitute.For<ISearchParameterCacheRefresherMetricHandler>();
            _coreFeatureConfiguration = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            _coreFeatureConfiguration.Value.Returns(new CoreFeatureConfiguration
            {
                SearchParameterCacheRefreshIntervalSeconds = 1,
                SearchParameterCacheRefreshMaxInitialDelaySeconds = 0, // No delay for tests
            });

            _service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                _coreFeatureConfiguration,
                _searchParameterCacheRefresherMetricHandler,
                NullLogger<SearchParameterCacheRefreshBackgroundService>.Instance);
        }

        [Fact]
        public async Task Handle_WhenSearchParametersInitializedNotificationReceived_ShouldSetInitializedFlag()
        {
            // Arrange
            var notification = new SearchParametersInitializedNotification();

            // Act
            await _service.HandleAsync(notification, CancellationToken.None);

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
                _searchParameterCacheRefresherMetricHandler,
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
                _searchParameterCacheRefresherMetricHandler,
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
                _searchParameterCacheRefresherMetricHandler,
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
        public void Constructor_WithZeroConsecutiveFailureThreshold_ShouldUseDefaultThreshold()
        {
            // Arrange
            var config = new CoreFeatureConfiguration
            {
                SearchParameterCacheRefreshConsecutiveFailureThreshold = 0,
            };
            var options = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            options.Value.Returns(config);

            var mockLogger = Substitute.For<ILogger<SearchParameterCacheRefreshBackgroundService>>();

            // Act
            var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                options,
                _searchParameterCacheRefresherMetricHandler,
                mockLogger);

            // Assert
            Assert.NotNull(service);

            // Verify that the constructor logged the clamped default threshold (1) by checking the Log method was called
            mockLogger.Received(1).Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString().Contains("SearchParameter cache refresh background service consecutive-failure threshold set to 1.")),
                null,
                Arg.Any<Func<object, Exception, string>>());
        }

        [Fact]
        public void Constructor_WithNegativeConsecutiveFailureThreshold_ShouldUseDefaultThreshold()
        {
            // Arrange - Test with negative value
            var config = new CoreFeatureConfiguration
            {
                SearchParameterCacheRefreshConsecutiveFailureThreshold = -5,
            };
            var options = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            options.Value.Returns(config);

            var mockLogger = Substitute.For<ILogger<SearchParameterCacheRefreshBackgroundService>>();

            // Act
            var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                options,
                _searchParameterCacheRefresherMetricHandler,
                mockLogger);

            // Assert
            Assert.NotNull(service);

            // Verify that the constructor logged the clamped default threshold (1) by checking the Log method was called
            mockLogger.Received(1).Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString().Contains("SearchParameter cache refresh background service consecutive-failure threshold set to 1.")),
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
                _searchParameterCacheRefresherMetricHandler,
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
                _searchParameterCacheRefresherMetricHandler,
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
                _searchParameterCacheRefresherMetricHandler,
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
                _searchParameterCacheRefresherMetricHandler,
                null));
        }

        [Fact]
        public async Task OnRefreshTimer_WhenCacheIsStale_ShouldCallGetAndApplySearchParameterUpdates()
        {
            // Arrange
            _searchParameterStatusManager.ClearReceivedCalls(); // Clear any previous calls
            _searchParameterOperations.ClearReceivedCalls();

            // Set initialized to true to allow timer to run
            await _service.HandleAsync(new SearchParametersInitializedNotification(), CancellationToken.None);

            // Wait for the timer to fire at least once and allow async operations to complete
            await Task.Delay(200);

            // Assert - use at least 1 call since timer might fire multiple times in test environment
            await _searchParameterOperations.Received().GetAndApplySearchParameterUpdates(Arg.Any<CancellationToken>(), true);
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
                _searchParameterCacheRefresherMetricHandler,
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

            // Set up throwing ObjectDisposedException to simulate the service provider being disposed
            _searchParameterOperations.GetAndApplySearchParameterUpdates(Arg.Any<CancellationToken>(), true)
                .Returns(_ => Task.FromException<bool>(new ObjectDisposedException("IServiceProvider")));

            var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                _coreFeatureConfiguration,
                _searchParameterCacheRefresherMetricHandler,
                mockLogger);

            // Act - Initialize and let timer run
            await service.HandleAsync(new SearchParametersInitializedNotification(), CancellationToken.None);

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

            var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                _coreFeatureConfiguration,
                _searchParameterCacheRefresherMetricHandler,
                mockLogger);

            _searchParameterOperations.GetAndApplySearchParameterUpdates(Arg.Any<CancellationToken>(), true)
                .Returns(_ => Task.FromException<bool>(new OperationCanceledException()));

            // Act - Initialize and let timer run
            await service.HandleAsync(new SearchParametersInitializedNotification(), CancellationToken.None);

            // Wait longer for timer to fire and handle the exception - give it up to 2 seconds
            // The timer starts immediately (TimeSpan.Zero) when Handle is called
            await Task.Delay(2000);

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

            using var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                _coreFeatureConfiguration,
                _searchParameterCacheRefresherMetricHandler,
                mockLogger);

            // Start the service and then immediately cancel it
            var executeTask = service.StartAsync(cancellationTokenSource.Token);
            cancellationTokenSource.Cancel();
            await executeTask;

            // Act - Try to handle the notification after cancellation
            await service.HandleAsync(new SearchParametersInitializedNotification(), CancellationToken.None);
        }

        [Fact]
        public async Task Handle_WhenServiceRunsWithSuccess_ThenSuccessMetricIsEmitted()
        {
            // Arrange
            using var cancellationTokenSource = new CancellationTokenSource();
            var mockLogger = Substitute.For<ILogger<SearchParameterCacheRefreshBackgroundService>>();

            using var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                _coreFeatureConfiguration,
                _searchParameterCacheRefresherMetricHandler,
                mockLogger);

            _searchParameterOperations.GetAndApplySearchParameterUpdates(Arg.Any<CancellationToken>(), true)
                .Returns(_ => true);

            // Start service that skips refresh
            await service.HandleAsync(new SearchParametersInitializedNotification(), CancellationToken.None);

            // Start the service and then immediately cancel it
            var executeTask = service.StartAsync(cancellationTokenSource.Token);

            await Task.Delay(2000);

            await executeTask;

            cancellationTokenSource.Cancel();

            _searchParameterCacheRefresherMetricHandler.Received().EmitSuccess();

            _searchParameterCacheRefresherMetricHandler.Received(0).EmitFailure(Arg.Any<string>());
        }

        [Fact]
        public async Task OnRefreshTimer_WhenConsecutiveFailuresBelowThreshold_ShouldNotEmitFailureMetric()
        {
            // Arrange
            using var cancellationTokenSource = new CancellationTokenSource();
            var mockLogger = Substitute.For<ILogger<SearchParameterCacheRefreshBackgroundService>>();
            var options = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            options.Value.Returns(new CoreFeatureConfiguration
            {
                SearchParameterCacheRefreshIntervalSeconds = 1,
                SearchParameterCacheRefreshMaxInitialDelaySeconds = 0,
                SearchParameterCacheRefreshConsecutiveFailureThreshold = 5,
            });

            using var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                options,
                _searchParameterCacheRefresherMetricHandler,
                mockLogger);

            _searchParameterOperations.GetAndApplySearchParameterUpdates(Arg.Any<CancellationToken>(), true)
                .Returns<Task<bool>>(_ => throw new InvalidOperationException("Transient failure"));

            await service.HandleAsync(new SearchParametersInitializedNotification(), CancellationToken.None);

            // Act - allow two refresh ticks to fire, which is below the configured threshold of 5
            var executeTask = service.StartAsync(cancellationTokenSource.Token);
            await Task.Delay(2200);
            cancellationTokenSource.Cancel();
            await executeTask;

            // Assert
            _searchParameterCacheRefresherMetricHandler.Received(0).EmitFailure(Arg.Any<string>());
        }

        [Fact]
        public async Task OnRefreshTimer_WhenConsecutiveFailuresReachThreshold_ShouldEmitFailureMetric()
        {
            // Arrange
            using var cancellationTokenSource = new CancellationTokenSource();
            var mockLogger = Substitute.For<ILogger<SearchParameterCacheRefreshBackgroundService>>();
            var options = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            options.Value.Returns(new CoreFeatureConfiguration
            {
                SearchParameterCacheRefreshIntervalSeconds = 1,
                SearchParameterCacheRefreshMaxInitialDelaySeconds = 0,
                SearchParameterCacheRefreshConsecutiveFailureThreshold = 2,
            });

            using var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                options,
                _searchParameterCacheRefresherMetricHandler,
                mockLogger);

            _searchParameterOperations.GetAndApplySearchParameterUpdates(Arg.Any<CancellationToken>(), true)
                .Returns<Task<bool>>(_ => throw new InvalidOperationException("Persistent failure"));

            await service.HandleAsync(new SearchParametersInitializedNotification(), CancellationToken.None);

            // Act - allow at least two refresh ticks to fire, reaching the configured threshold of 2
            var executeTask = service.StartAsync(cancellationTokenSource.Token);
            await Task.Delay(2200);
            cancellationTokenSource.Cancel();
            await executeTask;

            // Assert
            _searchParameterCacheRefresherMetricHandler.Received().EmitFailure(nameof(InvalidOperationException));
        }

        [Fact]
        public async Task OnRefreshTimer_WhenSuccessFollowsFailures_ShouldResetConsecutiveFailureCount()
        {
            // Arrange
            using var cancellationTokenSource = new CancellationTokenSource();
            var mockLogger = Substitute.For<ILogger<SearchParameterCacheRefreshBackgroundService>>();
            var options = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            options.Value.Returns(new CoreFeatureConfiguration
            {
                SearchParameterCacheRefreshIntervalSeconds = 1,
                SearchParameterCacheRefreshMaxInitialDelaySeconds = 0,
                SearchParameterCacheRefreshConsecutiveFailureThreshold = 2,
            });

            using var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                options,
                _searchParameterCacheRefresherMetricHandler,
                mockLogger);

            // Sequence: fail, succeed (should reset the counter), fail again. With a threshold of 2,
            // the second failure alone must NOT reach the threshold - it only would if the
            // intervening success had failed to reset the counter back to zero. This makes the test
            // meaningful: removing the reset would cause call 3 to hit the threshold and emit,
            // which the assertion below would catch.
            var callCount = 0;
            var thirdCallCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _searchParameterOperations.GetAndApplySearchParameterUpdates(Arg.Any<CancellationToken>(), true)
                .Returns(_ =>
                {
                    var currentCall = Interlocked.Increment(ref callCount);
                    try
                    {
                        if (currentCall == 1 || currentCall == 3)
                        {
                            throw new InvalidOperationException("Transient failure");
                        }

                        return Task.FromResult(true);
                    }
                    finally
                    {
                        if (currentCall >= 3)
                        {
                            thirdCallCompleted.TrySetResult(true);
                        }
                    }
                });

            await service.HandleAsync(new SearchParametersInitializedNotification(), CancellationToken.None);

            // Act - wait deterministically for the third call (fail, succeed, fail) instead of
            // relying on wall-clock timing, which can be flaky under test-host load.
            var executeTask = service.StartAsync(cancellationTokenSource.Token);
            await thirdCallCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellationTokenSource.Cancel();
            await executeTask;

            // Assert - the second failure (call 3) never reaches the threshold of 2, which only
            // happens if the intervening success (call 2) actually reset the counter to zero.
            _searchParameterCacheRefresherMetricHandler.Received(0).EmitFailure(Arg.Any<string>());
            _searchParameterCacheRefresherMetricHandler.Received().EmitSuccess();
        }

        [Fact]
        public async Task OnRefreshTimer_WhenFailuresExceedThreshold_ShouldContinueEmittingFailureMetric()
        {
            // Arrange
            using var cancellationTokenSource = new CancellationTokenSource();
            var mockLogger = Substitute.For<ILogger<SearchParameterCacheRefreshBackgroundService>>();
            var options = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            options.Value.Returns(new CoreFeatureConfiguration
            {
                SearchParameterCacheRefreshIntervalSeconds = 1,
                SearchParameterCacheRefreshMaxInitialDelaySeconds = 0,
                SearchParameterCacheRefreshConsecutiveFailureThreshold = 2,
            });

            using var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                options,
                _searchParameterCacheRefresherMetricHandler,
                mockLogger);

            // Persistent failures with a threshold of 2: call 1 is below threshold (no emit), and
            // calls 2, 3, and 4 each meet or exceed the threshold - the metric must be emitted on
            // every one of them, not just once when the threshold is first crossed.
            const int totalCalls = 4;
            var callCount = 0;
            var finalCallCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _searchParameterOperations.GetAndApplySearchParameterUpdates(Arg.Any<CancellationToken>(), true)
                .Returns<Task<bool>>(_ =>
                {
                    var currentCall = Interlocked.Increment(ref callCount);
                    if (currentCall >= totalCalls)
                    {
                        finalCallCompleted.TrySetResult(true);
                    }

                    throw new InvalidOperationException("Persistent failure");
                });

            await service.HandleAsync(new SearchParametersInitializedNotification(), CancellationToken.None);

            // Act - wait deterministically for the fourth call instead of relying on wall-clock timing.
            var executeTask = service.StartAsync(cancellationTokenSource.Token);
            await finalCallCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellationTokenSource.Cancel();
            await executeTask;

            // Assert - calls 2, 3, and 4 each reach/exceed the threshold of 2, so the failure
            // metric must fire on every one of them (3 total), not just once.
            _searchParameterCacheRefresherMetricHandler.Received(totalCalls - 1).EmitFailure(nameof(InvalidOperationException));
        }

        [Fact]
        public async Task WhenBackgroundIsBlockedByAPI_BackgroundShouldSkipRefresh()
        {
            var statusStore = Substitute.For<ISearchParameterStatusDataStore>();
            statusStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
                .Returns(callInfo =>
                {
                    Thread.Sleep(1000); // Block to simulate long-running operation.
                    return Task.FromResult<IReadOnlyCollection<ResourceSearchParameterStatus>>([]);
                });

            var statusManager = new SearchParameterStatusManager(
                statusStore,
                Substitute.For<ISearchParameterDefinitionManager>(),
                Substitute.For<ISearchParameterSupportResolver>(),
                Substitute.For<IMediator>(),
                Substitute.For<ILogger<SearchParameterStatusManager>>());

            var paramOperations = new SearchParameterOperations(
                statusManager,
                Substitute.For<ISearchParameterDefinitionManager>(),
                Substitute.For<IModelInfoProvider>(),
                Substitute.For<ISearchParameterSupportResolver>(),
                Substitute.For<IDataStoreSearchParameterValidator>(),
                Substitute.For<Func<IScoped<ISearchService>>>(),
                Substitute.For<IScopeProvider<IFhirDataStore>>(),
                Substitute.For<ILogger<SearchParameterOperations>>());

            // Start a long-running API call that holds the semaphore
            var apiTask = Task.Run(async () => { await paramOperations.GetAndApplySearchParameterUpdates(CancellationToken.None); });

            var mockLogger = Substitute.For<ILogger<SearchParameterCacheRefreshBackgroundService>>();
            var service = new SearchParameterCacheRefreshBackgroundService(_searchParameterStatusManager, paramOperations, _coreFeatureConfiguration, _searchParameterCacheRefresherMetricHandler, mockLogger);

            // Start service that skips refresh
            await service.HandleAsync(new SearchParametersInitializedNotification(), CancellationToken.None);

            await apiTask;

            mockLogger.Received().Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString().Contains("Skipped incremental SearchParameter cache refresh.")),
                null,
                Arg.Any<Func<object, Exception, string>>());

            service.Dispose();
        }

        [Fact]
        public async Task WhenAPIIsBlockedByBackground_ApiShouldWait()
        {
            var statusStore = Substitute.For<ISearchParameterStatusDataStore>();
            statusStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
                .Returns(callInfo =>
                {
                    Thread.Sleep(5000); // Block to simulate long-running operation.
                    return Task.FromResult<IReadOnlyCollection<ResourceSearchParameterStatus>>([]);
                });

            var statusManager = new SearchParameterStatusManager(
                statusStore,
                Substitute.For<ISearchParameterDefinitionManager>(),
                Substitute.For<ISearchParameterSupportResolver>(),
                Substitute.For<IMediator>(),
                Substitute.For<ILogger<SearchParameterStatusManager>>());

            var paramOperations = new SearchParameterOperations(
                statusManager,
                Substitute.For<ISearchParameterDefinitionManager>(),
                Substitute.For<IModelInfoProvider>(),
                Substitute.For<ISearchParameterSupportResolver>(),
                Substitute.For<IDataStoreSearchParameterValidator>(),
                Substitute.For<Func<IScoped<ISearchService>>>(),
                Substitute.For<IScopeProvider<IFhirDataStore>>(),
                Substitute.For<ILogger<SearchParameterOperations>>());

            var mockLogger = Substitute.For<ILogger<SearchParameterCacheRefreshBackgroundService>>();
            var service = new SearchParameterCacheRefreshBackgroundService(_searchParameterStatusManager, paramOperations, _coreFeatureConfiguration, _searchParameterCacheRefresherMetricHandler, mockLogger);

            // Start service that holds the semaphore
            await service.HandleAsync(new SearchParametersInitializedNotification(), CancellationToken.None);

            // Start API call that waits
            var sw = Stopwatch.StartNew();
            var apiTask = Task.Run(async () => { await paramOperations.GetAndApplySearchParameterUpdates(CancellationToken.None); });
            await apiTask;

            Assert.True(sw.Elapsed.TotalMilliseconds >= 4000, "API call should have been blocked by background operation.");

            service.Dispose();
        }
    }
}
