// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using static Hl7.Fhir.Model.OperationOutcome;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Definition
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterDefinitionBuilderTests
    {
        private readonly FhirJsonParser _jsonParser = new FhirJsonParser();

        private readonly string _invalidEntriesFile = "SearchParametersWithInvalidEntries.json";
        private readonly string _invalidDefinitionsFile = "SearchParametersWithInvalidDefinitions.json";
        private readonly string _validEntriesFile = "SearchParameters.json";
        private readonly ConcurrentDictionary<string, SearchParameterInfo> _uriDictionary;
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentQueue<SearchParameterInfo>>> _resourceTypeDictionary;
        private readonly ISearchParameterComparer<SearchParameterInfo> _searchParameterComparer;

        public SearchParameterDefinitionBuilderTests()
        {
            _uriDictionary = new ConcurrentDictionary<string, SearchParameterInfo>();
            _resourceTypeDictionary = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentQueue<SearchParameterInfo>>>();
            _searchParameterComparer = new SearchParameterComparer(Substitute.For<ILogger<ISearchParameterComparer<SearchParameterInfo>>>());
        }

        [Theory]
        [InlineData("SearchParametersWithInvalidBase.json", "SearchParameter[http://hl7.org/fhir/SearchParameter/DomainResource-text].resource.base is not defined.")]
        public void GivenAnInvalidSearchParameterDefinitionFile_WhenBuilt_ThenInvalidDefinitionExceptionShouldBeThrown(string fileName, string expectedIssue)
        {
            BuildAndVerify(fileName, expectedIssue);
        }

        [Theory]
        [InlineData("Entry 1 is not a SearchParameter resource.")]
        [InlineData("SearchParameter[3].url is invalid.")]
        public void GivenASearchParameterDefinitionFileWithInvalidEntries_WhenBuilt_ThenInvalidDefinitionExceptionShouldBeThrown(string expectedIssue)
        {
            BuildAndVerify(_invalidEntriesFile, expectedIssue);
        }

        [Theory]
        [InlineData("SearchParameter[http://hl7.org/fhir/SearchParameter/DocumentReference-relationship].component is null or empty.")]
        [InlineData("SearchParameter[http://hl7.org/fhir/SearchParameter/Group-characteristic-value].component[1].definition.reference is null or empty or does not refer to a valid SearchParameter resource.")]
        [InlineData("SearchParameter[http://hl7.org/fhir/SearchParameter/Observation-code-value-quantity].component[0] cannot refer to a composite SearchParameter.")]
        [InlineData("SearchParameter[http://hl7.org/fhir/SearchParameter/Observation-code-value-string].component[1].expression is null or empty.")]
        [InlineData("SearchParameter[http://hl7.org/fhir/SearchParameter/Observation-device].resource.base is not defined.")]
        [InlineData("SearchParameter[http://hl7.org/fhir/SearchParameter/Observation-related].resource.expression is null or empty.")]
        public void GivenASearchParameterDefinitionFileWithInvalidDefinitions_WhenBuilt_ThenInvalidDefinitionExceptionShouldBeThrown(string expectedIssue)
        {
            BuildAndVerify(_invalidDefinitionsFile, expectedIssue);
        }

        [Fact]
        public void GivenAValidSearchParameterDefinitionFile_WhenBuilt_ThenUriDictionaryShouldContainAllSearchParameters()
        {
            var bundle = SearchParameterDefinitionBuilder.ReadEmbeddedSearchParameters(
                _validEntriesFile,
                ModelInfoProvider.Instance,
                $"{typeof(Definitions).Namespace}.DefinitionFiles",
                typeof(EmbeddedResourceManager).Assembly);

            SearchParameterDefinitionBuilder.Build(bundle.Entries.Select(e => e.Resource).ToList(), _uriDictionary, _resourceTypeDictionary, ModelInfoProvider.Instance, _searchParameterComparer, NullLogger.Instance);

            Assert.Equal(6, _uriDictionary.Count);

            Bundle staticBundle = Definitions.GetDefinition("SearchParameters");

            Assert.Equal(
                staticBundle.Entry.Select(entry => entry.FullUrl).OrderBy(s => s, StringComparer.OrdinalIgnoreCase),
                _uriDictionary.Values.Select(value => value.Url.ToString()).OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
        }

        [Fact]
        public void GivenAValidSearchParameterDefinitionFile_WhenBuilt_ThenAllResourceTypesShouldBeIncluded()
        {
            var bundle = SearchParameterDefinitionBuilder.ReadEmbeddedSearchParameters(
                _validEntriesFile,
                ModelInfoProvider.Instance,
                $"{typeof(Definitions).Namespace}.DefinitionFiles",
                typeof(EmbeddedResourceManager).Assembly);

            SearchParameterDefinitionBuilder.Build(bundle.Entries.Select(e => e.Resource).ToList(), _uriDictionary, _resourceTypeDictionary, ModelInfoProvider.Instance, _searchParameterComparer, NullLogger.Instance);

            Assert.Equal(
                ModelInfoProvider.GetResourceTypeNames().Concat(new[] { "Resource", "DomainResource" }).OrderBy(x => x).ToArray(),
                _resourceTypeDictionary.Select(x => x.Key).OrderBy(x => x).ToArray());
        }

        [Fact]
        public void GivenAValidSearchParameterDefinitionFile_WhenBuilt_ThenCorrectListOfSearchParametersIsBuiltForEntriesWithSingleBase()
        {
            var bundle = SearchParameterDefinitionBuilder.ReadEmbeddedSearchParameters(
                _validEntriesFile,
                ModelInfoProvider.Instance,
                $"{typeof(Definitions).Namespace}.DefinitionFiles",
                typeof(EmbeddedResourceManager).Assembly);

            SearchParameterDefinitionBuilder.Build(bundle.Entries.Select(e => e.Resource).ToList(), _uriDictionary, _resourceTypeDictionary, ModelInfoProvider.Instance, _searchParameterComparer, NullLogger.Instance);
            IDictionary<string, ConcurrentQueue<SearchParameterInfo>> searchParametersDictionary = _resourceTypeDictionary[ResourceType.Account.ToString()];

            ValidateSearchParameters(
                searchParametersDictionary,
                ("_type", SearchParamType.Token, "Resource.type().name"),
                ("_id", SearchParamType.Token, "Resource.id"),
                ("balance", SearchParamType.Quantity, "Account.balance"),
                ("identifier", SearchParamType.Token, "Account.identifier"));
        }

        [Theory]
        [InlineData(ResourceType.MedicationRequest)]
        [InlineData(ResourceType.MedicationAdministration)]
        [InlineData(ResourceType.Medication)]
        [InlineData(ResourceType.MedicationDispense)]
        public void GivenAValidSearchParameterDefinitionFile_WhenBuilt_ThenCorrectListOfSearchParametersIsBuiltForEntriesWithMultipleBase(ResourceType resourceType)
        {
            var bundle = SearchParameterDefinitionBuilder.ReadEmbeddedSearchParameters(
                _validEntriesFile,
                ModelInfoProvider.Instance,
                $"{typeof(Definitions).Namespace}.DefinitionFiles",
                typeof(EmbeddedResourceManager).Assembly);

            SearchParameterDefinitionBuilder.Build(bundle.Entries.Select(e => e.Resource).ToList(), _uriDictionary, _resourceTypeDictionary, ModelInfoProvider.Instance, _searchParameterComparer, NullLogger.Instance);

            IDictionary<string, ConcurrentQueue<SearchParameterInfo>> searchParametersDictionary = _resourceTypeDictionary[resourceType.ToString()];

            ValidateSearchParameters(
                searchParametersDictionary,
                ("_type", SearchParamType.Token, "Resource.type().name"),
                ("_id", SearchParamType.Token, "Resource.id"),
                ("identifier", SearchParamType.Token, "MedicationRequest.identifier | MedicationAdministration.identifier | Medication.identifier | MedicationDispense.identifier"));
        }

        [Theory]
        [MemberData(nameof(GetSearchParameterConflictsData))]
        public void GivenSearchParametersWithConflicts_WhenBuilt_ThenResourceTypeDictionaryShouldContainSuperSetSearchParameters(
            string resourceType,
            string code,
            SearchParameter[] searchParameters,
            string[] searchParametersShouldBeAdded)
        {
            var bundle = SearchParameterDefinitionBuilder.ReadEmbeddedSearchParameters(
                _validEntriesFile,
                ModelInfoProvider.Instance,
                $"{typeof(Definitions).Namespace}.DefinitionFiles",
                typeof(EmbeddedResourceManager).Assembly);
            SearchParameterDefinitionBuilder.Build(
                bundle.Entries.Select(e => e.Resource).ToList(),
                _uriDictionary,
                _resourceTypeDictionary,
                ModelInfoProvider.Instance,
                _searchParameterComparer,
                NullLogger.Instance);

            SearchParameterDefinitionBuilder.Build(
                searchParameters.Select(x => x.ToTypedElement()).ToList(),
                _uriDictionary,
                _resourceTypeDictionary,
                ModelInfoProvider.Instance,
                _searchParameterComparer,
                NullLogger.Instance);
            Assert.Equal(searchParametersShouldBeAdded.Length, _resourceTypeDictionary[resourceType][code].Count);

            int i = 0;
            while (_resourceTypeDictionary[resourceType][code].Any())
            {
                if (_resourceTypeDictionary[resourceType][code].TryDequeue(out var sp))
                {
                    Assert.Equal(searchParametersShouldBeAdded[i++], sp.Url.OriginalString);
                }
                else
                {
                    Assert.Fail("TryDequeue failed.");
                }
            }
        }

        [Theory]
        [MemberData(nameof(GetSearchParameterConflictsDataForReject))]
        public void GivenSearchParametersWithConflicts_WhenBuilt_ThenConflictingSearchParametersShouldBeRejected(
            string resourceType,
            string code,
            SearchParameter[] searchParameters,
            SearchParameter[] conflictingSearchParameters)
        {
            SearchParameterDefinitionBuilder.Build(
                searchParameters.Select(x => x.ToTypedElement()).ToList(),
                _uriDictionary,
                _resourceTypeDictionary,
                ModelInfoProvider.Instance,
                _searchParameterComparer,
                NullLogger.Instance);

            SearchParameterDefinitionBuilder.Build(
                conflictingSearchParameters.Select(x => x.ToTypedElement()).ToList(),
                _uriDictionary,
                _resourceTypeDictionary,
                ModelInfoProvider.Instance,
                _searchParameterComparer,
                NullLogger.Instance);
            Assert.Single(_resourceTypeDictionary[resourceType][code]);
            Assert.Collection(
                _resourceTypeDictionary[resourceType][code],
                x => string.Equals(searchParameters.First().Url, x.Url.OriginalString, StringComparison.OrdinalIgnoreCase));
        }

        private void BuildAndVerify(string filename, string expectedIssue)
        {
            var bundle = SearchParameterDefinitionBuilder.ReadEmbeddedSearchParameters(
                filename,
                ModelInfoProvider.Instance,
                $"{typeof(Definitions).Namespace}.DefinitionFiles",
                typeof(EmbeddedResourceManager).Assembly);

            InvalidDefinitionException ex = Assert.Throws<InvalidDefinitionException>(
                () => SearchParameterDefinitionBuilder.Build(bundle.Entries.Select(e => e.Resource).ToList(), _uriDictionary, _resourceTypeDictionary, ModelInfoProvider.Instance, _searchParameterComparer, NullLogger.Instance));

            Assert.Contains(ex.Issues, issue =>
                issue.Severity == IssueSeverity.Fatal.ToString() &&
                issue.Code == IssueType.Invalid.ToString() &&
                issue.Diagnostics.StartsWith(expectedIssue));
        }

        private void ValidateSearchParameters(
            IDictionary<string, ConcurrentQueue<SearchParameterInfo>> searchParametersDictionary,
            params (string, SearchParamType, string)[] expectedEntries)
        {
            Assert.Equal(expectedEntries.Length, searchParametersDictionary.Count);

            foreach ((string code, SearchParamType searchParameterType, string expression) in expectedEntries)
            {
                Assert.True(searchParametersDictionary.TryGetValue(code, out ConcurrentQueue<SearchParameterInfo> q));

                var searchParameter = q.FirstOrDefault();
                Assert.Equal(code, searchParameter?.Code);
                Assert.Equal(searchParameterType.ToValueSet(), searchParameter?.Type);
                Assert.Equal(expression, searchParameter?.Expression);
            }
        }

        public static IEnumerable<object[]> GetSearchParameterConflictsData()
        {
            var data = new[]
            {
                new object[]
                {
                    "Patient",
                    "us-core-race",
                    new SearchParameter[]
                    {
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp | Patient.exp1",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race1",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp | Patient.exp1 | Patient.exp2",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race2",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp | Patient.exp1 | Patient.exp2 | Patient.exp3",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race3",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp | Patient.exp1 | Patient.exp2 | Patient.exp3 | Patient.exp4",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race4",
                        },
                    },
                    new string[]
                    {
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race4",
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race3",
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race2",
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race1",
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race",
                    },
                },
                new object[]
                {
                    "Patient",
                    "us-core-race",
                    new SearchParameter[]
                    {
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp | Patient.exp1 | Patient.exp2",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race2",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp | Patient.exp1 | Patient.exp2 | Patient.exp3 | Patient.exp4",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race4",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp | Patient.exp1 | Patient.exp2 | Patient.exp3",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race3",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp | Patient.exp1",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race1",
                        },
                    },
                    new string[]
                    {
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race4",
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race3",
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race2",
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race1",
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race",
                    },
                },
                new object[]
                {
                    "Patient",
                    "us-core-race",
                    new SearchParameter[]
                    {
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp | Patient.exp1 | Patient.exp2",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race2",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp | Patient.exp1 | Patient.exp2 | Patient.exp3 | Patient.exp4",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race4",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp | Patient.exp1 | Patient.exp2 | Patient.exp3 | Patient.unknown",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race3",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp | Patient.exp1",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race1",
                        },
                    },
                    new string[]
                    {
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race4",
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race2",
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race1",
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race",
                    },
                },
                new object[]
                {
                    "Patient",
                    "us-core-race",
                    new SearchParameter[]
                    {
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp | Patient.exp1 | Patient.exp2 | Patient.exp20",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race2",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp | Patient.exp1 | Patient.exp2 | Patient.exp3 | Patient.exp4",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race4",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp | Patient.exp1 | Patient.exp2 | Patient.exp3",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race3",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp | Patient.exp1",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race1",
                        },
                    },
                    new string[]
                    {
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race2",
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race1",
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race",
                    },
                },
                new object[]
                {
                    "Patient",
                    "us-core-race",
                    new SearchParameter[]
                    {
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp1",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race1",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp2",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race2",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp3",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race3",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp4",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race4",
                        },
                    },
                    new string[]
                    {
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race",
                    },
                },
                new object[]
                {
                    "Patient",
                    "us-core-race",
                    new SearchParameter[]
                    {
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp | Patient.exp1",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race1",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp | Patient.exp1 | Patient.exp2",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race2",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp | Patient.exp1 | Patient.exp2 | Patient.exp3 | Patient.exp4",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race3",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Patient },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
                            Code = "us-core-race",
                            Expression = "Patient.exp | Patient.exp1 | Patient.exp2 | Patient.exp3 | Patient.exp4",
                            Name = "us-core-race",
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-race4",
                        },
                    },
                    new string[]
                    {
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race3",
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race4",
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race2",
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race1",
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-race",
                    },
                },
                new object[]
                {
                    "Practitioner",
                    "_id",
                    new SearchParameter[]
                    {
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Practitioner },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Practitioner },
#endif
                            Code = "_id",
                            Expression = "Practitioner.id",
                            Name = "USCorePractitionerId",
                            Type = SearchParamType.Token,
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-practitioner-id",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Practitioner },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Practitioner },
#endif
                            Code = "_id",
                            Expression = "Practitioner.id",
                            Name = "USCorePractitionerId",
                            Type = SearchParamType.Token,
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-practitioner-id1",
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.Practitioner },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Practitioner },
#endif
                            Code = "_id",
                            Expression = "Practitioner.id",
                            Name = "USCorePractitionerId",
                            Type = SearchParamType.Token,
                            Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-practitioner-id2",
                        },
                    },
                    new string[]
                    {
                        "http://hl7.org/fhir/SearchParameter/Resource-id",
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-practitioner-id",
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-practitioner-id1",
                        "http://hl7.org/fhir/us/core/SearchParameter/us-core-practitioner-id2",
                    },
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }

        public static IEnumerable<object[]> GetSearchParameterConflictsDataForReject()
        {
            var data = new[]
            {
                new object[]
                {
                    // Conficts on type.
                    "DocumentReference",
                    "relationship",
                    new SearchParameter[]
                    {
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.DocumentReference },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.DocumentReference },
#endif
                            Code = "relationship",
                            Expression = "DocumentReference.relatesTo",
                            Name = "relationship",
                            Url = "http://hl7.org/fhir/SearchParameter/DocumentReference-relationship",
                            Type = SearchParamType.Composite,
                            Component = new List<SearchParameter.ComponentComponent>
                            {
                                new SearchParameter.ComponentComponent()
                                {
                                    Definition = CreateDefinition("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"),
                                    Expression = "target",
                                },
                                new SearchParameter.ComponentComponent()
                                {
                                    Definition = CreateDefinition("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"),
                                    Expression = "code",
                                },
                            },
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.DocumentReference },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.DocumentReference },
#endif
                            Code = "relatesto",
                            Expression = "DocumentReference.relatesTo.target",
                            Name = "relatesto",
                            Url = "http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto",
                            Type = SearchParamType.Reference,
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.DocumentReference },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.DocumentReference },
#endif
                            Code = "relation",
                            Expression = "DocumentReference.relatesTo.code",
                            Name = "relation",
                            Url = "http://hl7.org/fhir/SearchParameter/DocumentReference-relation",
                            Type = SearchParamType.Token,
                        },
                    },
                    new SearchParameter[]
                    {
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.DocumentReference },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.DocumentReference },
#endif
                            Code = "relationship",
                            Expression = "DocumentReference.relatesTo",
                            Name = "relationship",
                            Url = "http://hl7.org/fhir/SearchParameter/DocumentReference-relationship0",
                            Type = SearchParamType.Token,
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.DocumentReference },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.DocumentReference },
#endif
                            Code = "relationship",
                            Expression = "DocumentReference.relatesTo",
                            Name = "relationship",
                            Url = "http://hl7.org/fhir/SearchParameter/DocumentReference-relationship1",
                        },
                    },
                },
                new object[]
                {
                    // Conficts on components.
                    "DocumentReference",
                    "relationship",
                    new SearchParameter[]
                    {
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.DocumentReference },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.DocumentReference },
#endif
                            Code = "relationship",
                            Expression = "DocumentReference.relatesTo",
                            Name = "relationship",
                            Url = "http://hl7.org/fhir/SearchParameter/DocumentReference-relationship",
                            Type = SearchParamType.Composite,
                            Component = new List<SearchParameter.ComponentComponent>
                            {
                                new SearchParameter.ComponentComponent()
                                {
                                    Definition = CreateDefinition("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"),
                                    Expression = "target",
                                },
                                new SearchParameter.ComponentComponent()
                                {
                                    Definition = CreateDefinition("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"),
                                    Expression = "code",
                                },
                            },
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.DocumentReference },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.DocumentReference },
#endif
                            Code = "relatesto",
                            Expression = "DocumentReference.relatesTo.target",
                            Name = "relatesto",
                            Url = "http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto",
                            Type = SearchParamType.Reference,
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.DocumentReference },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.DocumentReference },
#endif
                            Code = "relation",
                            Expression = "DocumentReference.relatesTo.code",
                            Name = "relation",
                            Url = "http://hl7.org/fhir/SearchParameter/DocumentReference-relation",
                            Type = SearchParamType.Token,
                        },
                    },
                    new SearchParameter[]
                    {
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.DocumentReference },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.DocumentReference },
#endif
                            Code = "relationship",
                            Expression = "DocumentReference.relatesTo",
                            Name = "relationship",
                            Url = "http://hl7.org/fhir/SearchParameter/DocumentReference-relationship0",
                            Type = SearchParamType.Composite,
                            Component = new List<SearchParameter.ComponentComponent>
                            {
                                new SearchParameter.ComponentComponent()
                                {
                                    Definition = CreateDefinition("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"),
                                    Expression = "target",
                                },
                                new SearchParameter.ComponentComponent()
                                {
                                    Definition = CreateDefinition("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"),
                                    Expression = "relation",
                                },
                            },
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.DocumentReference },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.DocumentReference },
#endif
                            Code = "relationship",
                            Expression = "DocumentReference.relatesTo",
                            Name = "relationship",
                            Url = "http://hl7.org/fhir/SearchParameter/DocumentReference-relationship2",
                            Type = SearchParamType.Composite,
                            Component = new List<SearchParameter.ComponentComponent>
                            {
                                new SearchParameter.ComponentComponent()
                                {
                                    Definition = CreateDefinition("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"),
                                    Expression = "target",
                                },
                                new SearchParameter.ComponentComponent()
                                {
                                    Definition = CreateDefinition("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"),
                                    Expression = "code",
                                },
                                new SearchParameter.ComponentComponent()
                                {
                                    Definition = CreateDefinition("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"),
                                    Expression = "target",
                                },
                            },
                        },
                        new SearchParameter()
                        {
#if R4 || R4B || Stu3
                            Base = new List<ResourceType?> { ResourceType.DocumentReference },
#else
                            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.DocumentReference },
#endif
                            Code = "relationship",
                            Expression = "DocumentReference.relatesTo",
                            Name = "relationship",
                            Url = "http://hl7.org/fhir/SearchParameter/DocumentReference-relationship3",
                            Type = SearchParamType.Composite,
                            Component = new List<SearchParameter.ComponentComponent>
                            {
                                new SearchParameter.ComponentComponent()
                                {
                                    Definition = CreateDefinition("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"),
                                    Expression = "target",
                                },
                            },
                        },
                    },
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }

#if Stu3
        private static ResourceReference CreateDefinition(string reference)
        {
            return new ResourceReference(reference);
        }
#else
        private static string CreateDefinition(string reference)
        {
            return reference;
        }
#endif
    }
}
