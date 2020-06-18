// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchParameterDefinitionManagerTests
    {
        private static readonly string ResourceId = "http://hl7.org/fhir/SearchParameter/Resource-id";
        private static readonly string ResourceLastupdated = "http://hl7.org/fhir/SearchParameter/Resource-lastUpdated";
        private static readonly string ResourceProfile = "http://hl7.org/fhir/SearchParameter/Resource-profile";
        private static readonly string ResourceSecurity = "http://hl7.org/fhir/SearchParameter/Resource-security";
        private static readonly string ResourceQuery = "http://hl7.org/fhir/SearchParameter/Resource-query";

        private readonly SearchParameterStatusManager _manager;
        private readonly ISearchParameterRegistry _searchParameterRegistry;
        private readonly SearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly IMediator _mediator;
        private readonly SearchParameterInfo[] _searchParameterInfos;
        private readonly SearchParameterInfo _queryParameter;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver;

        public SearchParameterDefinitionManagerTests()
        {
            _searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();
            _mediator = Substitute.For<IMediator>();
            _searchParameterRegistry = Substitute.For<ISearchParameterRegistry>();
            _searchParameterDefinitionManager = new SearchParameterDefinitionManager(ModelInfoProvider.Instance);

            _manager = new SearchParameterStatusManager(
                _searchParameterRegistry,
                _searchParameterDefinitionManager,
                _searchParameterSupportResolver,
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

            _searchParameterSupportResolver
                .IsSearchParameterSupported(Arg.Any<SearchParameterInfo>())
                .Returns((false, false));

            _searchParameterSupportResolver
                .IsSearchParameterSupported(Arg.Is(_searchParameterInfos[4]))
                .Returns((true, false));
        }

        [Fact]
        public async Task GivenSupportedParams_WhenGettingSupported_ThenSupportedParamsReturned()
        {
            _searchParameterDefinitionManager.Start();
            await _manager.EnsureInitialized();
            var paramList = _searchParameterDefinitionManager.GetSearchByStatus(true, false);

            Assert.Single(paramList);
            Assert.Collection(paramList, p =>
            {
                Assert.True(p.IsSupported);
                Assert.False(p.IsSearchable);
            });
        }

        [Fact]
        public async Task GivenSearchableParams_WhenGettingSearchable_ThenSupportedParamsReturned()
        {
            _searchParameterDefinitionManager.Start();
            await _manager.EnsureInitialized();
            var paramList = _searchParameterDefinitionManager.GetSearchByStatus(true, true);

            Assert.Equal(2, paramList.Count());
            Assert.Collection(
                paramList,
                p1 =>
                {
                    Assert.True(p1.IsSupported);
                    Assert.True(p1.IsSearchable);
                },
                p2 =>
                {
                    Assert.True(p2.IsSupported);
                    Assert.True(p2.IsSearchable);
                });
        }

        [Fact]
        public async Task GivenDisabledParams_WhenGettingDisabled_ThenDisabledParamsReturned()
        {
            _searchParameterDefinitionManager.Start();
            await _manager.EnsureInitialized();
            var paramList = _searchParameterDefinitionManager.GetSearchByStatus(false, false);

            foreach (var param in paramList)
            {
                Assert.False(param.IsSearchable);
                Assert.False(param.IsSupported);
            }
        }
    }
}
