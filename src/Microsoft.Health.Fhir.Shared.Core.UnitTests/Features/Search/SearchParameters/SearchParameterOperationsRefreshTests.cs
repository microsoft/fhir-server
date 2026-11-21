// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
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
        private readonly ISearchParameterSupportResolver _supportResolver = Substitute.For<ISearchParameterSupportResolver>();
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly SearchParameterStatusManager _statusManager;
        private readonly SearchParameterOperations _operations;

        public SearchParameterOperationsRefreshTests()
        {
            _definitionManager.AllSearchParameters.Returns(Array.Empty<SearchParameterInfo>());
            _definitionManager.SearchParameterHashMap.Returns(new Dictionary<string, string>());

            _statusManager = new SearchParameterStatusManager(
                _statusDataStore,
                _definitionManager,
                _supportResolver,
                Substitute.For<IMediator>(),
                NullLogger<SearchParameterStatusManager>.Instance);

            _operations = CreateOperations(packageInstallEnabled: false);
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

        [Theory]
        [InlineData(false, true, true, false)]
        [InlineData(true, false, true, false)]
        [InlineData(true, true, false, false)]
        [InlineData(true, true, true, true)]
        public async Task GivenPackageInstallSettings_WhenRefreshing_ThenOnlyEnabledSqlBackgroundRefreshesPublishProofs(
            bool packageInstallEnabled,
            bool backgroundRefresh,
            bool sqlStore,
            bool publishesProof)
        {
            // Arrange
            _statusDataStore.SearchParamCacheUpdateProcessName.Returns(sqlStore ? "SearchParamCacheUpdate" : null);
            _statusDataStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
                .Returns(new[] { CreateStatus("http://test/SearchParameter/disabled", SearchParameterStatus.Disabled, SortParameterStatus.Disabled) });
            using var operations = CreateOperations(packageInstallEnabled);
            string proof = FirstReadLastUpdated.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture)
                + "|" + SearchParameterCacheFingerprint.Calculate(_definitionManager);

            // Act
            await operations.GetAndApplySearchParameterUpdates(CancellationToken.None, zeroWaitForSemaphore: backgroundRefresh);

            // Assert
            await _statusDataStore.Received(publishesProof ? 1 : 0).TryLogEvent(
                SearchParameterCacheFingerprint.UpdateProcessName,
                "Warn",
                proof,
                null,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAMissingDefinition_WhenTheWatermarkAdvances_ThenThePublishedProofDiffersFromACompleteCache()
        {
            // Arrange
            const string url = "http://test/SearchParameter/missing";
            _statusDataStore.SearchParamCacheUpdateProcessName.Returns("SearchParamCacheUpdate");
            _statusDataStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
                .Returns(new[] { CreateStatus(url, SearchParameterStatus.Enabled, SortParameterStatus.Disabled) });
            SetupSearchResults(Array.Empty<SearchParameter>());
            using var operations = CreateOperations(packageInstallEnabled: true);

            var completeCache = Substitute.For<ISearchParameterDefinitionManager>();
            completeCache.AllSearchParameters.Returns(new[]
            {
                new SearchParameterInfo("missing", "missing", Microsoft.Health.Fhir.ValueSets.SearchParamType.Token, new Uri(url), expression: "Patient.identifier"),
            });
            completeCache.SearchParameterHashMap.Returns(new Dictionary<string, string>());
            string missingFingerprint = SearchParameterCacheFingerprint.Calculate(_definitionManager);
            string completeFingerprint = SearchParameterCacheFingerprint.Calculate(completeCache);
            string watermark = FirstReadLastUpdated.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);

            // Act
            await operations.GetAndApplySearchParameterUpdates(CancellationToken.None, zeroWaitForSemaphore: true);

            // Assert
            Assert.Equal(FirstReadLastUpdated, operations.SearchParamLastUpdated);
            Assert.NotEqual(completeFingerprint, missingFingerprint);
            await _statusDataStore.Received(1).TryLogEvent(
                SearchParameterCacheFingerprint.UpdateProcessName,
                "Warn",
                $"{watermark}|{missingFingerprint}",
                null,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public void GivenEquivalentCachesInDifferentOrders_WhenCalculatingFingerprints_ThenOnlyActualDifferencesChangeTheProof()
        {
            // Arrange
            var first = Substitute.For<ISearchParameterDefinitionManager>();
            var second = Substitute.For<ISearchParameterDefinitionManager>();
            var firstA = FingerprintParameter("a", new[] { "Patient", "Observation" });
            var secondA = FingerprintParameter("a", new[] { "Observation", "Patient" });
            var firstB = FingerprintParameter("b", new[] { "Patient" });
            var secondB = FingerprintParameter("b", new[] { "Patient" });
            first.AllSearchParameters.Returns(new[] { firstA, firstB });
            second.AllSearchParameters.Returns(new[] { secondB, secondA });
            first.SearchParameterHashMap.Returns(new Dictionary<string, string> { ["Patient"] = "p", ["Observation"] = "o" });
            second.SearchParameterHashMap.Returns(new Dictionary<string, string> { ["Observation"] = "o", ["Patient"] = "p" });

            // Act
            string fingerprint = SearchParameterCacheFingerprint.Calculate(first);

            // Assert
            Assert.Equal(fingerprint, SearchParameterCacheFingerprint.Calculate(second));
            second.AllSearchParameters.Returns(new[] { secondA });
            Assert.NotEqual(fingerprint, SearchParameterCacheFingerprint.Calculate(second));
            second.AllSearchParameters.Returns(new[] { secondB, secondA });
            secondB.SearchParameterStatus = SearchParameterStatus.Disabled;
            Assert.NotEqual(fingerprint, SearchParameterCacheFingerprint.Calculate(second));
            secondB.SearchParameterStatus = firstB.SearchParameterStatus;
            second.SearchParameterHashMap.Returns(new Dictionary<string, string> { ["Patient"] = "different", ["Observation"] = "o" });
            Assert.NotEqual(fingerprint, SearchParameterCacheFingerprint.Calculate(second));
        }

        private static SearchParameterInfo FingerprintParameter(string name, string[] bases) =>
            new SearchParameterInfo(
                name,
                name,
                Microsoft.Health.Fhir.ValueSets.SearchParamType.Token,
                new Uri($"http://test/SearchParameter/{name}"),
                expression: "Patient.identifier",
                baseResourceTypes: bases)
            {
                SearchParameterStatus = SearchParameterStatus.Enabled,
            };

        private SearchParameterOperations CreateOperations(bool packageInstallEnabled)
        {
            var configuration = new OperationsConfiguration();
            configuration.FhirPackageInstall.Enabled = packageInstallEnabled;
            return new SearchParameterOperations(
                _statusManager,
                _definitionManager,
                ModelInfoProvider.Instance,
                _supportResolver,
                Substitute.For<IDataStoreSearchParameterValidator>(),
                _searchService.CreateMockScopeProvider(),
                Substitute.For<IFhirDataStore>().CreateMockScopeProvider(),
                Options.Create(configuration),
                NullLogger<SearchParameterOperations>.Instance);
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
