﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
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
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SearchParameterInfo>> _resourceTypeDictionary;

        public SearchParameterDefinitionBuilderTests()
        {
            _uriDictionary = new ConcurrentDictionary<string, SearchParameterInfo>();
            _resourceTypeDictionary = new ConcurrentDictionary<string, ConcurrentDictionary<string, SearchParameterInfo>>();
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
        [InlineData("A search parameter with the same definition URL 'http://hl7.org/fhir/SearchParameter/Resource-content' already exists.")]
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

            SearchParameterDefinitionBuilder.Build(bundle.Entries.Select(e => e.Resource).ToList(), _uriDictionary, _resourceTypeDictionary, ModelInfoProvider.Instance);

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

            SearchParameterDefinitionBuilder.Build(bundle.Entries.Select(e => e.Resource).ToList(), _uriDictionary, _resourceTypeDictionary, ModelInfoProvider.Instance);

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

            SearchParameterDefinitionBuilder.Build(bundle.Entries.Select(e => e.Resource).ToList(), _uriDictionary, _resourceTypeDictionary, ModelInfoProvider.Instance);

            IDictionary<string, SearchParameterInfo> searchParametersDictionary = _resourceTypeDictionary[ResourceType.Account.ToString()];

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

            SearchParameterDefinitionBuilder.Build(bundle.Entries.Select(e => e.Resource).ToList(), _uriDictionary, _resourceTypeDictionary, ModelInfoProvider.Instance);

            IDictionary<string, SearchParameterInfo> searchParametersDictionary = _resourceTypeDictionary[resourceType.ToString()];

            ValidateSearchParameters(
                searchParametersDictionary,
                ("_type", SearchParamType.Token, "Resource.type().name"),
                ("_id", SearchParamType.Token, "Resource.id"),
                ("identifier", SearchParamType.Token, "MedicationRequest.identifier | MedicationAdministration.identifier | Medication.identifier | MedicationDispense.identifier"));
        }

        private void BuildAndVerify(string filename, string expectedIssue)
        {
            var bundle = SearchParameterDefinitionBuilder.ReadEmbeddedSearchParameters(
                filename,
                ModelInfoProvider.Instance,
                $"{typeof(Definitions).Namespace}.DefinitionFiles",
                typeof(EmbeddedResourceManager).Assembly);

            InvalidDefinitionException ex = Assert.Throws<InvalidDefinitionException>(
                () => SearchParameterDefinitionBuilder.Build(bundle.Entries.Select(e => e.Resource).ToList(), _uriDictionary, _resourceTypeDictionary, ModelInfoProvider.Instance));

            Assert.Contains(ex.Issues, issue =>
                issue.Severity == IssueSeverity.Fatal.ToString() &&
                issue.Code == IssueType.Invalid.ToString() &&
                issue.Diagnostics.StartsWith(expectedIssue));
        }

        private void ValidateSearchParameters(
            IDictionary<string, SearchParameterInfo> searchParametersDictionary,
            params (string, SearchParamType, string)[] expectedEntries)
        {
            Assert.Equal(expectedEntries.Length, searchParametersDictionary.Count);

            foreach ((string code, SearchParamType searchParameterType, string expression) in expectedEntries)
            {
                Assert.True(searchParametersDictionary.TryGetValue(code, out SearchParameterInfo searchParameter));

                Assert.Equal(code, searchParameter.Code);
                Assert.Equal(searchParameterType.ToValueSet(), searchParameter.Type);
                Assert.Equal(expression, searchParameter.Expression);
            }
        }
    }
}
