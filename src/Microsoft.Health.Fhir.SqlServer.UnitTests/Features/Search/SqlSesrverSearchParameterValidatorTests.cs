// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlSesrverSearchParameterValidatorTests
    {
        private SearchParameterToSearchValueTypeMap _parameterToSearchValueTypeMap;
        private SqlServerSearchParameterValidator _sqlServerSearchParameterValidator;
        private SqlServerSearchParameterValidator _vectorSearchEnabledValidator;

        public SqlSesrverSearchParameterValidatorTests()
        {
            _parameterToSearchValueTypeMap = new SearchParameterToSearchValueTypeMap();
            _sqlServerSearchParameterValidator = CreateValidator(vectorSearchEnabled: false);
            _vectorSearchEnabledValidator = CreateValidator(vectorSearchEnabled: true);
        }

        [Theory]
        [MemberData(nameof(GetValidSearchParameters))]
        public void GivenASupportedType_WhenSearchParameterisValidated_ThenReturnTrue(SearchParameterInfo searchParameter)
        {
            Assert.True(_sqlServerSearchParameterValidator.ValidateSearchParameter(searchParameter, out var _));
        }

        [Theory]
        [MemberData(nameof(GetInValidSearchParameters))]
        public void GivenAnUnSupportedType_WhenSearchParameterisValidated_ThenReturnFalse(SearchParameterInfo searchParameter)
        {
            Assert.False(_sqlServerSearchParameterValidator.ValidateSearchParameter(searchParameter, out var _));
        }

        [Fact]
        public void GivenAServableVectorSearchParameter_WhenSearchParameterIsValidated_ThenReturnTrue()
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(new VectorSearchParameterConfig());

            bool result = _vectorSearchEnabledValidator.ValidateSearchParameter(searchParameter, out string errorMessage);

            Assert.True(result);
            Assert.Null(errorMessage);
        }

        [Fact]
        public void GivenAVectorSearchParameterWhileSemanticSearchIsDisabled_WhenSearchParameterIsValidated_ThenReturnFalse()
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(new VectorSearchParameterConfig());

            bool result = _sqlServerSearchParameterValidator.ValidateSearchParameter(searchParameter, out string errorMessage);

            Assert.False(result);
            Assert.Contains("semantic search is not enabled", errorMessage);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("draft")]
        [InlineData("retired")]
        public void GivenAVectorSearchParameterThatIsNotActive_WhenSearchParameterIsValidated_ThenReturnFalse(string definitionStatus)
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(new VectorSearchParameterConfig(), definitionStatus: definitionStatus);

            bool result = _vectorSearchEnabledValidator.ValidateSearchParameter(searchParameter, out string errorMessage);

            Assert.False(result);
            Assert.Contains("publication status 'active'", errorMessage);
        }

        [Fact]
        public void GivenAVectorSearchParameterWithoutAnExpression_WhenSearchParameterIsValidated_ThenReturnFalse()
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(new VectorSearchParameterConfig(), expression: null);

            bool result = _vectorSearchEnabledValidator.ValidateSearchParameter(searchParameter, out string errorMessage);

            Assert.False(result);
            Assert.Contains("must declare an expression", errorMessage);
        }

        [Fact]
        public void GivenAVectorSearchParameterWithoutBaseResourceTypes_WhenSearchParameterIsValidated_ThenReturnFalse()
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(new VectorSearchParameterConfig(), baseResourceTypes: Array.Empty<string>());

            bool result = _vectorSearchEnabledValidator.ValidateSearchParameter(searchParameter, out string errorMessage);

            Assert.False(result);
            Assert.Contains("base resource type", errorMessage);
        }

        [Fact]
        public void GivenAVectorSearchParameterThatIsNotSpecial_WhenSearchParameterIsValidated_ThenReturnFalse()
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(new VectorSearchParameterConfig(), type: ValueSets.SearchParamType.String);

            bool result = _vectorSearchEnabledValidator.ValidateSearchParameter(searchParameter, out string errorMessage);

            Assert.False(result);
            Assert.Contains("FHIR type 'special'", errorMessage);
        }

        [Fact]
        public void GivenValidOverlapOnlyOverride_WhenSearchParameterIsValidated_ThenReturnTrue()
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(new VectorSearchParameterConfig { ChunkOverlapTokens = 200 });

            bool result = _vectorSearchEnabledValidator.ValidateSearchParameter(searchParameter, out string errorMessage);

            Assert.True(result);
            Assert.Null(errorMessage);
        }

        [Fact]
        public void GivenOverlapOnlyOverrideEqualToGlobalChunkSize_WhenSearchParameterIsValidated_ThenReturnFalse()
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(new VectorSearchParameterConfig { ChunkOverlapTokens = 800 });

            bool result = _vectorSearchEnabledValidator.ValidateSearchParameter(searchParameter, out string errorMessage);

            Assert.False(result);
            Assert.Contains("chunk overlap", errorMessage);
        }

        [Fact]
        public void GivenChunkSizeOnlyOverrideNotLargerThanGlobalOverlap_WhenSearchParameterIsValidated_ThenReturnFalse()
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(new VectorSearchParameterConfig { ChunkSizeTokens = 100 });

            bool result = _vectorSearchEnabledValidator.ValidateSearchParameter(searchParameter, out string errorMessage);

            Assert.False(result);
            Assert.Contains("chunk overlap", errorMessage);
        }

        public static IEnumerable<object[]> GetValidSearchParameters()
        {
            yield return new object[] { new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Date) };
            yield return new object[] { new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Token) };
            yield return new object[] { new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Number) };
            yield return new object[] { new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Quantity) };
            yield return new object[] { new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Reference) };
            yield return new object[] { new SearchParameterInfo("test", "test", ValueSets.SearchParamType.String) };
            yield return new object[] { new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Uri) };

            var components = new List<SearchParameterComponentInfo>();
            var component = new SearchParameterComponentInfo();
            component.ResolvedSearchParameter = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Token);
            components.Add(component);
            component = new SearchParameterComponentInfo();
            component.ResolvedSearchParameter = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Quantity);
            components.Add(component);
            var compositeParam = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Composite, components: components);
            yield return new object[] { compositeParam };
        }

        public static IEnumerable<object[]> GetInValidSearchParameters()
        {
            yield return new object[] { new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Special) };
            yield return new object[]
            {
                new SearchParameterInfo(
                    "test",
                    "test",
                    ValueSets.SearchParamType.String,
                    vectorConfig: new VectorSearchParameterConfig()),
            };

            var components = new List<SearchParameterComponentInfo>();
            var component = new SearchParameterComponentInfo();
            component.ResolvedSearchParameter = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.String);
            components.Add(component);
            component = new SearchParameterComponentInfo();
            component.ResolvedSearchParameter = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Quantity);
            components.Add(component);
            var compositeParam = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Composite, components: components);
            yield return new object[] { compositeParam };
        }

        private SqlServerSearchParameterValidator CreateValidator(bool vectorSearchEnabled)
        {
            return new SqlServerSearchParameterValidator(
                _parameterToSearchValueTypeMap,
                Options.Create(new VectorSearchConfiguration { Enabled = vectorSearchEnabled }));
        }

        private static SearchParameterInfo CreateVectorSearchParameter(
            VectorSearchParameterConfig vectorConfig,
            ValueSets.SearchParamType type = ValueSets.SearchParamType.Special,
            string definitionStatus = "active",
            string expression = "Observation.note.text",
            IReadOnlyList<string> baseResourceTypes = null)
        {
            return new SearchParameterInfo(
                "ObservationNoteVector",
                "note-vector",
                type,
                new Uri("http://example.org/SearchParameter/observation-note-vector"),
                expression: expression,
                baseResourceTypes: baseResourceTypes ?? new[] { "Observation" },
                vectorConfig: vectorConfig,
                definitionStatus: definitionStatus);
        }
    }
}
