// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.SearchParameterState;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.SearchParameterState;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.SearchParameterState
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SearchParameterStatus)]
    public class SearchParameterStateHandlerTests
    {
        private static readonly string ResourceId = "http://hl7.org/fhir/SearchParameter/Resource-id";
        private static readonly string ResourceLastUpdated = "http://hl7.org/fhir/SearchParameter/Resource-lastUpdated";
        private static readonly string ResourceProfile = "http://hl7.org/fhir/SearchParameter/Resource-profile";
        private static readonly string ResourceSecurity = "http://hl7.org/fhir/SearchParameter/Resource-security";
        private static readonly string ResourceQuery = "http://hl7.org/fhir/SearchParameter/Resource-query";
        private static readonly string ResourceTest = "http://hl7.org/fhir/SearchParameter/Resource-test";
        private static readonly string PatientPreExisting2 = "http://test/Patient-preexisting2";
        private static readonly string PatientLastUpdated = "http://test/Patient-lastupdated";

        private readonly IAuthorizationService<DataActions> _authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
        private readonly SearchParameterStateHandler _searchParameterHandler;
        private readonly SearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;
        private readonly CancellationToken _cancellationToken;

        private readonly ISearchParameterStatusDataStore _searchParameterStatusDataStore = Substitute.For<ISearchParameterStatusDataStore>();
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly ILogger<SearchParameterStatusManager> _logger = Substitute.For<ILogger<SearchParameterStatusManager>>();
        private ISearchService _searchService = Substitute.For<ISearchService>();

        private const string HttpGetName = "GET";
        private const string HttpPostName = "POST";

        public SearchParameterStateHandlerTests()
        {
            _searchParameterDefinitionManager = new SearchParameterDefinitionManager(ModelInfoProvider.Instance, _mediator, () => _searchService.CreateMockScope(), NullLogger<SearchParameterDefinitionManager>.Instance);
            _searchParameterStatusManager = new SearchParameterStatusManager(_searchParameterStatusDataStore, _searchParameterDefinitionManager, _searchParameterSupportResolver, _mediator, _logger);
            _searchParameterHandler = new SearchParameterStateHandler(_authorizationService, _searchParameterDefinitionManager, _searchParameterStatusManager);
            _cancellationToken = CancellationToken.None;

            _authorizationService.CheckAccess(DataActions.Read, _cancellationToken).Returns(DataActions.Read);
            var searchParamDefinitionStore = new List<SearchParameterInfo>
            {
                new SearchParameterInfo(
                    "Resource",
                    "id",
                    ValueSets.SearchParamType.Token,
                    new Uri(ResourceId),
                    new List<SearchParameterComponentInfo> { new SearchParameterComponentInfo(new Uri(ResourceId)) }),
                new SearchParameterInfo(
                    "Resource",
                    "lastUpdated",
                    ValueSets.SearchParamType.Token,
                    new Uri(ResourceLastUpdated),
                    new List<SearchParameterComponentInfo> { new SearchParameterComponentInfo(new Uri(ResourceLastUpdated)) }),
                new SearchParameterInfo(
                    "Resource",
                    "profile",
                    ValueSets.SearchParamType.Token,
                    new Uri(ResourceProfile),
                    new List<SearchParameterComponentInfo> { new SearchParameterComponentInfo(new Uri(ResourceProfile)) }),
                new SearchParameterInfo(
                    "Resource",
                    "security",
                    ValueSets.SearchParamType.Token,
                    new Uri(ResourceSecurity),
                    new List<SearchParameterComponentInfo> { new SearchParameterComponentInfo(new Uri(ResourceSecurity)) }),
                new SearchParameterInfo(
                    "Resource",
                    "query",
                    ValueSets.SearchParamType.Token,
                    new Uri(ResourceQuery),
                    new List<SearchParameterComponentInfo> { new SearchParameterComponentInfo(new Uri(ResourceQuery)) }),
                new SearchParameterInfo(
                    "Resource",
                    "test",
                    ValueSets.SearchParamType.Token,
                    new Uri(ResourceTest),
                    new List<SearchParameterComponentInfo> { new SearchParameterComponentInfo(new Uri(ResourceTest)) }),
                new SearchParameterInfo(
                    "Patient",
                    "preexisting2",
                    ValueSets.SearchParamType.Token,
                    new Uri(PatientPreExisting2),
                    new List<SearchParameterComponentInfo> { new SearchParameterComponentInfo(new Uri(PatientPreExisting2)) }),
                new SearchParameterInfo(
                    "Patient",
                    "lastUpdated",
                    ValueSets.SearchParamType.Token,
                    new Uri(PatientLastUpdated),
                    new List<SearchParameterComponentInfo> { new SearchParameterComponentInfo(new Uri(PatientLastUpdated)) }),
            };

            _searchParameterStatusDataStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>())
                .Returns(new[]
                {
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Enabled,
                        Uri = new Uri(ResourceId),
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.PendingDisable,
                        Uri = new Uri(ResourceLastUpdated),
                        IsPartiallySupported = true,
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Disabled,
                        Uri = new Uri(ResourceProfile),
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Supported,
                        Uri = new Uri(ResourceSecurity),
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Enabled,
                        Uri = new Uri(PatientPreExisting2),
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.PendingDelete,
                        Uri = new Uri(ResourceQuery),
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Deleted,
                        Uri = new Uri(ResourceTest),
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Deleted,
                        Uri = new Uri(PatientLastUpdated),
                    },
                });
            ConcurrentDictionary<string, SearchParameterInfo> urlLookup = new ConcurrentDictionary<string, SearchParameterInfo>(searchParamDefinitionStore.ToDictionary(x => x.Url.ToString()));
            _searchParameterDefinitionManager.UrlLookup = urlLookup;
            ConcurrentDictionary<string, ConcurrentDictionary<string, SearchParameterInfo>> typeLookup = new ConcurrentDictionary<string, ConcurrentDictionary<string, SearchParameterInfo>>();
            typeLookup.GetOrAdd("Resource", new ConcurrentDictionary<string, SearchParameterInfo>(searchParamDefinitionStore.Where(sp => sp.Name == "Resource").ToDictionary(x => x.Code.ToString())));
            typeLookup.GetOrAdd("Patient", new ConcurrentDictionary<string, SearchParameterInfo>(searchParamDefinitionStore.Where(sp => sp.Name == "Patient").ToDictionary(x => x.Code.ToString())));
            _searchParameterDefinitionManager.TypeLookup = typeLookup;
        }

        [Fact]
        public async void GivenARequestWithNoQueries_WhenHandling_ThenAllSearchParametersAreReturned()
        {
            // Arrange
            var request = new SearchParameterStateRequest(new List<Tuple<string, string>>());

            // Act
            var response = await _searchParameterHandler.Handle(request, _cancellationToken);

            // Assert
            Assert.NotNull(response);
            Parameters result = response.SearchParameters.ToPoco<Parameters>();
            Assert.Equal(8, result.Parameter.Count);
            Assert.True(result.Parameter.Where(p => p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Url && pt.Value.ToString() == ResourceId).Any() && p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Status && pt.Value.ToString() == SearchParameterStatus.Enabled.ToString()).Any()).Any());
            Assert.True(result.Parameter.Where(p => p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Url && pt.Value.ToString() == ResourceQuery).Any() && p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Status && pt.Value.ToString() == SearchParameterStatus.PendingDelete.ToString()).Any()).Any());
            Assert.True(result.Parameter.Where(p => p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Url && pt.Value.ToString() == ResourceLastUpdated).Any() && p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Status && pt.Value.ToString() == SearchParameterStatus.PendingDisable.ToString()).Any()).Any());
            Assert.True(result.Parameter.Where(p => p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Url && pt.Value.ToString() == ResourceProfile).Any() && p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Status && pt.Value.ToString() == SearchParameterStatus.Disabled.ToString()).Any()).Any());
            Assert.True(result.Parameter.Where(p => p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Url && pt.Value.ToString() == ResourceSecurity).Any() && p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Status && pt.Value.ToString() == SearchParameterStatus.Supported.ToString()).Any()).Any());
            Assert.True(result.Parameter.Where(p => p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Url && pt.Value.ToString() == ResourceTest).Any() && p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Status && pt.Value.ToString() == SearchParameterStatus.Deleted.ToString()).Any()).Any());
            Assert.True(result.Parameter.Where(p => p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Url && pt.Value.ToString() == PatientPreExisting2).Any() && p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Status && pt.Value.ToString() == SearchParameterStatus.Enabled.ToString()).Any()).Any());
            Assert.True(result.Parameter.Where(p => p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Url && pt.Value.ToString() == PatientLastUpdated).Any() && p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Status && pt.Value.ToString() == SearchParameterStatus.Deleted.ToString()).Any()).Any());
        }

        [Fact]
        public async void GivenARequestWithAValidUrl_WhenHandling_ThenSearchParameterMatchingUrlIsReturned()
        {
            // Arrange
            var request = new SearchParameterStateRequest(new List<Tuple<string, string>>() { new Tuple<string, string>(SearchParameterStateProperties.Url, ResourceId) });

            // Act
            var response = await _searchParameterHandler.Handle(request, _cancellationToken);

            // Assert
            Assert.NotNull(response);
            Assert.NotNull(response.SearchParameters);
            Parameters result = response.SearchParameters.ToPoco<Parameters>();
            Assert.Single(result.Parameter);
            Assert.True(result.Parameter.Where(p => p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Url && pt.Value.ToString() == ResourceId).Any() && p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Status && pt.Value.ToString() == SearchParameterStatus.Enabled.ToString()).Any()).Any());
        }

        [Fact]
        public async void GivenARequestWithAValidCode_WhenHandling_ThenSearchParameterMatchingCodeIsReturned()
        {
            // Arrange
            var request = new SearchParameterStateRequest(new List<Tuple<string, string>>() { new Tuple<string, string>(SearchParameterStateProperties.Code, "lastUpdated") });

            // Act
            var response = await _searchParameterHandler.Handle(request, _cancellationToken);

            // Assert
            Assert.NotNull(response);
            Assert.NotNull(response.SearchParameters);
            Parameters result = response.SearchParameters.ToPoco<Parameters>();
            Assert.Equal(2, result.Parameter.Count);
            Assert.True(result.Parameter.Where(p => p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Url && pt.Value.ToString() == ResourceLastUpdated).Any() && p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Status && pt.Value.ToString() == SearchParameterStatus.PendingDisable.ToString()).Any()).Any());
            Assert.True(result.Parameter.Where(p => p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Url && pt.Value.ToString() == PatientLastUpdated).Any() && p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Status && pt.Value.ToString() == SearchParameterStatus.Deleted.ToString()).Any()).Any());
        }

        [Fact]
        public async void GivenARequestWithAValidResourceType_WhenHandling_ThenSearchParametersMatchingResourceTypeAreReturned()
        {
            // Arrange
            var request = new SearchParameterStateRequest(new List<Tuple<string, string>>() { new Tuple<string, string>(SearchParameterStateProperties.ResourceType, "Resource") });

            // Act
            var response = await _searchParameterHandler.Handle(request, _cancellationToken);

            // Assert
            Assert.NotNull(response);
            Parameters result = response.SearchParameters.ToPoco<Parameters>();
            Assert.Equal(6, result.Parameter.Count);
            Assert.True(result.Parameter.Where(p => p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Url && pt.Value.ToString() == ResourceId).Any() && p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Status && pt.Value.ToString() == SearchParameterStatus.Enabled.ToString()).Any()).Any());
            Assert.True(result.Parameter.Where(p => p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Url && pt.Value.ToString() == ResourceQuery).Any() && p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Status && pt.Value.ToString() == SearchParameterStatus.PendingDelete.ToString()).Any()).Any());
            Assert.True(result.Parameter.Where(p => p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Url && pt.Value.ToString() == ResourceLastUpdated).Any() && p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Status && pt.Value.ToString() == SearchParameterStatus.PendingDisable.ToString()).Any()).Any());
            Assert.True(result.Parameter.Where(p => p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Url && pt.Value.ToString() == ResourceProfile).Any() && p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Status && pt.Value.ToString() == SearchParameterStatus.Disabled.ToString()).Any()).Any());
            Assert.True(result.Parameter.Where(p => p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Url && pt.Value.ToString() == ResourceSecurity).Any() && p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Status && pt.Value.ToString() == SearchParameterStatus.Supported.ToString()).Any()).Any());
            Assert.True(result.Parameter.Where(p => p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Url && pt.Value.ToString() == ResourceTest).Any() && p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Status && pt.Value.ToString() == SearchParameterStatus.Deleted.ToString()).Any()).Any());
        }

        [Fact]
        public async void GivenARequestWithAValidCodeAndResourceType_WhenHandling_ThenSearchParameterMatchingCodeAndResourceTypeIsReturned()
        {
            // Arrange
            var request = new SearchParameterStateRequest(new List<Tuple<string, string>>() { new Tuple<string, string>(SearchParameterStateProperties.Code, "lastUpdated"), new Tuple<string, string>(SearchParameterStateProperties.ResourceType, "Resource") });

            // Act
            var response = await _searchParameterHandler.Handle(request, _cancellationToken);

            // Assert
            Assert.NotNull(response);
            Assert.NotNull(response.SearchParameters);
            Parameters result = response.SearchParameters.ToPoco<Parameters>();
            Assert.Single(result.Parameter);
            Assert.True(result.Parameter.Where(p => p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Url && pt.Value.ToString() == ResourceLastUpdated).Any() && p.Part.Where(pt => pt.Name == SearchParameterStateProperties.Status && pt.Value.ToString() == SearchParameterStatus.PendingDisable.ToString()).Any()).Any());
        }

        [Fact]
        public async void GivenARequestWithAValidCodeAndResourceTypeThatDonotHaveMatchinSearchParameters_WhenHandling_ThenNoSearchParametersAreReturned()
        {
            // Arrange
            var request = new SearchParameterStateRequest(new List<Tuple<string, string>>() { new Tuple<string, string>(SearchParameterStateProperties.Code, "lastUpdated"), new Tuple<string, string>(SearchParameterStateProperties.ResourceType, "nomatch") });

            // Act
            // Assert
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => _searchParameterHandler.Handle(request, _cancellationToken));
        }
    }
}
