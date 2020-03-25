// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Registry
{
    public class SearchParameterStatusManagerTests
    {
        private static readonly string ResourceId = "http://hl7.org/fhir/SearchParameter/Resource-id";
        private static readonly string ResourceLastupdated = "http://hl7.org/fhir/SearchParameter/Resource-lastUpdated";
        private static readonly string ResourceProfile = "http://hl7.org/fhir/SearchParameter/Resource-profile";
        private static readonly string ResourceSecurity = "http://hl7.org/fhir/SearchParameter/Resource-security";
        private static readonly string ResourceQuery = "http://hl7.org/fhir/SearchParameter/Resource-query";

        private readonly SearchParameterStatusManager _manager;
        private readonly ISearchParameterRegistry _searchParameterRegistry;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly IMediator _mediator;
        private readonly SearchParameterInfo[] _searchParameterInfos;
        private readonly SearchParameterInfo _queryParameter;

        public SearchParameterStatusManagerTests()
        {
            _searchParameterRegistry = Substitute.For<ISearchParameterRegistry>();
            _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            _mediator = Substitute.For<IMediator>();

            _manager = new SearchParameterStatusManager(
                _searchParameterRegistry,
                _searchParameterDefinitionManager,
                _mediator);

            _searchParameterRegistry.GetSearchParameterStatuses()
                .Returns(new[]
                {
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Enabled,
                        Uri = new Uri(ResourceId),
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Enabled,
                        Uri = new Uri(ResourceLastupdated),
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
                });

            _queryParameter = new SearchParameterInfo("_query", SearchParamType.Token, new Uri(ResourceQuery));
            _searchParameterInfos = new[]
            {
                new SearchParameterInfo("_id", SearchParamType.Token, new Uri(ResourceId)),
                new SearchParameterInfo("_lastUpdated", SearchParamType.Token, new Uri(ResourceLastupdated)),
                new SearchParameterInfo("_profile", SearchParamType.Token, new Uri(ResourceProfile)),
                new SearchParameterInfo("_security", SearchParamType.Token, new Uri(ResourceSecurity)),
                _queryParameter,
            };

            _searchParameterDefinitionManager.GetSearchParameters("Account")
                .Returns(_searchParameterInfos);

            _searchParameterDefinitionManager.AllSearchParameters
                .Returns(_searchParameterInfos);

            _searchParameterDefinitionManager.GetSearchParameter(new Uri(ResourceQuery))
                .Returns(_queryParameter);
        }

        [Fact]
        public async Task GivenASPStatusManager_WhenInitializing_ThenSearchParameterIsUpdatedFromRegistry()
        {
            await _manager.EnsureInitialized();

            var list = _searchParameterDefinitionManager.GetSearchParameters("Account").ToList();

            Assert.True(list[0].IsSearchable);
            Assert.True(list[0].IsSupported);
            Assert.Null(list[0].IsPartiallySupported);

            Assert.True(list[1].IsSearchable);
            Assert.True(list[1].IsSupported);
            Assert.True(list[1].IsPartiallySupported);

            Assert.False(list[2].IsSearchable);
            Assert.False(list[2].IsSupported);
            Assert.Null(list[2].IsPartiallySupported);

            Assert.False(list[3].IsSearchable);
            Assert.True(list[3].IsSupported);
            Assert.Null(list[3].IsPartiallySupported);

            Assert.False(list[4].IsSearchable);
            Assert.True(list[4].IsSupported);
            Assert.Null(list[4].IsPartiallySupported);
        }

        [Fact]
        public async Task GivenASPStatusManager_WhenInitializing_ThenUpdatedSearchParametersInNotification()
        {
            await _manager.EnsureInitialized();

            await _mediator
                .Received()
                .Publish(
                    Arg.Is<SearchParametersUpdated>(x => _searchParameterInfos.Except(x.SearchParameters).Any() == false),
                    Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenASPStatusManager_WhenInitializing_ThenRegistryShouldGetNewlyFoundParameters()
        {
            await _manager.EnsureInitialized();

            await _searchParameterRegistry
                .Received()
                .UpdateStatuses(Arg.Is<IEnumerable<ResourceSearchParameterStatus>>(x => x.Single().Uri == _queryParameter.Url));
        }
    }
}
