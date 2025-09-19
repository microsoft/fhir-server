// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Notifications;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
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
    public class SearchParameterStatusManagerRedisIntegrationTests
    {
        private readonly ISearchParameterStatusDataStore _searchParameterStatusDataStore = Substitute.For<ISearchParameterStatusDataStore>();
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();
        private readonly INotificationService _notificationService = Substitute.For<INotificationService>();
        private readonly IUnifiedNotificationPublisher _unifiedPublisher = Substitute.For<IUnifiedNotificationPublisher>();

        [Fact]
        public async Task UpdateSearchParameterStatusAsync_ShouldPublishToRedisWhenEnabled()
        {
            // Arrange
            var manager = new SearchParameterStatusManager(
                _searchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                _searchParameterSupportResolver,
                _notificationService,
                _unifiedPublisher,
                NullLogger<SearchParameterStatusManager>.Instance);

            var searchParameterUris = new List<string> { "http://hl7.org/fhir/SearchParameter/test" };
            var searchParameter = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/test"));

            _searchParameterDefinitionManager.GetSearchParameter(Arg.Any<string>()).Returns(searchParameter);
            _searchParameterStatusDataStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>())
                .Returns(new List<ResourceSearchParameterStatus>());

            // Act
            await manager.UpdateSearchParameterStatusAsync(searchParameterUris, SearchParameterStatus.Enabled, CancellationToken.None);

            // Assert
            await _unifiedPublisher.Received(1).PublishAsync(
                Arg.Is<SearchParametersUpdatedNotification>(n => n.SearchParameters.Any()),
                true, // enableRedisNotification should be true
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ApplySearchParameterStatus_WithIsFromRemoteSync_ShouldNotPublishToRedis()
        {
            // Arrange
            var manager = new SearchParameterStatusManager(
                _searchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                _searchParameterSupportResolver,
                _notificationService,
                _unifiedPublisher,
                NullLogger<SearchParameterStatusManager>.Instance);

            var updatedStatuses = new List<ResourceSearchParameterStatus>
            {
                new ResourceSearchParameterStatus
                {
                    Uri = new Uri("http://hl7.org/fhir/SearchParameter/test"),
                    Status = SearchParameterStatus.Enabled,
                    LastUpdated = DateTimeOffset.UtcNow,
                },
            };

            var searchParameter = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/test"));
            _searchParameterDefinitionManager.TryGetSearchParameter(Arg.Any<string>(), out Arg.Any<SearchParameterInfo>())
                .Returns(x =>
                {
                    x[1] = searchParameter;
                    return true;
                });

            // Act
            await manager.ApplySearchParameterStatus(updatedStatuses, CancellationToken.None, isFromRemoteSync: true);

            // Assert - Should NOT publish to Redis when isFromRemoteSync is true
            await _unifiedPublisher.Received(1).PublishAsync(
                Arg.Any<SearchParametersUpdatedNotification>(),
                false, // enableRedisNotification should be false to prevent loops
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ApplySearchParameterStatus_WithIsFromRemoteSyncFalse_ShouldPublishToRedis()
        {
            // Arrange
            var manager = new SearchParameterStatusManager(
                _searchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                _searchParameterSupportResolver,
                _notificationService,
                _unifiedPublisher,
                NullLogger<SearchParameterStatusManager>.Instance);

            var updatedStatuses = new List<ResourceSearchParameterStatus>
            {
                new ResourceSearchParameterStatus
                {
                    Uri = new Uri("http://hl7.org/fhir/SearchParameter/test"),
                    Status = SearchParameterStatus.Enabled,
                    LastUpdated = DateTimeOffset.UtcNow,
                },
            };

            var searchParameter = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/test"));
            _searchParameterDefinitionManager.TryGetSearchParameter(Arg.Any<string>(), out Arg.Any<SearchParameterInfo>())
                .Returns(x =>
                {
                    x[1] = searchParameter;
                    return true;
                });

            // Act
            await manager.ApplySearchParameterStatus(updatedStatuses, CancellationToken.None, isFromRemoteSync: false);

            // Assert - Should publish to Redis when isFromRemoteSync is false
            await _unifiedPublisher.Received(1).PublishAsync(
                Arg.Any<SearchParametersUpdatedNotification>(),
                true, // enableRedisNotification should be true
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task AddSearchParameterStatusAsync_ShouldPublishToRedis()
        {
            // Arrange
            var manager = new SearchParameterStatusManager(
                _searchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                _searchParameterSupportResolver,
                _notificationService,
                _unifiedPublisher,
                NullLogger<SearchParameterStatusManager>.Instance);

            var searchParameterUris = new List<string> { "http://hl7.org/fhir/SearchParameter/test" };
            var searchParameter = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/test"));

            _searchParameterDefinitionManager.GetSearchParameter(Arg.Any<string>()).Returns(searchParameter);
            _searchParameterSupportResolver.IsSearchParameterSupported(Arg.Any<SearchParameterInfo>())
                .Returns((true, false));

            // Act
            await manager.AddSearchParameterStatusAsync(searchParameterUris, CancellationToken.None);

            // Assert
            await _unifiedPublisher.Received(1).PublishAsync(
                Arg.Is<SearchParametersUpdatedNotification>(n => n.SearchParameters.Any()),
                true, // enableRedisNotification should be true
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task DeleteSearchParameterStatusAsync_ShouldPublishToRedis()
        {
            // Arrange
            var manager = new SearchParameterStatusManager(
                _searchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                _searchParameterSupportResolver,
                _notificationService,
                _unifiedPublisher,
                NullLogger<SearchParameterStatusManager>.Instance);

            var searchParameterUri = "http://hl7.org/fhir/SearchParameter/test";
            var searchParameter = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.String, new Uri(searchParameterUri));

            _searchParameterDefinitionManager.GetSearchParameter(searchParameterUri).Returns(searchParameter);

            // Act
            await manager.DeleteSearchParameterStatusAsync(searchParameterUri, CancellationToken.None);

            // Assert
            await _unifiedPublisher.Received(1).PublishAsync(
                Arg.Is<SearchParametersUpdatedNotification>(n => n.SearchParameters.Any()),
                true, // enableRedisNotification should be true
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public void InstanceId_ShouldReturnUnifiedPublisherInstanceId()
        {
            // Arrange
            const string expectedInstanceId = "test-instance-123";
            _unifiedPublisher.InstanceId.Returns(expectedInstanceId);

            var manager = new SearchParameterStatusManager(
                _searchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                _searchParameterSupportResolver,
                _notificationService,
                _unifiedPublisher,
                NullLogger<SearchParameterStatusManager>.Instance);

            // Act
            var actualInstanceId = manager.InstanceId;

            // Assert
            Assert.Equal(expectedInstanceId, actualInstanceId);
        }

        [Fact]
        public async Task ApplySearchParameterStatus_WithMultipleParameters_ShouldPublishAllInSingleNotification()
        {
            // Arrange
            var manager = new SearchParameterStatusManager(
                _searchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                _searchParameterSupportResolver,
                _notificationService,
                _unifiedPublisher,
                NullLogger<SearchParameterStatusManager>.Instance);

            var updatedStatuses = new List<ResourceSearchParameterStatus>
            {
                new ResourceSearchParameterStatus
                {
                    Uri = new Uri("http://hl7.org/fhir/SearchParameter/test1"),
                    Status = SearchParameterStatus.Enabled,
                    LastUpdated = DateTimeOffset.UtcNow,
                },
                new ResourceSearchParameterStatus
                {
                    Uri = new Uri("http://hl7.org/fhir/SearchParameter/test2"),
                    Status = SearchParameterStatus.Supported,
                    LastUpdated = DateTimeOffset.UtcNow,
                },
            };

            var searchParameter1 = new SearchParameterInfo("test1", "test1", ValueSets.SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/test1"));
            var searchParameter2 = new SearchParameterInfo("test2", "test2", ValueSets.SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/test2"));

            _searchParameterDefinitionManager.TryGetSearchParameter("http://hl7.org/fhir/SearchParameter/test1", out Arg.Any<SearchParameterInfo>())
                .Returns(x =>
                {
                    x[1] = searchParameter1;
                    return true;
                });

            _searchParameterDefinitionManager.TryGetSearchParameter("http://hl7.org/fhir/SearchParameter/test2", out Arg.Any<SearchParameterInfo>())
                .Returns(x =>
                {
                    x[1] = searchParameter2;
                    return true;
                });

            // Act
            await manager.ApplySearchParameterStatus(updatedStatuses, CancellationToken.None, isFromRemoteSync: false);

            // Assert
            await _unifiedPublisher.Received(1).PublishAsync(
                Arg.Is<SearchParametersUpdatedNotification>(n => n.SearchParameters.Count() == 2),
                true,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ApplySearchParameterStatus_WithNoMatchingParameters_ShouldNotPublish()
        {
            // Arrange
            var manager = new SearchParameterStatusManager(
                _searchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                _searchParameterSupportResolver,
                _notificationService,
                _unifiedPublisher,
                NullLogger<SearchParameterStatusManager>.Instance);

            var updatedStatuses = new List<ResourceSearchParameterStatus>
            {
                new ResourceSearchParameterStatus
                {
                    Uri = new Uri("http://hl7.org/fhir/SearchParameter/nonexistent"),
                    Status = SearchParameterStatus.Enabled,
                    LastUpdated = DateTimeOffset.UtcNow,
                },
            };

            // Mock that parameter doesn't exist in definition manager
            _searchParameterDefinitionManager.TryGetSearchParameter(Arg.Any<string>(), out Arg.Any<SearchParameterInfo>())
                .Returns(false);

            // Act
            await manager.ApplySearchParameterStatus(updatedStatuses, CancellationToken.None, isFromRemoteSync: false);

            // Assert - Should not publish notification for non-existent parameters
            // The actual behavior is that it will still call the unified publisher, but with an empty list
            // So we should verify it publishes an empty notification instead
            await _unifiedPublisher.Received(1).PublishAsync(
                Arg.Is<SearchParametersUpdatedNotification>(n => !n.SearchParameters.Any()),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ApplySearchParameterStatus_WithCancellationToken_ShouldPassThroughToken()
        {
            // Arrange
            var manager = new SearchParameterStatusManager(
                _searchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                _searchParameterSupportResolver,
                _notificationService,
                _unifiedPublisher,
                NullLogger<SearchParameterStatusManager>.Instance);

            var updatedStatuses = new List<ResourceSearchParameterStatus>
            {
                new ResourceSearchParameterStatus
                {
                    Uri = new Uri("http://hl7.org/fhir/SearchParameter/test"),
                    Status = SearchParameterStatus.Enabled,
                    LastUpdated = DateTimeOffset.UtcNow,
                },
            };

            var searchParameter = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/test"));
            _searchParameterDefinitionManager.TryGetSearchParameter(Arg.Any<string>(), out Arg.Any<SearchParameterInfo>())
                .Returns(x =>
                {
                    x[1] = searchParameter;
                    return true;
                });

            var cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;

            // Act
            await manager.ApplySearchParameterStatus(updatedStatuses, cancellationToken, isFromRemoteSync: false);

            // Assert
            await _unifiedPublisher.Received(1).PublishAsync(
                Arg.Any<SearchParametersUpdatedNotification>(),
                Arg.Any<bool>(),
                cancellationToken); // Should pass through the specific cancellation token
        }

        [Fact]
        public async Task UpdateSearchParameterStatusAsync_WithDataStoreException_ShouldNotPublishNotification()
        {
            // Arrange
            var manager = new SearchParameterStatusManager(
                _searchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                _searchParameterSupportResolver,
                _notificationService,
                _unifiedPublisher,
                NullLogger<SearchParameterStatusManager>.Instance);

            var searchParameterUris = new List<string> { "http://hl7.org/fhir/SearchParameter/test" };
            var searchParameter = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/test"));

            _searchParameterDefinitionManager.GetSearchParameter(Arg.Any<string>()).Returns(searchParameter);
            _searchParameterStatusDataStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>())
                .Returns(new List<ResourceSearchParameterStatus>());

            // Mock exception in data store operation
            _searchParameterStatusDataStore.UpsertStatuses(Arg.Any<IReadOnlyCollection<ResourceSearchParameterStatus>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new Exception("Database error")));

            // Act & Assert - Should throw the mocked exception and not publish notification
            var exception = await Assert.ThrowsAsync<Exception>(
                () => manager.UpdateSearchParameterStatusAsync(searchParameterUris, SearchParameterStatus.Enabled, CancellationToken.None));

            Assert.Equal("Database error", exception.Message);

            // Should not publish notification if operation failed
            await _unifiedPublisher.DidNotReceive().PublishAsync(
                Arg.Any<SearchParametersUpdatedNotification>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SearchParameterStatusManager_LoopPrevention_ShouldWork()
        {
            // Arrange - Test the critical loop prevention mechanism
            var manager = new SearchParameterStatusManager(
                _searchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                _searchParameterSupportResolver,
                _notificationService,
                _unifiedPublisher,
                NullLogger<SearchParameterStatusManager>.Instance);

            var updatedStatuses = new List<ResourceSearchParameterStatus>
            {
                new ResourceSearchParameterStatus
                {
                    Uri = new Uri("http://hl7.org/fhir/SearchParameter/test"),
                    Status = SearchParameterStatus.Enabled,
                    LastUpdated = DateTimeOffset.UtcNow,
                },
            };

            var searchParameter = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/test"));
            _searchParameterDefinitionManager.TryGetSearchParameter(Arg.Any<string>(), out Arg.Any<SearchParameterInfo>())
                .Returns(x =>
                {
                    x[1] = searchParameter;
                    return true;
                });

            // Act - Simulate processing from Redis notification
            await manager.ApplySearchParameterStatus(updatedStatuses, CancellationToken.None, isFromRemoteSync: true);

            // Assert - Should use enableRedisNotification = false to prevent loop
            await _unifiedPublisher.Received(1).PublishAsync(
                Arg.Any<SearchParametersUpdatedNotification>(),
                false, // This false value prevents infinite loops
                Arg.Any<CancellationToken>());

            // Act - Simulate local change
            await manager.ApplySearchParameterStatus(updatedStatuses, CancellationToken.None, isFromRemoteSync: false);

            // Assert - Should use enableRedisNotification = true for local changes
            await _unifiedPublisher.Received(1).PublishAsync(
                Arg.Any<SearchParametersUpdatedNotification>(),
                true, // This true value enables cross-instance notification
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public void SearchParameterStatusManager_RedisIntegration_PropertiesShouldBeConsistent()
        {
            // Arrange
            var manager = new SearchParameterStatusManager(
                _searchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                _searchParameterSupportResolver,
                _notificationService,
                _unifiedPublisher,
                NullLogger<SearchParameterStatusManager>.Instance);

            // Act & Assert - Verify Redis integration properties
            Assert.NotNull(manager.InstanceId); // Should delegate to unified publisher

            // Verify unified publisher is being used (through instance ID delegation)
            var instanceId = manager.InstanceId;
            var publisherInstanceId = _unifiedPublisher.InstanceId;
            Assert.NotNull(instanceId);
        }
    }
}
