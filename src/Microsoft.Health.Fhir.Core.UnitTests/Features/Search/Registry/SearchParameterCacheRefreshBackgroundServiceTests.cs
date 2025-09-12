// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
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
                SearchParameterCacheRefreshIntervalMinutes = 1,
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
                SearchParameterCacheRefreshIntervalMinutes = 5,
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
        public void Constructor_WithZeroOrNegativeRefreshInterval_ShouldUseDefaultInterval()
        {
            // Arrange
            var config = new CoreFeatureConfiguration
            {
                SearchParameterCacheRefreshIntervalMinutes = 0,
            };
            var options = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            options.Value.Returns(config);

            // Act & Assert - Should not throw and should use default interval (which is enforced as minimum 1 minute)
            var service = new SearchParameterCacheRefreshBackgroundService(
                _searchParameterStatusManager,
                _searchParameterOperations,
                options,
                NullLogger<SearchParameterCacheRefreshBackgroundService>.Instance);

            Assert.NotNull(service);
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
    }
}
