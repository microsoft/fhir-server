// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.SpecialParsers;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlSearchParser.SpecialParsers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SortSqlParserTests
    {
        [Fact]
        public void GivenNoSortValue_WhenCreateOrderByClause_ThenReturnsDefaultOrder()
        {
            var result = SortSqlParser.CreateOrderByClause(sortDescending: false, hasSortValue: false);

            Assert.Contains("t.IsMatch DESC", result);
            Assert.Contains("t.ResourceTypeId ASC", result);
            Assert.Contains("t.ResourceSurrogateId ASC", result);
        }

        [Fact]
        public void GivenAscendingSort_WhenCreateOrderByClause_ThenReturnsAscendingSortWithNullsLast()
        {
            var result = SortSqlParser.CreateOrderByClause(sortDescending: false, hasSortValue: true);

            Assert.Contains("t.IsMatch DESC", result);
            Assert.Contains("CASE WHEN t.SortValue IS NULL THEN 1 ELSE 0 END ASC", result);
            Assert.Contains("t.SortValue ASC", result);
            Assert.Contains("t.ResourceTypeId ASC", result);
            Assert.Contains("t.ResourceSurrogateId ASC", result);
        }

        [Fact]
        public void GivenDescendingSort_WhenCreateOrderByClause_ThenReturnsDescendingSortWithNullsLast()
        {
            var result = SortSqlParser.CreateOrderByClause(sortDescending: true, hasSortValue: true);

            Assert.Contains("t.SortValue DESC", result);
            Assert.Contains("CASE WHEN t.SortValue IS NULL THEN 1 ELSE 0 END ASC", result);
        }

        [Fact]
        public void GivenNullSortParameterName_WhenCreateSortCte_ThenReturnsNull()
        {
            var parser = new SortSqlParser(ParserTestHelper.CreateMockDefinitionManager());
            var result = InvokeCreateSortCte(parser, null, false, "cte0", "sortCte", 1, new ParserOptions());

            Assert.Null(result);
        }

        [Fact]
        public void GivenEmptySourceCteName_WhenCreateSortCte_ThenReturnsNull()
        {
            var parser = new SortSqlParser(ParserTestHelper.CreateMockDefinitionManager());
            var result = InvokeCreateSortCte(parser, "date", false, string.Empty, "sortCte", 1, new ParserOptions());

            Assert.Null(result);
        }

        [Fact]
        public void GivenStringSortContinuation_WhenCreateSortCte_ThenUsesSeparateTypedParametersExcludedFromHash()
        {
            const short patientResourceTypeId = 1;
            const short nameSearchParamId = 12;
            var parser = new SortSqlParser(CreateDefinitionManager("Patient", patientResourceTypeId, "name", SearchParamType.String, nameSearchParamId));
            using var command = new SqlCommand();
            var options = new ParserOptions
            {
                ParameterManager = new HashingSqlQueryParameterManager(
                    new SqlQueryParameterManager(command.Parameters)),
                ReuseQueryPlans = true,
            };

            var result = InvokeCreateSortCte(parser, "name", false, "cte0", "sortCte", patientResourceTypeId, options, "Smith", 321L);

            Assert.Contains("sp.Text > @p0", result, StringComparison.Ordinal);
            Assert.Contains("sp.Text =", result, StringComparison.Ordinal);
            Assert.Contains("@p1", result, StringComparison.Ordinal);
            Assert.Contains("r.ResourceSurrogateId >", result, StringComparison.Ordinal);
            Assert.DoesNotContain("Smith", result, StringComparison.Ordinal);
            Assert.DoesNotContain("'@p0'", result, StringComparison.Ordinal);
            Assert.NotSame(command.Parameters["@p0"], command.Parameters["@p1"]);
            Assert.Equal("Smith", command.Parameters["@p0"].Value);
            Assert.Equal(SqlDbType.NVarChar, command.Parameters["@p0"].SqlDbType);
            Assert.Equal("Smith", command.Parameters["@p1"].Value);
            Assert.Equal(SqlDbType.NVarChar, command.Parameters["@p1"].SqlDbType);
            Assert.Equal(321L, command.Parameters["@p2"].Value);
            Assert.Equal(SqlDbType.BigInt, command.Parameters["@p2"].SqlDbType);
            Assert.False(options.ParameterManager!.HasParametersToHash);
        }

        private static string? InvokeCreateSortCte(
            SortSqlParser parser,
            string? sortParameterName,
            bool sortDescending,
            string sourceCteName,
            string targetCteName,
            short resourceTypeId,
            ParserOptions options,
            string? continuationPoint = null,
            long? continuationResourceSurrogateId = null)
        {
            var method = typeof(SortSqlParser).GetMethod(
                nameof(SortSqlParser.CreateSortCte),
                new[]
                {
                    typeof(string),
                    typeof(bool),
                    typeof(string),
                    typeof(string),
                    typeof(short),
                    typeof(ParserOptions),
                    typeof(string),
                    typeof(long?),
                });

            Assert.NotNull(method);

            return (string?)method!.Invoke(
                parser,
                new object?[]
                {
                    sortParameterName,
                    sortDescending,
                    sourceCteName,
                    targetCteName,
                    resourceTypeId,
                    options,
                    continuationPoint,
                    continuationResourceSurrogateId,
                });
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
