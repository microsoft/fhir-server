// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;
using static Hl7.Fhir.Model.OperationOutcome;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Definition
{
    public class SearchParameterDefinitionBuilderTests
    {
        private readonly FhirJsonParser _jsonParser = new FhirJsonParser();

        private readonly SearchParameterDefinitionBuilder _builderWithInvalidEntries;
        private readonly SearchParameterDefinitionBuilder _builderWithInvalidDefinitions;
        private readonly SearchParameterDefinitionBuilder _builderWithValidEntries;

        public SearchParameterDefinitionBuilderTests()
        {
            _builderWithInvalidEntries = CreateBuilder("SearchParametersWithInvalidEntries");
            _builderWithInvalidDefinitions = CreateBuilder("SearchParametersWithInvalidDefinitions");
            _builderWithValidEntries = CreateBuilder("SearchParameters");
        }

        [Theory]
        [InlineData("SearchParametersWithInvalidBase", "bundle.entry[http://hl7.org/fhir/SearchParameter/DomainResource-text].resource.base is not defined.")]
        public void GivenAnInvalidSearchParameterDefinitionFile_WhenBuilt_ThenInvalidDefinitionExceptionShouldBeThrown(string fileName, string expectedIssue)
        {
            var builder = CreateBuilder(fileName);

            BuildAndVerify(builder, expectedIssue);
        }

        [Theory]
        [InlineData("bundle.entry[1].resource is not a SearchParameter resource.")]
        [InlineData("bundle.entry[3].url is invalid.")]
        [InlineData("A search parameter with the same definition URL 'http://hl7.org/fhir/SearchParameter/Resource-content' already exists.")]
        public void GivenASearchParameterDefinitionFileWithInvalidEntries_WhenBuilt_ThenInvalidDefinitionExceptionShouldBeThrown(string expectedIssue)
        {
            BuildAndVerify(_builderWithInvalidEntries, expectedIssue);
        }

        [Theory]
        [InlineData("bundle.entry[http://hl7.org/fhir/SearchParameter/DocumentReference-relationship].component is null or empty.")]
        [InlineData("bundle.entry[http://hl7.org/fhir/SearchParameter/Group-characteristic-value].component[1].definition.reference is null or empty or does not refer to a valid SearchParameter resource.")]
        [InlineData("bundle.entry[http://hl7.org/fhir/SearchParameter/Observation-code-value-quantity].component[0] cannot refer to a composite SearchParameter.")]
        [InlineData("bundle.entry[http://hl7.org/fhir/SearchParameter/Observation-code-value-string].component[1].expression is null or empty.")]
        [InlineData("bundle.entry[http://hl7.org/fhir/SearchParameter/Observation-device].resource.base is not defined.")]
        [InlineData("bundle.entry[http://hl7.org/fhir/SearchParameter/Observation-related].resource.expression is null or empty.")]
        public void GivenASearchParameterDefinitionFileWithInvalidDefinitions_WhenBuilt_ThenInvalidDefinitionExceptionShouldBeThrown(string expectedIssue)
        {
            BuildAndVerify(_builderWithInvalidDefinitions, expectedIssue);
        }

        [Fact]
        public void GivenAValidSearchParameterDefinitionFile_WhenBuilt_ThenUriDictionaryShouldContainAllSearchParameters()
        {
            _builderWithValidEntries.Build();

            Assert.Equal(6, _builderWithValidEntries.UriDictionary.Count);

            Bundle bundle = Definitions.GetDefinition("SearchParameters");

            Assert.Equal(
                bundle.Entry.Select(entry => entry.FullUrl),
                _builderWithValidEntries.UriDictionary.Values.Select(value => value.Url.ToString()));
        }

        [Fact]
        public void GivenAValidSearchParameterDefinitionFile_WhenBuilt_ThenAllResourceTypesShouldBeIncluded()
        {
            _builderWithValidEntries.Build();

            Assert.Equal(
                ModelInfoProvider.GetResourceTypeNames().OrderBy(x => x),
                _builderWithValidEntries.ResourceTypeDictionary.Select(x => x.Key).OrderBy(x => x).ToArray());
        }

        [Fact]
        public void GivenAValidSearchParameterDefinitionFile_WhenBuilt_ThenCorrectListOfSearchParametersIsBuiltForEntriesWithSingleBase()
        {
            _builderWithValidEntries.Build();

            IDictionary<string, SearchParameterInfo> searchParametersDictionary = _builderWithValidEntries.ResourceTypeDictionary[ResourceType.Account.ToString()];

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
        [InlineData(ResourceType.MedicationStatement)]
        [InlineData(ResourceType.MedicationDispense)]
        public void GivenAValidSearchParameterDefinitionFile_WhenBuilt_ThenCorrectListOfSearchParametersIsBuiltForEntriesWithMultipleBase(ResourceType resourceType)
        {
            _builderWithValidEntries.Build();

            IDictionary<string, SearchParameterInfo> searchParametersDictionary = _builderWithValidEntries.ResourceTypeDictionary[resourceType.ToString()];

            ValidateSearchParameters(
                searchParametersDictionary,
                ("_type", SearchParamType.Token, "Resource.type().name"),
                ("_id", SearchParamType.Token, "Resource.id"),
                ("identifier", SearchParamType.Token, "MedicationRequest.identifier | MedicationAdministration.identifier | MedicationStatement.identifier | MedicationDispense.identifier"));
        }

        private SearchParameterDefinitionBuilder CreateBuilder(string fileName)
        {
            return new SearchParameterDefinitionBuilder(
                _jsonParser,
                ModelInfoProvider.Instance,
                typeof(EmbeddedResourceManager).Assembly,
                $"{typeof(Definitions).Namespace}.DefinitionFiles.{ModelInfoProvider.Version}.{fileName}.json");
        }

        private void BuildAndVerify(SearchParameterDefinitionBuilder builder, string expectedIssue)
        {
            InvalidDefinitionException ex = Assert.Throws<InvalidDefinitionException>(() => builder.Build());

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

            foreach ((string name, SearchParamType searchParameterType, string expression) in expectedEntries)
            {
                Assert.True(searchParametersDictionary.TryGetValue(name, out SearchParameterInfo searchParameter));

                Assert.Equal(name, searchParameter.Name);
                Assert.Equal(searchParameterType.ToValueSet(), searchParameter.Type);
                Assert.Equal(expression, searchParameter.Expression);
            }
        }
    }
}
