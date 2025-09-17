// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Notifications;
using Microsoft.Health.Fhir.Core.Features.Notifications.Models;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Registry
{
    public class SearchParameterStatusManagerWithNotificationsTests
    {
        private readonly ISearchParameterStatusDataStore _searchParameterStatusDataStore = Substitute.For<ISearchParameterStatusDataStore>();
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();
        private readonly INotificationService _notificationService = Substitute.For<INotificationService>();
        private readonly IUnifiedNotificationPublisher _unifiedPublisher = Substitute.For<IUnifiedNotificationPublisher>();
        private readonly ILogger<SearchParameterStatusManager> _logger = Substitute.For<ILogger<SearchParameterStatusManager>>();
        private readonly SearchParameterStatusManager _manager;

        public SearchParameterStatusManagerWithNotificationsTests()
        {
            _manager = new SearchParameterStatusManager(
                _searchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                _searchParameterSupportResolver,
                _notificationService,
                _unifiedPublisher,
                _logger);
        }

        [Fact]
        public async Task UpdateSearchParameterStatusAsync_WithUnifiedPublisher_ShouldPublishNotification()
        {
            // Arrange
            var searchParameterUris = new List<string> { "http://hl7.org/fhir/SearchParameter/test" };
            var searchParameter = new SearchParameterInfo("test", "test", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/test"));

            _searchParameterDefinitionManager.GetSearchParameter(Arg.Any<string>()).Returns(searchParameter);
            _searchParameterStatusDataStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>())
                .Returns(new List<ResourceSearchParameterStatus>());

            // Act
            await _manager.UpdateSearchParameterStatusAsync(searchParameterUris, SearchParameterStatus.Enabled, CancellationToken.None);

            // Assert
            await _unifiedPublisher.Received(1).PublishAsync(
                Arg.Any<SearchParametersUpdatedNotification>(),
                true,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task AddSearchParameterStatusAsync_WithUnifiedPublisher_ShouldPublishNotification()
        {
            // Arrange
            var searchParameterUris = new List<string> { "http://hl7.org/fhir/SearchParameter/test" };
            var searchParameter = new SearchParameterInfo("test", "test", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/test"));

            _searchParameterDefinitionManager.GetSearchParameter(Arg.Any<string>()).Returns(searchParameter);
            _searchParameterStatusDataStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>())
                .Returns(new List<ResourceSearchParameterStatus>());

            // Act
            await _manager.AddSearchParameterStatusAsync(searchParameterUris, CancellationToken.None);

            // Assert
            await _unifiedPublisher.Received(1).PublishAsync(
                Arg.Any<SearchParametersUpdatedNotification>(),
                true,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task DeleteSearchParameterStatusAsync_WithUnifiedPublisher_ShouldPublishNotification()
        {
            // Arrange
            var searchParameterUri = "http://hl7.org/fhir/SearchParameter/test";
            var searchParameter = new SearchParameterInfo("test", "test", SearchParamType.String, new Uri(searchParameterUri));

            _searchParameterDefinitionManager.GetSearchParameter(Arg.Any<string>()).Returns(searchParameter);
            _searchParameterStatusDataStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>())
                .Returns(new List<ResourceSearchParameterStatus>());

            // Act
            await _manager.DeleteSearchParameterStatusAsync(searchParameterUri, CancellationToken.None);

            // Assert
            await _unifiedPublisher.Received(1).PublishAsync(
                Arg.Any<SearchParametersUpdatedNotification>(),
                true,
                Arg.Any<CancellationToken>());
        }
    }
}
