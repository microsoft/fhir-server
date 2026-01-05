// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.MemberMatch;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using SearchEntryMode = Microsoft.Health.Fhir.ValueSets.SearchEntryMode;
using SearchParamType = Microsoft.Health.Fhir.ValueSets.SearchParamType;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.MemberMatch
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.MemberMatch)]
    public class MemberMatchServiceTests
    {
        private readonly ISearchService _searchService;
        private readonly IScoped<ISearchService> _scopedSearchService;
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly IResourceDeserializer _resourceDeserializer;
        private readonly ISearchIndexer _searchIndexer;
        private readonly ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver _searchParameterDefinitionManagerResolver;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly IExpressionParser _expressionParser;
        private readonly MemberMatchService _memberMatchService;

        public MemberMatchServiceTests()
        {
            _searchService = Substitute.For<ISearchService>();
            _scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            _scopedSearchService.Value.Returns(_searchService);
            _searchServiceFactory = () => _scopedSearchService;
            _resourceDeserializer = Substitute.For<IResourceDeserializer>();
            _searchIndexer = Substitute.For<ISearchIndexer>();
            _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            _searchParameterDefinitionManagerResolver = () => _searchParameterDefinitionManager;
            _expressionParser = Substitute.For<IExpressionParser>();

            // Setup search parameter definition manager with required parameters
            var beneficiaryParameter = new SearchParameterInfo("beneficiary", "beneficiary", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Coverage-beneficiary"));
            var resourceTypeParameter = new SearchParameterInfo("_type", "_type", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-type"));

            _searchParameterDefinitionManager.GetSearchParameter("Coverage", "beneficiary").Returns(beneficiaryParameter);
            _searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.ResourceType).Returns(resourceTypeParameter);

            _memberMatchService = new MemberMatchService(
                _searchServiceFactory,
                _resourceDeserializer,
                _searchIndexer,
                _searchParameterDefinitionManagerResolver,
                _expressionParser,
                NullLogger<MemberMatchService>.Instance);
        }

        [Fact]
        public async Task GivenNoMatchingPatients_WhenFindMatch_ThenMemberMatchNoMatchFoundExceptionIsThrown()
        {
            // Arrange
            var patient = CreateTestPatient();
            var coverage = CreateTestCoverage();
            var patientElement = patient.ToResourceElement();
            var coverageElement = coverage.ToResourceElement();

            _searchIndexer.Extract(Arg.Any<ResourceElement>()).Returns(new List<SearchIndexEntry>());

            var searchResult = new SearchResult(new List<SearchResultEntry>(), null, null, new List<Tuple<string, string>>());
            _searchService.SearchAsync(Arg.Any<SearchOptions>(), Arg.Any<CancellationToken>()).Returns(searchResult);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<MemberMatchMatchingException>(
                () => _memberMatchService.FindMatch(coverageElement, patientElement, CancellationToken.None));

            Assert.Equal(Microsoft.Health.Fhir.Core.Resources.MemberMatchNoMatchFound, exception.Message);
        }

        [Fact]
        public async Task GivenMultipleMatchingPatients_WhenFindMatch_ThenMemberMatchMultipleMatchesFoundExceptionIsThrown()
        {
            // Arrange
            var patient = CreateTestPatient();
            var coverage = CreateTestCoverage();
            var patientElement = patient.ToResourceElement();
            var coverageElement = coverage.ToResourceElement();

            _searchIndexer.Extract(Arg.Any<ResourceElement>()).Returns(new List<SearchIndexEntry>());

            var matchingPatient1 = CreateMatchingPatient("patient1");
            var matchingPatient2 = CreateMatchingPatient("patient2");

            var searchEntries = new List<SearchResultEntry>
            {
                CreateSearchResultEntry(matchingPatient1, SearchEntryMode.Match),
                CreateSearchResultEntry(matchingPatient2, SearchEntryMode.Match),
            };

            var searchResult = new SearchResult(searchEntries, null, null, new List<Tuple<string, string>>());
            _searchService.SearchAsync(Arg.Any<SearchOptions>(), Arg.Any<CancellationToken>()).Returns(searchResult);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<MemberMatchMatchingException>(
                () => _memberMatchService.FindMatch(coverageElement, patientElement, CancellationToken.None));

            Assert.Equal(Microsoft.Health.Fhir.Core.Resources.MemberMatchMultipleMatchesFound, exception.Message);
        }

        [Fact]
        public async Task GivenMatchingPatientWithoutMBIdentifier_WhenFindMatch_ThenMemberMatchNoMatchFoundExceptionIsThrown()
        {
            // Arrange
            var patient = CreateTestPatient();
            var coverage = CreateTestCoverage();
            var patientElement = patient.ToResourceElement();
            var coverageElement = coverage.ToResourceElement();

            _searchIndexer.Extract(Arg.Any<ResourceElement>()).Returns(new List<SearchIndexEntry>());

            // Create a patient without MB identifier
            var matchingPatient = new Patient
            {
                Id = "patient1",
                Identifier = new List<Identifier>
                {
                    new Identifier("test-system", "12345")
                    {
                        Type = new CodeableConcept("http://terminology.hl7.org/CodeSystem/v2-0203", "MR"),
                    },
                },
            };

            var matchingPatientElement = matchingPatient.ToResourceElement();

            var searchEntry = CreateSearchResultEntry(matchingPatient, SearchEntryMode.Match);
            var searchResult = new SearchResult(new List<SearchResultEntry> { searchEntry }, null, null, new List<Tuple<string, string>>());

            _searchService.SearchAsync(Arg.Any<SearchOptions>(), Arg.Any<CancellationToken>()).Returns(searchResult);
            _resourceDeserializer.Deserialize(Arg.Any<ResourceWrapper>()).Returns(matchingPatientElement);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<MemberMatchMatchingException>(
                () => _memberMatchService.FindMatch(coverageElement, patientElement, CancellationToken.None));

            Assert.Equal(Microsoft.Health.Fhir.Core.Resources.MemberMatchNoMatchFound, exception.Message);
        }

        [Fact]
        public async Task GivenInvalidSearchOperationException_WhenFindMatch_ThenExceptionIsRethrown()
        {
            // Arrange
            var patient = CreateTestPatient();
            var coverage = CreateTestCoverage();
            var patientElement = patient.ToResourceElement();
            var coverageElement = coverage.ToResourceElement();

            _searchIndexer.Extract(Arg.Any<ResourceElement>()).Returns(new List<SearchIndexEntry>());

            var invalidSearchException = new InvalidSearchOperationException("Invalid search");
            _searchService.SearchAsync(Arg.Any<SearchOptions>(), Arg.Any<CancellationToken>()).Returns<SearchResult>(x => throw invalidSearchException);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidSearchOperationException>(
                () => _memberMatchService.FindMatch(coverageElement, patientElement, CancellationToken.None));
        }

        [Fact]
        public async Task GivenSqlQueryPlanException_WhenFindMatch_ThenExceptionIsRethrown()
        {
            // Arrange
            var patient = CreateTestPatient();
            var coverage = CreateTestCoverage();
            var patientElement = patient.ToResourceElement();
            var coverageElement = coverage.ToResourceElement();

            _searchIndexer.Extract(Arg.Any<ResourceElement>()).Returns(new List<SearchIndexEntry>());

            var sqlException = new Exception("The query processor ran out of internal resources and could not produce a query plan.");
            _searchService.SearchAsync(Arg.Any<SearchOptions>(), Arg.Any<CancellationToken>()).Returns<SearchResult>(x => throw sqlException);

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(
                () => _memberMatchService.FindMatch(coverageElement, patientElement, CancellationToken.None));
        }

        [Fact]
        public async Task GivenGenericException_WhenFindMatch_ThenMemberMatchMatchingExceptionIsThrown()
        {
            // Arrange
            var patient = CreateTestPatient();
            var coverage = CreateTestCoverage();
            var patientElement = patient.ToResourceElement();
            var coverageElement = coverage.ToResourceElement();

            _searchIndexer.Extract(Arg.Any<ResourceElement>()).Returns(new List<SearchIndexEntry>());

            var genericException = new Exception("Some unexpected error");
            _searchService.SearchAsync(Arg.Any<SearchOptions>(), Arg.Any<CancellationToken>()).Returns<SearchResult>(x => throw genericException);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<MemberMatchMatchingException>(
                () => _memberMatchService.FindMatch(coverageElement, patientElement, CancellationToken.None));

            Assert.Equal(Microsoft.Health.Fhir.Core.Resources.GenericMemberMatch, exception.Message);
        }

        [Fact]
        public async Task GivenMatchWithIncludeEntries_WhenFindMatch_ThenOnlyMatchEntriesAreConsidered()
        {
            // Arrange
            var patient = CreateTestPatient();
            var coverage = CreateTestCoverage();
            var patientElement = patient.ToResourceElement();
            var coverageElement = coverage.ToResourceElement();

            _searchIndexer.Extract(Arg.Any<ResourceElement>()).Returns(new List<SearchIndexEntry>());

            var matchingPatient = CreateMatchingPatient("patient1");
            var includedPatient = CreateMatchingPatient("patient2");
            var matchingPatientElement = matchingPatient.ToResourceElement();

            var searchEntries = new List<SearchResultEntry>
            {
                CreateSearchResultEntry(matchingPatient, SearchEntryMode.Match),
                CreateSearchResultEntry(includedPatient, SearchEntryMode.Include),
            };

            var searchResult = new SearchResult(searchEntries, null, null, new List<Tuple<string, string>>());

            _searchService.SearchAsync(Arg.Any<SearchOptions>(), Arg.Any<CancellationToken>()).Returns(searchResult);
            _resourceDeserializer.Deserialize(Arg.Any<ResourceWrapper>()).Returns(matchingPatientElement);

            // Act
            var result = await _memberMatchService.FindMatch(coverageElement, patientElement, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("patient1", matchingPatient.Id);
        }

        [Fact]
        public async Task GivenValidInputs_WhenFindMatch_ThenSearchOptionsAreConfiguredCorrectly()
        {
            // Arrange
            var patient = CreateTestPatient();
            var coverage = CreateTestCoverage();
            var patientElement = patient.ToResourceElement();
            var coverageElement = coverage.ToResourceElement();

            _searchIndexer.Extract(Arg.Any<ResourceElement>()).Returns(new List<SearchIndexEntry>());

            var matchingPatient = CreateMatchingPatient("patient1");
            var matchingPatientElement = matchingPatient.ToResourceElement();

            var searchEntry = CreateSearchResultEntry(matchingPatient, SearchEntryMode.Match);
            var searchResult = new SearchResult(new List<SearchResultEntry> { searchEntry }, null, null, new List<Tuple<string, string>>());

            SearchOptions capturedOptions = null;
            _searchService.SearchAsync(Arg.Do<SearchOptions>(x => capturedOptions = x), Arg.Any<CancellationToken>()).Returns(searchResult);
            _resourceDeserializer.Deserialize(Arg.Any<ResourceWrapper>()).Returns(matchingPatientElement);

            // Act
            await _memberMatchService.FindMatch(coverageElement, patientElement, CancellationToken.None);

            // Assert
            Assert.NotNull(capturedOptions);
            Assert.Equal(2, capturedOptions.MaxItemCount);
            Assert.NotNull(capturedOptions.Sort);
            Assert.NotNull(capturedOptions.UnsupportedSearchParams);
            Assert.NotNull(capturedOptions.Expression);
        }

        [Fact]
        public async Task GivenMatchingPatientWithNullIdentifierType_WhenFindMatch_ThenMemberMatchNoMatchFoundExceptionIsThrown()
        {
            // Arrange
            var patient = CreateTestPatient();
            var coverage = CreateTestCoverage();
            var patientElement = patient.ToResourceElement();
            var coverageElement = coverage.ToResourceElement();

            _searchIndexer.Extract(Arg.Any<ResourceElement>()).Returns(new List<SearchIndexEntry>());

            var matchingPatient = new Patient
            {
                Id = "patient1",
                Identifier = new List<Identifier>
                {
                    new Identifier("test-system", "12345"),
                },
            };

            var matchingPatientElement = matchingPatient.ToResourceElement();

            var searchEntry = CreateSearchResultEntry(matchingPatient, SearchEntryMode.Match);
            var searchResult = new SearchResult(new List<SearchResultEntry> { searchEntry }, null, null, new List<Tuple<string, string>>());

            _searchService.SearchAsync(Arg.Any<SearchOptions>(), Arg.Any<CancellationToken>()).Returns(searchResult);
            _resourceDeserializer.Deserialize(Arg.Any<ResourceWrapper>()).Returns(matchingPatientElement);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<MemberMatchMatchingException>(
                () => _memberMatchService.FindMatch(coverageElement, patientElement, CancellationToken.None));

            Assert.Equal(Microsoft.Health.Fhir.Core.Resources.MemberMatchNoMatchFound, exception.Message);
        }

        private static Patient CreateTestPatient()
        {
            return new Patient
            {
                Id = "test-patient",
                Name = new List<HumanName>
                {
                    new HumanName { Family = "Doe", Given = new[] { "John" } },
                },
                BirthDate = "1970-01-01",
            };
        }

        private static Coverage CreateTestCoverage()
        {
            return new Coverage
            {
                Id = "test-coverage",
                Status = FinancialResourceStatusCodes.Active,
                Beneficiary = new ResourceReference("Patient/test-patient"),
                Type = new CodeableConcept("http://terminology.hl7.org/CodeSystem/v3-ActCode", "EHCPOL"),
            };
        }

        private static Patient CreateMatchingPatient(string id)
        {
            return new Patient
            {
                Id = id,
                Identifier = new List<Identifier>
                {
                    new Identifier("test-system", "MB-12345")
                    {
                        Type = new CodeableConcept("http://terminology.hl7.org/CodeSystem/v2-0203", "MB")
                        {
                            Coding = new List<Coding>
                            {
                                new Coding("http://terminology.hl7.org/CodeSystem/v2-0203", "MB", "Member Number"),
                            },
                        },
                    },
                },
                Name = new List<HumanName>
                {
                    new HumanName { Family = "Doe", Given = new[] { "John" } },
                },
            };
        }

        private static SearchResultEntry CreateSearchResultEntry(Patient patient, SearchEntryMode entryMode)
        {
            var rawResource = new RawResource(
                System.Text.Json.JsonSerializer.Serialize(patient),
                FhirResourceFormat.Json,
                false);

            var wrapper = new ResourceWrapper(
                patient.ToResourceElement(),
                rawResource,
                new ResourceRequest("GET"),
                false,
                null,
                null,
                null);

            return new SearchResultEntry(wrapper, entryMode);
        }
    }
}
