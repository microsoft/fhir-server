// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using SearchParameterStatus = Microsoft.Health.Fhir.Core.Features.Search.Registry.SearchParameterStatus;
using SearchParamType = Hl7.Fhir.Model.SearchParamType;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchParameters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterOperationsRefreshTests
    {
        private static readonly DateTimeOffset FirstReadLastUpdated = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private readonly ISearchParameterStatusDataStore _statusDataStore = Substitute.For<ISearchParameterStatusDataStore>();
        private readonly ISearchParameterDefinitionManager _definitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly SearchParameterOperations _operations;

        public SearchParameterOperationsRefreshTests()
        {
            var supportResolver = Substitute.For<ISearchParameterSupportResolver>();
            _definitionManager.AllSearchParameters.Returns(Array.Empty<SearchParameterInfo>());

            var statusManager = new SearchParameterStatusManager(
                _statusDataStore,
                _definitionManager,
                supportResolver,
                Substitute.For<IMediator>(),
                NullLogger<SearchParameterStatusManager>.Instance);

            _operations = new SearchParameterOperations(
                statusManager,
                _definitionManager,
                ModelInfoProvider.Instance,
                supportResolver,
                Substitute.For<IDataStoreSearchParameterValidator>(),
                _searchService.CreateMockScopeProvider(),
                Substitute.For<IFhirDataStore>().CreateMockScopeProvider(),
                NullLogger<SearchParameterOperations>.Instance);
        }

        [Fact]
        public async Task GivenACompositeBeforeItsComponentsInALargeBatch_WhenRefreshing_ThenTheCompositeIsAddedAfterAllNonComposites()
        {
            // Arrange: the composite is first, followed by 100 non-composites, so without reordering it would land in the first chunk of 100.
            var composite = CreateSearchParameter("http://test/SearchParameter/composite", SearchParamType.Composite);
            var nonComposites = Enumerable.Range(0, 100)
                .Select(i => CreateSearchParameter($"http://test/SearchParameter/component-{i}", SearchParamType.Token))
                .ToList();
            var resources = new[] { composite }.Concat(nonComposites).ToList();

            _statusDataStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
                .Returns(resources.Select(r => CreateStatus(r.Url, SearchParameterStatus.Enabled, SortParameterStatus.Disabled)).ToList());
            SetupSearchResults(resources);

            var addedChunks = new List<List<string>>();
            _definitionManager.AddNewSearchParameters(Arg.Do<IReadOnlyCollection<ITypedElement>>(chunk => addedChunks.Add(chunk.Select(e => e.GetStringScalar("url")).ToList())));

            // Act
            await _operations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            // Assert
            Assert.Equal(2, addedChunks.Count);
            Assert.Equal(nonComposites.Select(r => r.Url), addedChunks[0]);
            Assert.Equal(new[] { composite.Url }, addedChunks[1]);
        }

        [Fact]
        public async Task GivenAddedDefinitions_WhenTheRereadStatusIsUnchanged_ThenTheRereadStatusIsApplied()
        {
            // Arrange
            var searchParameter = CreateSearchParameter("http://test/SearchParameter/custom", SearchParamType.Token);
            var firstRead = CreateStatus(searchParameter.Url, SearchParameterStatus.Enabled, SortParameterStatus.Disabled);
            var reread = CreateStatus(searchParameter.Url, SearchParameterStatus.Enabled, SortParameterStatus.Enabled);

            _statusDataStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
                .Returns(new[] { firstRead }, new[] { reread });
            SetupSearchResults(new[] { searchParameter });

            // Act
            await _operations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            // Assert
            _statusDataStore.Received(1).SyncStatuses(Arg.Is<IReadOnlyCollection<ResourceSearchParameterStatus>>(s => s.Single() == reread));
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task GivenAddedDefinitions_WhenTheRowChangedBetweenReads_ThenTheOriginalStatusIsApplied(bool lastUpdatedChanged, bool statusChanged)
        {
            // Arrange
            var searchParameter = CreateSearchParameter("http://test/SearchParameter/custom", SearchParamType.Token);
            var firstRead = CreateStatus(searchParameter.Url, SearchParameterStatus.Enabled, SortParameterStatus.Disabled);
            var reread = CreateStatus(
                searchParameter.Url,
                statusChanged ? SearchParameterStatus.Supported : SearchParameterStatus.Enabled,
                SortParameterStatus.Enabled,
                lastUpdatedChanged ? FirstReadLastUpdated.AddSeconds(1) : FirstReadLastUpdated);

            _statusDataStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
                .Returns(new[] { firstRead }, new[] { reread });
            SetupSearchResults(new[] { searchParameter });

            // Act
            await _operations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            // Assert
            _statusDataStore.Received(1).SyncStatuses(Arg.Is<IReadOnlyCollection<ResourceSearchParameterStatus>>(s => s.Single() == firstRead));
        }

        [Fact]
        public async Task GivenNoAddedDefinitions_WhenRefreshing_ThenStatusesAreNotReread()
        {
            // Arrange
            var disabled = CreateStatus("http://test/SearchParameter/disabled", SearchParameterStatus.Disabled, SortParameterStatus.Disabled);
            _statusDataStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
                .Returns(new[] { disabled });
            SetupSearchResults(Array.Empty<SearchParameter>());

            // Act
            await _operations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            // Assert
            await _statusDataStore.Received(1).GetSearchParameterStatuses(Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>());
            _definitionManager.DidNotReceive().AddNewSearchParameters(Arg.Any<IReadOnlyCollection<ITypedElement>>());
            _statusDataStore.Received(1).SyncStatuses(Arg.Is<IReadOnlyCollection<ResourceSearchParameterStatus>>(s => s.Single() == disabled));
        }

        private void SetupSearchResults(IReadOnlyCollection<SearchParameter> searchParameters)
        {
            var serializer = new FhirJsonSerializer();
            var entries = searchParameters
                .Select(sp =>
                {
                    var rawResource = new RawResource(serializer.SerializeToString(sp), FhirResourceFormat.Json, false);
                    var wrapper = new ResourceWrapper(
                        new ResourceElement(rawResource.ToITypedElement(ModelInfoProvider.Instance)),
                        rawResource,
                        new ResourceRequest("POST"),
                        false,
                        null,
                        null,
                        null);
                    return new SearchResultEntry(wrapper);
                })
                .ToList();

            _searchService
                .SearchAsync(
                    KnownResourceTypes.SearchParameter,
                    Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>(),
                    Arg.Any<ResourceVersionType>(),
                    Arg.Any<bool>(),
                    Arg.Any<bool>(),
                    Arg.Any<Microsoft.Health.Fhir.Core.Features.Security.DataActions>())
                .Returns(new SearchResult(entries, null, null, new List<Tuple<string, string>>()));
        }

        private static SearchParameter CreateSearchParameter(string url, SearchParamType type)
        {
            var searchParameter = new SearchParameter
            {
                Id = url.Split('/').Last(),
                Url = url,
                Name = url.Split('/').Last(),
                Code = url.Split('/').Last(),
                Status = PublicationStatus.Active,
                Description = new Markdown("test"),
                Type = type,
                Expression = "Patient.id",
#if Stu3 || R4 || R4B
                Base = new List<ResourceType?> { ResourceType.Patient },
#else
                Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
            };

            if (type == SearchParamType.Composite)
            {
                searchParameter.Component = new List<SearchParameter.ComponentComponent>
                {
#if Stu3
                    new SearchParameter.ComponentComponent { Definition = new ResourceReference("http://test/SearchParameter/component-0"), Expression = "Patient" },
#else
                    new SearchParameter.ComponentComponent { Definition = "http://test/SearchParameter/component-0", Expression = "Patient" },
#endif
                };
            }

            return searchParameter;
        }

        private static ResourceSearchParameterStatus CreateStatus(string url, SearchParameterStatus status, SortParameterStatus sortStatus, DateTimeOffset? lastUpdated = null)
        {
            return new ResourceSearchParameterStatus
            {
                Uri = new Uri(url),
                Status = status,
                SortStatus = sortStatus,
                LastUpdated = lastUpdated ?? FirstReadLastUpdated,
            };
        }
    }
}
