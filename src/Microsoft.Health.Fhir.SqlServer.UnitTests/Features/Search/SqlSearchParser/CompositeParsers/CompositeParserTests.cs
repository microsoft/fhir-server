// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.CompositeParsers;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlSearchParser.CompositeParsers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class CompositeParserTests
    {
        private static readonly SqlSearchParameterDefinitionManager MockDefManager =
            ParserTestHelper.CreateMockDefinitionManager();

        [Fact]
        public void GivenTokenStringComposite_WhenBuildWhereClause_ThenCombinesTokenAndStringConditions()
        {
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);
            var parser = new TokenStringCompositeSqlParser(MockDefManager);
            var result = parser.BuildWhereClause("http://sys|needle-token$needle-text", string.Empty, options);

            Assert.Contains("SystemId1 = (SELECT SystemId FROM dbo.System WHERE Value = @p0)", result);
            Assert.Contains("Code1 = @p1", result);
            Assert.Contains("Text2 like @p2", result);
            Assert.DoesNotContain("needle-token", result);
            Assert.DoesNotContain("needle-text", result);
            Assert.Equal("http://sys", command.Parameters["@p0"].Value);
            Assert.Equal("needle-token", command.Parameters["@p1"].Value);
            Assert.Equal("needle-text%", command.Parameters["@p2"].Value);
        }

        [Fact]
        public void GivenTokenTokenComposite_WhenBuildWhereClause_ThenCombinesTwoTokenConditions()
        {
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);
            var parser = new TokenTokenCompositeSqlParser(MockDefManager);
            var result = parser.BuildWhereClause("first-code$second-code", string.Empty, options);

            Assert.Contains("Code1 = @p0", result);
            Assert.Contains("Code2 = @p1", result);
            Assert.Equal("first-code", command.Parameters["@p0"].Value);
            Assert.Equal("second-code", command.Parameters["@p1"].Value);
        }

        [Fact]
        public void GivenTokenDateTimeComposite_WhenBuildWhereClause_ThenCombinesTokenAndDateConditions()
        {
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);
            var parser = new TokenDateTimeCompositeSqlParser(MockDefManager);
            var result = parser.BuildWhereClause("date-token$2024-01-15", string.Empty, options);

            Assert.Contains("Code1 = @p0", result);
            Assert.Contains("DateTime2", result);
            Assert.Equal("date-token", command.Parameters["@p0"].Value);
            Assert.NotNull(command.Parameters["@p1"].Value);
        }

        [Fact]
        public void GivenTokenQuantityComposite_WhenBuildWhereClause_ThenCombinesTokenAndQuantityConditions()
        {
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);
            var parser = new TokenQuantityCompositeSqlParser(MockDefManager);
            var result = parser.BuildWhereClause("quantity-token$gt100", string.Empty, options);

            Assert.Contains("Code1 = @p0", result);
            Assert.Contains("HighValue2 > @p1", result);
            Assert.Equal("quantity-token", command.Parameters["@p0"].Value);
            Assert.Equal(100m, command.Parameters["@p1"].Value);
        }

        [Fact]
        public void GivenTokenNumberNumberComposite_WhenBuildWhereClause_ThenCombinesThreeComponents()
        {
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);
            var parser = new TokenNumberNumberCompositeSqlParser(MockDefManager);
            var result = parser.BuildWhereClause("range-token$gt100$lt200", string.Empty, options);

            Assert.Contains("Code1 = @p0", result);
            Assert.Contains("HighValue2 > @p1", result);
            Assert.Contains("LowValue3 < @p2", result);
            Assert.DoesNotContain("range-token", result);
            Assert.Equal("range-token", command.Parameters["@p0"].Value);
            Assert.Equal(100m, command.Parameters["@p1"].Value);
            Assert.Equal(200m, command.Parameters["@p2"].Value);
        }

        [Fact]
        public void GivenTwoComponentComposite_WhenValueHasNoDollarSign_ThenThrows()
        {
            var parser = new TokenStringCompositeSqlParser(MockDefManager);
            Assert.Throws<InvalidOperationException>(() =>
                parser.BuildWhereClause("nodollarsign", string.Empty, new ParserOptions()));
        }

        [Fact]
        public void GivenThreeComponentComposite_WhenValueHasOnlyOneDollarSign_ThenThrows()
        {
            var parser = new TokenNumberNumberCompositeSqlParser(MockDefManager);
            Assert.Throws<InvalidOperationException>(() =>
                parser.BuildWhereClause("code$100", string.Empty, new ParserOptions()));
        }

        [Fact]
        public void GivenReferenceTokenComposite_WhenBuildWhereClause_ThenCombinesReferenceAndTokenConditions()
        {
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);
            var fhirModel = ParserTestHelper.CreateMockFhirModel(("Patient", 1));
            var parser = new ReferenceTokenCompositeSqlParser(MockDefManager, fhirModel);
            var result = parser.BuildWhereClause("Patient/reference-id$token-value", string.Empty, options);

            Assert.Contains("ReferenceResourceId1 = @p0", result);
            Assert.Contains("ReferenceResourceTypeId1 = 1", result);
            Assert.Contains("Code2 = @p1", result);
            Assert.DoesNotContain("reference-id", result);
            Assert.DoesNotContain("token-value", result);
            Assert.Equal("reference-id", command.Parameters["@p0"].Value);
            Assert.Equal("token-value", command.Parameters["@p1"].Value);
        }

        [Fact]
        public void GivenCompositeSearchJoinInfo_WhenGetSearchJoinInfo_ThenUsesProvidedParserOptions()
        {
            var parser = new TokenStringCompositeSqlParser(CreateDefinitionManager("Observation", 7, "combo", SearchParamType.Composite, 99));
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            var result = parser.GetSearchJoinInfo("combo", "join-token$join-text", 7, options);

            Assert.NotNull(result);
            Assert.Equal("TokenStringCompositeSearchParam", result.Value.tableName);
            Assert.Equal(99, result.Value.searchParamId);
            Assert.Contains("t_placeholder.Code1 = @p0", result.Value.whereClause);
            Assert.Contains("t_placeholder.Text2 like @p1", result.Value.whereClause);
            Assert.DoesNotContain("join-token", result.Value.whereClause);
            Assert.DoesNotContain("join-text", result.Value.whereClause);
            Assert.Equal("join-token", command.Parameters["@p0"].Value);
            Assert.Equal("join-text%", command.Parameters["@p1"].Value);
        }

        private static SqlSearchParameterDefinitionManager CreateDefinitionManager(
            string resourceTypeName,
            short resourceTypeId,
            string searchParameterCode,
            SearchParamType searchParamType,
            short searchParameterId)
        {
            var searchParameterUrl = new Uri($"http://example.org/SearchParameter/{resourceTypeName}-{searchParameterCode}");
            var searchParameter = new SearchParameterInfo(searchParameterCode, searchParameterCode, searchParamType, searchParameterUrl);
            var definitionManager = (SearchParameterDefinitionManager)RuntimeHelpers.GetUninitializedObject(typeof(SearchParameterDefinitionManager));
            definitionManager.UrlLookup = new ConcurrentDictionary<string, SearchParameterInfo>
            {
                [searchParameterUrl.OriginalString] = searchParameter,
            };
            definitionManager.TypeLookup = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentQueue<string>>>(StringComparer.OrdinalIgnoreCase)
            {
                [resourceTypeName] = new ConcurrentDictionary<string, ConcurrentQueue<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    [searchParameterCode] = new ConcurrentQueue<string>(new[] { searchParameterUrl.OriginalString }),
                },
            };
            typeof(SearchParameterDefinitionManager).GetField("_initialized", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(definitionManager, true);

            var fhirModel = ParserTestHelper.CreateMockFhirModel((resourceTypeName, resourceTypeId));
            fhirModel.GetResourceTypeName(resourceTypeId).Returns(resourceTypeName);
            fhirModel.GetSearchParamId(searchParameterUrl).Returns(searchParameterId);

            return new SqlSearchParameterDefinitionManager(definitionManager, fhirModel);
        }
    }
}
