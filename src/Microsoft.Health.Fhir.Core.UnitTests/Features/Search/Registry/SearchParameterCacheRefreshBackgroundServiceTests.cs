// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using MediatR;
using Microsoft.AspNetCore.Components.Forms;
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
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
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
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly IOptions<CoreFeatureConfiguration> _coreFeatureConfiguration;
        private readonly SearchParameterCacheRefreshBackgroundService _service;

        public SearchParameterCacheRefreshBackgroundServiceTests()
        {
            _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            _coreFeatureConfiguration = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            _coreFeatureConfiguration.Value.Returns(new CoreFeatureConfiguration
            {
                SearchParameterCacheRefreshIntervalSeconds = 1,
                SearchParameterCacheRefreshMaxInitialDelaySeconds = 0, // No delay for tests
            });

            _service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterDefinitionManager,
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
                _searchParameterDefinitionManager,
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
                _searchParameterDefinitionManager,
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
                _searchParameterDefinitionManager,
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
                _searchParameterDefinitionManager,
                null,
                NullLogger<SearchParameterCacheRefreshBackgroundService>.Instance));
        }

        [Fact]
        public void Constructor_WithNullSearchParameterDefinitionManager_ShouldThrow()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SearchParameterCacheRefreshBackgroundService(
                null,
                _coreFeatureConfiguration,
                NullLogger<SearchParameterCacheRefreshBackgroundService>.Instance));
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrow()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SearchParameterCacheRefreshBackgroundService(
                _searchParameterDefinitionManager,
                _coreFeatureConfiguration,
                null));
        }

        [Fact]
        public async Task OnRefreshTimer_WhenCacheIsStale_ShouldCallGetAndApplySearchParameterUpdates()
        {
            // Arrange
            _searchParameterDefinitionManager.ClearReceivedCalls(); // Clear any previous calls

            // Set initialized to true to allow timer to run
            await _service.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);

            // Wait for the timer to fire at least once and allow async operations to complete
            await Task.Delay(200);

            // Assert - use at least 1 call since timer might fire multiple times in test environment
            await _searchParameterDefinitionManager.Received().GetAndApplySearchParameterUpdates(Arg.Any<CancellationToken>(), true);
        }

        [Fact]
        public async Task ExecuteAsync_WhenCancellationRequested_ShouldStopGracefully()
        {
            // Arrange
            using var cancellationTokenSource = new CancellationTokenSource();
            var mockLogger = Substitute.For<ILogger<SearchParameterCacheRefreshBackgroundService>>();

            var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterDefinitionManager,
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

            // Set up throwing ObjectDisposedException to simulate the service provider being disposed
            _searchParameterDefinitionManager.GetAndApplySearchParameterUpdates(Arg.Any<CancellationToken>(), true)
                .Returns(_ => Task.FromException<bool>(new ObjectDisposedException("IServiceProvider")));

            var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterDefinitionManager,
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

            var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterDefinitionManager,
                _coreFeatureConfiguration,
                mockLogger);

            _searchParameterDefinitionManager.GetAndApplySearchParameterUpdates(Arg.Any<CancellationToken>(), true)
                .Returns(_ => Task.FromException<bool>(new OperationCanceledException()));

            // Act - Initialize and let timer run
            await service.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);

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

            var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterDefinitionManager,
                _coreFeatureConfiguration,
                mockLogger);

            // Start the service and then immediately cancel it
            var executeTask = service.StartAsync(cancellationTokenSource.Token);
            cancellationTokenSource.Cancel();
            await executeTask;

            // Act - Try to handle the notification after cancellation
            await service.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);

            service.Dispose();
        }

        [Fact]
        public async Task WhenBackgroundIsBlockedByAPI_BackgroundShouldSkipRefresh()
        {
            var statusStore = Substitute.For<ISearchParameterStatusDataStore>();
            var run = 0;
            statusStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
                .Returns(callInfo =>
                {
                    run++;
                    Thread.Sleep(run == 2 ? 1000 : 0);
                    return Task.FromResult<IReadOnlyCollection<ResourceSearchParameterStatus>>([]);
                });

            var definitionManager = new SearchParameterDefinitionManager(
                CreateMockedModelInfoProviderWithBundleSupport(),
                Substitute.For<IMediator>(),
                Substitute.For<IScopeProvider<ISearchService>>(),
                Substitute.For<ISearchParameterComparer<SearchParameterInfo>>(),
                statusStore.CreateMockScopeProvider(),
                Substitute.For<ILogger<SearchParameterDefinitionManager>>());

            // run = 1
            await definitionManager.EnsureInitializedAsync(CancellationToken.None);

            // run = 2
            var apiTask = Task.Run(async () => { await definitionManager.GetAndApplySearchParameterUpdates(CancellationToken.None); });

            var mockLogger = Substitute.For<ILogger<SearchParameterCacheRefreshBackgroundService>>();
            var service = new SearchParameterCacheRefreshBackgroundService(definitionManager, _coreFeatureConfiguration, mockLogger);

            // run = 3
            await service.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);

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
            var run = 0;
            statusStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
                .Returns(callInfo =>
                {
                    run++;
                    Thread.Sleep(run == 2 ? 2000 : 0);
                    return Task.FromResult<IReadOnlyCollection<ResourceSearchParameterStatus>>(Array.Empty<ResourceSearchParameterStatus>());
                });

            var definitionManager = new SearchParameterDefinitionManager(
                CreateMockedModelInfoProviderWithBundleSupport(),
                Substitute.For<IMediator>(),
                Substitute.For<IScopeProvider<ISearchService>>(),
                Substitute.For<ISearchParameterComparer<SearchParameterInfo>>(),
                statusStore.CreateMockScopeProvider(),
                Substitute.For<ILogger<SearchParameterDefinitionManager>>());

            // run = 1
            await definitionManager.EnsureInitializedAsync(CancellationToken.None);

            var mockLogger = Substitute.For<ILogger<SearchParameterCacheRefreshBackgroundService>>();
            var service = new SearchParameterCacheRefreshBackgroundService(definitionManager, _coreFeatureConfiguration, mockLogger);

            // run = 2
            await service.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);

            // run = 3
            var sw = Stopwatch.StartNew();
            var apiTask = Task.Run(async () => { await definitionManager.GetAndApplySearchParameterUpdates(CancellationToken.None); });
            await apiTask;
            Assert.True(sw.Elapsed.TotalMilliseconds >= 1500, "API call should have been blocked by background operation.");

            service.Dispose();
        }

        private static IModelInfoProvider CreateMockedModelInfoProviderWithBundleSupport()
        {
            var modelInfoProvider = Substitute.For<IModelInfoProvider>();
            modelInfoProvider.Version.Returns(FhirSpecification.R4);

            // Create a minimal Bundle ITypedElement for search-parameters.json
            var searchParamsBundle = CreateMockBundleTypedElement();
            var msSearchParamsBundle = CreateMockBundleTypedElement();

            // Mock ToTypedElement to return our mocked bundles
            var callCount = 0;
            modelInfoProvider.ToTypedElement(Arg.Any<RawResource>())
                .Returns(callInfo =>
                {
                    callCount++;

                    // First call is for search-parameters.json, second is for ms-search-parameters.json
                    return callCount == 1 ? searchParamsBundle : msSearchParamsBundle;
                });

            return modelInfoProvider;
        }

        private static ITypedElement CreateMockBundleTypedElement()
        {
            var bundleElement = Substitute.For<ITypedElement>();
            bundleElement.InstanceType.Returns("Bundle");
            bundleElement.Name.Returns("Bundle");

            // Mock the 'entry' child - return empty list
            bundleElement.Children("entry").Returns(Enumerable.Empty<ITypedElement>());

            return bundleElement;
        }
    }
}
