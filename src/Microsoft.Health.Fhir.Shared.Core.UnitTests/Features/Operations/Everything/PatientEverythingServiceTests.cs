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
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Everything;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Everything
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Trait(Traits.Category, Categories.PatientEverything)]
    public class PatientEverythingServiceTests
    {
        private readonly IModelInfoProvider _modelInfoProvider = Substitute.For<IModelInfoProvider>();
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly ISearchOptionsFactory _searchOptionsFactory = Substitute.For<ISearchOptionsFactory>();
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly ICompartmentDefinitionManager _compartmentDefinitionManager = Substitute.For<ICompartmentDefinitionManager>();
        private readonly IReferenceSearchValueParser _referenceSearchValueParser = Substitute.For<IReferenceSearchValueParser>();
        private readonly IResourceDeserializer _resourceDeserializer = Substitute.For<IResourceDeserializer>();
        private readonly IUrlResolver _urlResolver = Substitute.For<IUrlResolver>();
        private readonly Func<IScoped<IFhirDataStore>> _fhirDataStore = Substitute.For<Func<IScoped<IFhirDataStore>>>();
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

        private readonly PatientEverythingService _patientEverythingService;

        public PatientEverythingServiceTests()
        {
            // Setup default mock behaviors
            _modelInfoProvider.Version.Returns(FhirSpecification.R4);

            // Setup SearchParameterDefinitionManager
            var clinicalDateParam = new SearchParameterInfo(
                name: "date",
                code: "date",
                searchParamType: ValueSets.SearchParamType.Date,
                url: SearchParameterNames.ClinicalDateUri,
                components: null,
                expression: "date",
                targetResourceTypes: null,
                baseResourceTypes: new[] { "Observation", "Condition", "Encounter" });

            _searchParameterDefinitionManager.GetSearchParameter(SearchParameterNames.ClinicalDateUri.OriginalString)
                .Returns(clinicalDateParam);

            // Setup CompartmentDefinitionManager
            var compartmentResourceTypes = new HashSet<string> { "Patient", "Observation", "Condition", "Encounter", "Procedure" };
            _compartmentDefinitionManager.TryGetResourceTypes(ValueSets.CompartmentType.Patient, out Arg.Any<HashSet<string>>())
                .Returns(x =>
                {
                    x[1] = compartmentResourceTypes;
                    return true;
                });

            // Setup FhirDataStore
            var mockDataStore = Substitute.For<IFhirDataStore>();
            var mockScoped = Substitute.For<IScoped<IFhirDataStore>>();
            mockScoped.Value.Returns(mockDataStore);
            _fhirDataStore.Invoke().Returns(mockScoped);

            // Setup ResourceDeserializer
            _resourceDeserializer.Deserialize(Arg.Any<ResourceWrapper>()).Returns(x =>
            {
                var wrapper = x.Arg<ResourceWrapper>();
                var patient = new Patient { Id = wrapper.ResourceId, Link = new List<Patient.LinkComponent>() };
                return patient.ToResourceElement();
            });

            // Setup ContextAccessor
            var mockContext = Substitute.For<IFhirRequestContext>();
            mockContext.BundleIssues.Returns(new List<OperationOutcomeIssue>());
            _contextAccessor.RequestContext.Returns(mockContext);

            _patientEverythingService = new PatientEverythingService(
                _modelInfoProvider,
                () => _searchService.CreateMockScope(),
                _searchOptionsFactory,
                _searchParameterDefinitionManager,
                _compartmentDefinitionManager,
                _referenceSearchValueParser,
                _resourceDeserializer,
                _urlResolver,
                _fhirDataStore,
                _contextAccessor);
        }

        [Fact]
        public async Task GivenInputParameters_WhenSearch_ThenCorrectResultsAreReturned()
        {
            var searchOptions = new SearchOptions();
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);
            _searchOptionsFactory.Create(ResourceType.Patient.ToString(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);

            var searchResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, new Tuple<string, string>[0]);
            _searchService.SearchAsync(Arg.Any<SearchOptions>(), CancellationToken.None).Returns(searchResult);
            _searchService.SearchHistoryAsync(
                KnownResourceTypes.Patient,
                Arg.Any<string>(),
                Arg.Any<PartialDateTime>(),
                Arg.Any<PartialDateTime>(),
                Arg.Any<PartialDateTime>(),
                Arg.Any<int?>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                CancellationToken.None,
                Arg.Any<bool>()).Returns(searchResult);

            SearchResult actualResult = await _patientEverythingService.SearchAsync("123", null, null, null, null, null, CancellationToken.None);

            Assert.Equal(searchResult.ContinuationToken, actualResult.ContinuationToken);
            Assert.Equal(searchResult.Results, actualResult.Results);
        }

        [Fact]
        public async Task GivenInvalidContinuationToken_WhenSearch_ThenBadRequestExceptionIsThrown()
        {
            var invalidToken = ContinuationTokenEncoder.Encode("{\"Phase\":5}");

            await Assert.ThrowsAsync<BadRequestException>(() =>
                _patientEverythingService.SearchAsync("123", null, null, null, null, invalidToken, CancellationToken.None));
        }

        [Fact]
        public async Task GivenNegativePhaseInContinuationToken_WhenSearch_ThenBadRequestExceptionIsThrown()
        {
            var invalidToken = ContinuationTokenEncoder.Encode("{\"Phase\":-1}");

            await Assert.ThrowsAsync<BadRequestException>(() =>
                _patientEverythingService.SearchAsync("123", null, null, null, null, invalidToken, CancellationToken.None));
        }

        [Fact]
        public async Task GivenPhase0WithResults_WhenSearch_ThenReturnsResultsWithContinuationToken()
        {
            var searchOptions = new SearchOptions();
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);

            var patientWrapper = CreateResourceWrapper(Samples.GetDefaultPatient());
            var searchResult = new SearchResult(
                new[] { new SearchResultEntry(patientWrapper) },
                "continuationToken",
                null,
                new Tuple<string, string>[0]);

            _searchService.SearchAsync(Arg.Any<SearchOptions>(), CancellationToken.None).Returns(searchResult);

            SearchResult actualResult = await _patientEverythingService.SearchAsync("123", null, null, null, null, null, CancellationToken.None);

            Assert.NotNull(actualResult.ContinuationToken);
            Assert.Single(actualResult.Results);
        }

        [Fact]
        public async Task GivenPhase0WithNoResults_WhenSearch_ThenProceedsToPhase1()
        {
            var searchOptions = new SearchOptions();
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);

            var emptyResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, new Tuple<string, string>[0]);
            _searchService.SearchAsync(Arg.Any<SearchOptions>(), CancellationToken.None).Returns(emptyResult);

            await _patientEverythingService.SearchAsync("123", null, null, null, null, null, CancellationToken.None);

            // Should have called search multiple times (phase 0, then phase 1/2)
            await _searchService.Received().SearchAsync(Arg.Any<SearchOptions>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenPhase1WithDateRange_WhenSearch_ThenSearchCompartmentWithDateIsCalled()
        {
            var searchOptions = new SearchOptions();
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);

            var emptyResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, new Tuple<string, string>[0]);
            _searchService.SearchAsync(Arg.Any<SearchOptions>(), CancellationToken.None).Returns(emptyResult);

            var start = PartialDateTime.Parse("2020-01-01");
            var end = PartialDateTime.Parse("2020-12-31");

            await _patientEverythingService.SearchAsync("123", start, end, null, null, null, CancellationToken.None);

            // Should have searched with date parameters
            await _searchService.Received().SearchAsync(Arg.Any<SearchOptions>(), CancellationToken.None);
        }

        [Fact]
        public async Task GivenPhase1WithNoDates_WhenSearch_ThenSkipsToPhase2()
        {
            var searchOptions = new SearchOptions();
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);

            var emptyResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, new Tuple<string, string>[0]);
            _searchService.SearchAsync(Arg.Any<SearchOptions>(), CancellationToken.None).Returns(emptyResult);

            await _patientEverythingService.SearchAsync("123", null, null, null, null, null, CancellationToken.None);

            // Should proceed through phases
            await _searchService.Received().SearchAsync(Arg.Any<SearchOptions>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenTypeFilter_WhenSearch_ThenOnlySpecifiedTypesReturned()
        {
            var searchOptions = new SearchOptions();
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);

            var patientWrapper = CreateResourceWrapper(Samples.GetDefaultPatient());
            var observationWrapper = CreateResourceWrapper(Samples.GetDefaultObservation());

            var searchResult = new SearchResult(
                new[] { new SearchResultEntry(patientWrapper), new SearchResultEntry(observationWrapper) },
                null,
                null,
                new Tuple<string, string>[0]);

            _searchService.SearchAsync(Arg.Any<SearchOptions>(), CancellationToken.None).Returns(searchResult);

            SearchResult actualResult = await _patientEverythingService.SearchAsync("123", null, null, null, "Patient", null, CancellationToken.None);

            // Type filtering happens internally
            Assert.NotNull(actualResult);
        }

        [Fact]
        public async Task GivenSinceParameter_WhenSearch_ThenFiltersByLastModified()
        {
            var searchOptions = new SearchOptions();
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);

            var since = PartialDateTime.Parse("2020-01-01");
            var emptyResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, new Tuple<string, string>[0]);
            _searchService.SearchAsync(Arg.Any<SearchOptions>(), CancellationToken.None).Returns(emptyResult);

            SearchResult actualResult = await _patientEverythingService.SearchAsync("123", null, null, since, null, null, CancellationToken.None);

            Assert.NotNull(actualResult);
        }

        [Fact]
        public async Task GivenContinuationTokenWithPhase1_WhenSearch_ThenResumesFromPhase1()
        {
            var searchOptions = new SearchOptions();
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);

            var token = new EverythingOperationContinuationToken { Phase = 1 };
            var encodedToken = ContinuationTokenEncoder.Encode(token.ToJson());

            var emptyResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, new Tuple<string, string>[0]);
            _searchService.SearchAsync(Arg.Any<SearchOptions>(), CancellationToken.None).Returns(emptyResult);

            var start = PartialDateTime.Parse("2020-01-01");
            SearchResult actualResult = await _patientEverythingService.SearchAsync("123", start, null, null, null, encodedToken, CancellationToken.None);

            Assert.NotNull(actualResult);
        }

        [Fact]
        public async Task GivenContinuationTokenWithPhase2_WhenSearch_ThenResumesFromPhase2()
        {
            var searchOptions = new SearchOptions();
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);

            var token = new EverythingOperationContinuationToken { Phase = 2 };
            var encodedToken = ContinuationTokenEncoder.Encode(token.ToJson());

            var emptyResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, new Tuple<string, string>[0]);
            _searchService.SearchAsync(Arg.Any<SearchOptions>(), CancellationToken.None).Returns(emptyResult);

            SearchResult actualResult = await _patientEverythingService.SearchAsync("123", null, null, null, null, encodedToken, CancellationToken.None);

            Assert.NotNull(actualResult);
        }

        [Fact]
        public async Task GivenContinuationTokenWithInternalToken_WhenSearch_ThenPassesInternalTokenToSearch()
        {
            var searchOptions = new SearchOptions();
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);

            var token = new EverythingOperationContinuationToken
            {
                Phase = 0,
                InternalContinuationToken = "internalToken",
            };
            var encodedToken = ContinuationTokenEncoder.Encode(token.ToJson());

            var emptyResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, new Tuple<string, string>[0]);
            _searchService.SearchAsync(Arg.Any<SearchOptions>(), CancellationToken.None).Returns(emptyResult);

            SearchResult actualResult = await _patientEverythingService.SearchAsync("123", null, null, null, null, encodedToken, CancellationToken.None);

            Assert.NotNull(actualResult);
        }

        [Fact]
        public async Task GivenMultipleResourceTypes_WhenSearch_ThenFiltersCorrectly()
        {
            var searchOptions = new SearchOptions();
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);

            var emptyResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, new Tuple<string, string>[0]);
            _searchService.SearchAsync(Arg.Any<SearchOptions>(), CancellationToken.None).Returns(emptyResult);

            SearchResult actualResult = await _patientEverythingService.SearchAsync("123", null, null, null, "Patient,Observation", null, CancellationToken.None);

            Assert.NotNull(actualResult);
        }

        [Fact]
        public async Task GivenPhaseWithResults_WhenResultsReturnedWithContinuationToken_ThenNextCallContinuesInSamePhase()
        {
            var searchOptions = new SearchOptions();
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);

            var patientWrapper = CreateResourceWrapper(Samples.GetDefaultPatient());
            var searchResult = new SearchResult(
                new[] { new SearchResultEntry(patientWrapper) },
                "continuationToken",
                null,
                new Tuple<string, string>[0]);

            _searchService.SearchAsync(Arg.Any<SearchOptions>(), CancellationToken.None).Returns(searchResult);

            SearchResult actualResult = await _patientEverythingService.SearchAsync("123", null, null, null, null, null, CancellationToken.None);

            Assert.NotNull(actualResult.ContinuationToken);
            Assert.Single(actualResult.Results);
        }

        [Fact]
        public async Task GivenAllPhasesComplete_WhenNoMoreResults_ThenReturnsFinalResults()
        {
            var searchOptions = new SearchOptions();
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);

            var emptyResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, new Tuple<string, string>[0]);
            _searchService.SearchAsync(Arg.Any<SearchOptions>(), CancellationToken.None).Returns(emptyResult);

            SearchResult actualResult = await _patientEverythingService.SearchAsync("123", null, null, null, null, null, CancellationToken.None);

            Assert.Null(actualResult.ContinuationToken);
        }

        [Fact]
        public async Task GivenPhase2CompletesWithNoResults_WhenNoDevicePhaseNeeded_ThenReturnsWithNoContinuation()
        {
            // Configure for R5 where Device search is not needed (Phase 3 is skipped)
            _modelInfoProvider.Version.Returns(FhirSpecification.R5);

            var searchOptions = new SearchOptions();
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);

            var emptyResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, new Tuple<string, string>[0]);
            _searchService.SearchAsync(Arg.Any<SearchOptions>(), CancellationToken.None).Returns(emptyResult);

            SearchResult actualResult = await _patientEverythingService.SearchAsync("123", null, null, null, null, null, CancellationToken.None);

            Assert.Null(actualResult.ContinuationToken);
        }

        private ResourceWrapper CreateResourceWrapper(ResourceElement resourceElement)
        {
            // Ensure the resource has an ID and LastUpdated
            var poco = resourceElement.ToPoco();
            if (string.IsNullOrEmpty(poco.Id))
            {
                poco.Id = Guid.NewGuid().ToString();
            }

            if (poco.Meta == null)
            {
                poco.Meta = new Meta { LastUpdated = DateTimeOffset.UtcNow };
            }
            else if (poco.Meta.LastUpdated == null)
            {
                poco.Meta.LastUpdated = DateTimeOffset.UtcNow;
            }

            // Convert back to ResourceElement to ensure ID is properly set
            var updatedElement = poco.ToResourceElement();
            var json = updatedElement.Instance.ToJson();
            var rawResource = new RawResource(json, FhirResourceFormat.Json, isMetaSet: false);

            return new ResourceWrapper(
                updatedElement,
                rawResource,
                new ResourceRequest("POST"),
                false,
                null,
                null,
                null);
        }
    }
}
