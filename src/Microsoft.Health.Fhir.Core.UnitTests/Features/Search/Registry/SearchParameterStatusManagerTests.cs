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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Registry
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterStatusManagerTests
    {
        private static readonly string ResourceId = "http://hl7.org/fhir/SearchParameter/Resource-id";
        private static readonly string ResourceLastupdated = "http://hl7.org/fhir/SearchParameter/Resource-lastUpdated";
        private static readonly string ResourceProfile = "http://hl7.org/fhir/SearchParameter/Resource-profile";
        private static readonly string ResourceSecurity = "http://hl7.org/fhir/SearchParameter/Resource-security";
        private static readonly string ResourceQuery = "http://hl7.org/fhir/SearchParameter/Resource-query";

        private readonly SearchParameterStatusManager _manager;
        private readonly ISearchParameterStatusDataStore _searchParameterStatusDataStore;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly IMediator _mediator;
        private readonly SearchParameterInfo[] _searchParameterInfos;
        private readonly SearchParameterInfo _queryParameter;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver;
        private readonly ResourceSearchParameterStatus[] _resourceSearchParameterStatuses;

        public SearchParameterStatusManagerTests()
        {
            _searchParameterStatusDataStore = Substitute.For<ISearchParameterStatusDataStore>();
            _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            _searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();
            _mediator = Substitute.For<IMediator>();

            _manager = new SearchParameterStatusManager(
                _searchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                _searchParameterSupportResolver,
                _mediator,
                NullLogger<SearchParameterStatusManager>.Instance);

            _resourceSearchParameterStatuses = new[]
                {
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Enabled,
                        Uri = new Uri(ResourceId),
                        LastUpdated = Clock.UtcNow,
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Enabled,
                        Uri = new Uri(ResourceLastupdated),
                        IsPartiallySupported = true,
                        LastUpdated = Clock.UtcNow,
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Disabled,
                        Uri = new Uri(ResourceProfile),
                        LastUpdated = Clock.UtcNow,
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Supported,
                        Uri = new Uri(ResourceSecurity),
                        LastUpdated = Clock.UtcNow,
                    },
                };

            _searchParameterStatusDataStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>()).Returns(_resourceSearchParameterStatuses);

            List<string> baseResourceTypes = new List<string>() { "Resource" };
            List<string> targetResourceTypes = new List<string>() { "Patient" };
            _queryParameter = new SearchParameterInfo("_query", "_query", SearchParamType.Token, new Uri(ResourceQuery));
            _searchParameterInfos = new[]
            {
                new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri(ResourceId), targetResourceTypes: targetResourceTypes, baseResourceTypes: baseResourceTypes),
                new SearchParameterInfo("_lastUpdated", "_lastUpdated", SearchParamType.Token, new Uri(ResourceLastupdated), targetResourceTypes: targetResourceTypes, baseResourceTypes: baseResourceTypes),
                new SearchParameterInfo("_profile", "_profile", SearchParamType.Token, new Uri(ResourceProfile), targetResourceTypes: targetResourceTypes),
                new SearchParameterInfo("_security", "_security", SearchParamType.Token, new Uri(ResourceSecurity), targetResourceTypes: targetResourceTypes),
                _queryParameter,
            };

            _searchParameterDefinitionManager.GetSearchParameters("Account")
                .Returns(_searchParameterInfos);

            _searchParameterDefinitionManager.AllSearchParameters
                .Returns(_searchParameterInfos);

            _searchParameterDefinitionManager.GetSearchParameter(ResourceQuery)
                .Returns(_queryParameter);

            _searchParameterSupportResolver
                .IsSearchParameterSupported(Arg.Any<SearchParameterInfo>())
                .Returns((false, false));

            _searchParameterSupportResolver
                .IsSearchParameterSupported(Arg.Is(_searchParameterInfos[4]))
                .Returns((true, false));
        }

        [Fact]
        public async Task GivenASPStatusManager_WhenInitializing_ThenSearchParameterIsUpdatedFromRegistry()
        {
            await _manager.EnsureInitializedAsync(CancellationToken.None);

            var list = _searchParameterDefinitionManager.GetSearchParameters("Account").ToList();

            Assert.True(list[0].IsSearchable);
            Assert.True(list[0].IsSupported);
            Assert.False(list[0].IsPartiallySupported);

            Assert.True(list[1].IsSearchable);
            Assert.True(list[1].IsSupported);
            Assert.True(list[1].IsPartiallySupported);

            Assert.False(list[2].IsSearchable);
            Assert.False(list[2].IsSupported);
            Assert.False(list[2].IsPartiallySupported);

            Assert.False(list[3].IsSearchable);
            Assert.True(list[3].IsSupported);
            Assert.False(list[3].IsPartiallySupported);

            Assert.False(list[4].IsSearchable);
            Assert.True(list[4].IsSupported);
            Assert.False(list[4].IsPartiallySupported);
        }

        [Fact]
        public async Task GivenASPStatusManager_WhenInitializing_ThenUpdatedSearchParametersInNotification()
        {
            await _manager.EnsureInitializedAsync(CancellationToken.None);

            // Id should not be modified in this test case
            var modifiedItems = _searchParameterInfos.Skip(1).ToArray();

            await _mediator
                .Received()
                .Publish(
                    Arg.Is<SearchParametersUpdatedNotification>(x => modifiedItems.Except(x.SearchParameters).Any() == false),
                    Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenASPStatusManager_WhenInitializing_ThenRegistryShouldNotUpdateNewlyFoundParameters()
        {
            await _manager.EnsureInitializedAsync(CancellationToken.None);

            await _searchParameterStatusDataStore
                .DidNotReceive()
                .UpsertStatuses(Arg.Any<List<ResourceSearchParameterStatus>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenASPStatusManager_WhenInitializingAndResolverThrowsException_ThenCatchItAndReturnFalse()
        {
            _searchParameterSupportResolver
                .IsSearchParameterSupported(Arg.Is(_searchParameterInfos[2]))
                .Returns(x => throw new FormatException("Unable to resolve"));

            await _manager.EnsureInitializedAsync(CancellationToken.None);

            var list = _searchParameterDefinitionManager.GetSearchParameters("Account").ToList();

            _searchParameterSupportResolver.Received(1).IsSearchParameterSupported(Arg.Is(_searchParameterInfos[2]));
            Assert.False(list[2].IsSearchable);
            Assert.False(list[2].IsSupported);
            Assert.False(list[2].IsPartiallySupported);
        }
    }
}
