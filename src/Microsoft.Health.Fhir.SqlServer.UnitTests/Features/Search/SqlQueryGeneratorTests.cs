// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Xunit.Sdk;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Search)]
public class SqlQueryGeneratorTests
{
    private readonly ISqlServerFhirModel _fhirModel;
    private readonly SearchParamTableExpressionQueryGeneratorFactory _queryGeneratorFactory;
    private readonly SchemaInformation _schemaInformation = new(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
    private readonly IndentedStringBuilder _strBuilder = new(new StringBuilder());
    private readonly SqlQueryGenerator _queryGenerator;

    public SqlQueryGeneratorTests()
    {
        _fhirModel = Substitute.For<ISqlServerFhirModel>();

        // Create real instances instead of mocking since factory is internal
        var searchParameterToSearchValueTypeMap = new SearchParameterToSearchValueTypeMap();
        _queryGeneratorFactory = new SearchParamTableExpressionQueryGeneratorFactory(searchParameterToSearchValueTypeMap);

        _schemaInformation.Current = SchemaVersionConstants.Max;

        using Data.SqlClient.SqlCommand command = new();
        HashingSqlQueryParameterManager parameters = new(new SqlQueryParameterManager(command.Parameters));

        _queryGenerator = new(
            _strBuilder,
            parameters,
            _fhirModel,
            _schemaInformation,
            _queryGeneratorFactory,
            false,
            false);
    }

    [Fact]
    public void GivenASearchTypeLatestResources_WhenSqlGenerated_ThenSqlFiltersForLatestOnly()
    {
        Expression predicate = Expression.And([new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Patient", false))]);
        SqlRootExpression sqlExpression = new([new(null, predicate, SearchParamTableExpressionKind.All)], new List<SearchParameterExpressionBase>());
        SearchOptions searchOptions = new()
        {
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        var output = _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        Assert.Contains("IsHistory = 0", _strBuilder.ToString());
        Assert.Contains("IsDeleted = 0", _strBuilder.ToString());
    }

    [Fact]
    public void GivenASearchTypeForSoftDeletedOnly_WhenSqlGenerated_ThenFilterForSoftDeletedInSql()
    {
        Expression predicate = Expression.And([new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Patient", false))]);
        SqlRootExpression sqlExpression = new([new(null, predicate, SearchParamTableExpressionKind.All)], new List<SearchParameterExpressionBase>());
        SearchOptions searchOptions = new()
        {
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.SoftDeleted,
        };

        var output = _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        Assert.Contains("IsDeleted = 1", _strBuilder.ToString());
    }

    [Fact]
    public void GivenASearchTypeForHistoryOnly_WhenSqlGenerated_ThenFilterForHistoryInSql()
    {
        Expression predicate = Expression.And([new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Patient", false))]);
        SqlRootExpression sqlExpression = new([new(null, predicate, SearchParamTableExpressionKind.All)], new List<SearchParameterExpressionBase>());
        SearchOptions searchOptions = new()
        {
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.History,
        };

        var output = _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        Assert.Contains("History = 1", _strBuilder.ToString());
    }

    [Fact]
    public void GivenASearchTypeForLatestHistorySoftDeleted_WhenSqlGenerated_ThenFiltersArentInSql()
    {
        Expression predicate = Expression.And([new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Patient", false))]);
        SqlRootExpression sqlExpression = new([new(null, predicate, SearchParamTableExpressionKind.All)], new List<SearchParameterExpressionBase>());
        SearchOptions searchOptions = new()
        {
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest | ResourceVersionType.History | ResourceVersionType.SoftDeleted,
        };

        var output = _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        Assert.DoesNotContain("IsHistory =", _strBuilder.ToString());
        Assert.DoesNotContain("IsDeleted =", _strBuilder.ToString());
    }

    [Fact]
    public void GivenASearchTypeForHistorySoftDeleted_WhenSqlGenerated_ThenSqlFiltersOutLatest()
    {
        Expression predicate = Expression.And([new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Patient", false))]);
        SqlRootExpression sqlExpression = new([new(null, predicate, SearchParamTableExpressionKind.All)], new List<SearchParameterExpressionBase>());
        SearchOptions searchOptions = new()
        {
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.History | ResourceVersionType.SoftDeleted,
        };

        var output = _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        Assert.Contains("IsHistory = 1", _strBuilder.ToString());
        Assert.Contains("IsDeleted = 1", _strBuilder.ToString());
    }

    [Fact]
    public void GivenPreparedVectorQueryAndStructuredCandidates_WhenSqlGenerated_ThenRanksBeforePagination()
    {
        // Arrange
        var vectorSearchParameter = new SearchParameterInfo(
            name: "SemanticText",
            code: "semantic-text",
            searchParamType: SearchParamType.Special,
            url: new Uri("https://example.org/fhir/SearchParameter/semantic-text"));
        _fhirModel.GetSearchParamId(vectorSearchParameter.Url).Returns((short)71);

        Expression predicate = Expression.And(
            [new SearchParameterExpression(
                new SearchParameterInfo("_type", "_type"),
                new StringExpression(StringOperator.Equals, FieldName.String, null, "Patient", false))]);
        var sqlExpression = new SqlRootExpression(
            [new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All)],
            new List<SearchParameterExpressionBase>());
        var searchOptions = new SqlSearchOptions(new SearchOptions
        {
            MaxItemCount = 10,
            SearchParameters = Array.Empty<SearchParameterInfo>(),
            UnsupportedSearchParams = Array.Empty<Tuple<string, string>>(),
            Sort = Array.Empty<(SearchParameterInfo, SortOrder)>(),
            ResourceVersionTypes = ResourceVersionType.Latest,
        })
        {
            PreparedVectorQuery = new PreparedVectorSearchQuery(
                vectorSearchParameter,
                embeddingModelId: 3,
                Enumerable.Repeat(0.25f, VectorSearchConfiguration.SupportedDimensions).ToArray(),
                minimumScore: 0.65m),
        };

        // Act
        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string generatedSql = _strBuilder.ToString();

        // Assert
        int candidateJoinIndex = generatedSql.IndexOf("JOIN cte0", StringComparison.Ordinal);
        int vectorApplyIndex = generatedSql.IndexOf("CROSS APPLY", StringComparison.Ordinal);

        Assert.True(candidateJoinIndex >= 0, generatedSql);
        Assert.True(vectorApplyIndex > candidateJoinIndex, generatedSql);
        Assert.Contains("SELECT TOP (", generatedSql, StringComparison.Ordinal);
        Assert.Contains("dbo.VectorSearchParam", generatedSql, StringComparison.Ordinal);
        Assert.Contains("VECTOR_DISTANCE(", generatedSql, StringComparison.Ordinal);
        Assert.Contains("AS VECTOR(1536)", generatedSql, StringComparison.Ordinal);
        Assert.Contains("semantic.SemanticDistance", generatedSql, StringComparison.Ordinal);
        Assert.Contains("semantic.SemanticChunkOrdinal", generatedSql, StringComparison.Ordinal);
        Assert.Contains("semantic.SemanticChunkText", generatedSql, StringComparison.Ordinal);
        Assert.Contains("AS SemanticChunkOrdinal", generatedSql, StringComparison.Ordinal);
        Assert.Contains("AS SemanticChunkText", generatedSql, StringComparison.Ordinal);
        Assert.Contains("AS SemanticSourceResourceTypeId", generatedSql, StringComparison.Ordinal);
        Assert.Contains("AS SemanticSourceResourceId", generatedSql, StringComparison.Ordinal);
        Assert.Contains("AS SemanticSourceResourceVersion", generatedSql, StringComparison.Ordinal);
        Assert.Contains("AS SemanticSourcePath", generatedSql, StringComparison.Ordinal);
        Assert.Contains("AS SemanticEvidenceJson", generatedSql, StringComparison.Ordinal);
        Assert.Contains("FOR JSON PATH", generatedSql, StringComparison.Ordinal);
        Assert.Contains("))) <= ", generatedSql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY SemanticDistance ASC", generatedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("[0.25,0.25", generatedSql, StringComparison.Ordinal);

        // The Top CTE that normally carries IsMatch/IsPartial is suppressed for vector search,
        // so the outer projection must emit constant match bits instead of reading them from the last CTE.
        Assert.Contains("CAST(1 AS bit) AS IsMatch", generatedSql, StringComparison.Ordinal);
        Assert.Contains("CAST(0 AS bit) AS IsPartial", generatedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("CAST(IsMatch AS bit)", generatedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("CAST(IsPartial AS bit)", generatedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenPreparedVectorQueryAndScoreSort_WhenSqlGenerated_ThenRanksByDistanceWithoutSortValueLookup()
    {
        // Arrange
        var vectorSearchParameter = new SearchParameterInfo(
            name: "SemanticText",
            code: "semantic-text",
            searchParamType: SearchParamType.Special,
            url: new Uri("https://example.org/fhir/SearchParameter/semantic-text"));
        _fhirModel.GetSearchParamId(vectorSearchParameter.Url).Returns((short)71);
        Expression predicate = Expression.And(
            [new SearchParameterExpression(
                new SearchParameterInfo("_type", "_type"),
                new StringExpression(StringOperator.Equals, FieldName.String, null, "Patient", false))]);
        var sqlExpression = new SqlRootExpression(
            [new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All)],
            new List<SearchParameterExpressionBase>());
        var searchOptions = CreateVectorSearchOptions(
            vectorSearchParameter,
            [
                (SearchParameterInfo.ScoreSearchParameter, SortOrder.Ascending),
                (SearchParameterInfo.ResourceTypeSearchParameter, SortOrder.Ascending),
                (new SearchParameterInfo(SearchParameterNames.LastUpdated, SearchParameterNames.LastUpdated), SortOrder.Ascending),
            ]);

        // Act
        Expression rewritten = new SortRewriter(_queryGeneratorFactory).VisitSqlRoot(sqlExpression, searchOptions);
        _queryGenerator.VisitSqlRoot((SqlRootExpression)rewritten, searchOptions);
        string generatedSql = _strBuilder.ToString();

        // Assert
        Assert.Same(sqlExpression, rewritten);
        Assert.Contains("ORDER BY SemanticDistance ASC", generatedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("SortValue", generatedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenPreparedVectorQueryAndSemanticCursor_WhenSqlGenerated_ThenContinuesAfterDistanceAndStableKeys()
    {
        // Arrange
        var vectorSearchParameter = new SearchParameterInfo(
            name: "SemanticText",
            code: "semantic-text",
            searchParamType: SearchParamType.Special,
            url: new Uri("https://example.org/fhir/SearchParameter/semantic-text"));
        _fhirModel.GetSearchParamId(vectorSearchParameter.Url).Returns((short)71);
        Expression predicate = Expression.And(
            [new SearchParameterExpression(
                new SearchParameterInfo("_type", "_type"),
                new StringExpression(StringOperator.Equals, FieldName.String, null, "Patient", false))]);
        var sqlExpression = new SqlRootExpression(
            [new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All)],
            new List<SearchParameterExpressionBase>());
        var searchOptions = CreateVectorSearchOptions(
            vectorSearchParameter,
            [(SearchParameterInfo.ScoreSearchParameter, SortOrder.Ascending)]);
        searchOptions.SemanticContinuationDistance = 0.125;
        searchOptions.SemanticContinuationResourceTypeId = 103;
        searchOptions.SemanticContinuationResourceSurrogateId = 12345;

        // Act
        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string generatedSql = _strBuilder.ToString();

        // Assert
        Assert.Contains("semantic.SemanticDistance >", generatedSql, StringComparison.Ordinal);
        Assert.Contains("semantic.SemanticDistance =", generatedSql, StringComparison.Ordinal);
        Assert.Contains("ResourceTypeId >", generatedSql, StringComparison.Ordinal);
        Assert.Contains("ResourceSurrogateId >", generatedSql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY SemanticDistance ASC", generatedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenPreparedVectorQueryAndLastUpdatedSort_WhenSqlGenerated_ThenRequestedSortOverridesRelevanceOrder()
    {
        // Arrange
        var vectorSearchParameter = new SearchParameterInfo(
            name: "SemanticText",
            code: "semantic-text",
            searchParamType: SearchParamType.Special,
            url: new Uri("https://example.org/fhir/SearchParameter/semantic-text"));
        _fhirModel.GetSearchParamId(vectorSearchParameter.Url).Returns((short)71);

        Expression predicate = Expression.And(
            [new SearchParameterExpression(
                new SearchParameterInfo("_type", "_type"),
                new StringExpression(StringOperator.Equals, FieldName.String, null, "Patient", false))]);
        var sqlExpression = new SqlRootExpression(
            [new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All)],
            new List<SearchParameterExpressionBase>());
        var searchOptions = CreateVectorSearchOptions(
            vectorSearchParameter,
            [(new SearchParameterInfo(SearchParameterNames.LastUpdated, SearchParameterNames.LastUpdated), SortOrder.Descending)]);

        // Act
        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string generatedSql = _strBuilder.ToString();

        // Assert
        Assert.Contains("ResourceSurrogateId DESC", generatedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("ORDER BY SemanticDistance ASC", generatedSql, StringComparison.Ordinal);
        Assert.Contains("semantic.SemanticDistance", generatedSql, StringComparison.Ordinal);
        Assert.Contains("semantic.SemanticEvidenceJson", generatedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenPreparedVectorQueryAndDateSort_WhenSqlGenerated_ThenRequestedSortOverridesRelevanceOrder()
    {
        // Arrange
        var vectorSearchParameter = new SearchParameterInfo(
            name: "SemanticText",
            code: "semantic-text",
            searchParamType: SearchParamType.Special,
            url: new Uri("https://example.org/fhir/SearchParameter/semantic-text"));
        var dateSortParameter = new SearchParameterInfo(
            name: "date",
            code: "date",
            searchParamType: SearchParamType.Date,
            url: new Uri("https://example.org/fhir/SearchParameter/date"));
        _fhirModel.GetSearchParamId(vectorSearchParameter.Url).Returns((short)71);
        _fhirModel.GetSearchParamId(dateSortParameter.Url).Returns((short)72);

        Expression predicate = Expression.And(
            [new SearchParameterExpression(
                new SearchParameterInfo("_type", "_type"),
                new StringExpression(StringOperator.Equals, FieldName.String, null, "Patient", false))]);
        var sqlExpression = new SqlRootExpression(
            [new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All)],
            new List<SearchParameterExpressionBase>());
        var searchOptions = CreateVectorSearchOptions(vectorSearchParameter, [(dateSortParameter, SortOrder.Descending)]);
        sqlExpression = (SqlRootExpression)new SortRewriter(_queryGeneratorFactory).VisitSqlRoot(sqlExpression, searchOptions);

        // Act
        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string generatedSql = _strBuilder.ToString();

        // Assert
        Assert.Contains("SortValue DESC", generatedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("ORDER BY SemanticDistance ASC", generatedSql, StringComparison.Ordinal);
        Assert.Contains("semantic.SemanticDistance", generatedSql, StringComparison.Ordinal);
        Assert.Contains("semantic.SemanticEvidenceJson", generatedSql, StringComparison.Ordinal);
    }

    private static SqlSearchOptions CreateVectorSearchOptions(
        SearchParameterInfo vectorSearchParameter,
        IReadOnlyList<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)> sort)
    {
        return new SqlSearchOptions(new SearchOptions
        {
            MaxItemCount = 10,
            SearchParameters = Array.Empty<SearchParameterInfo>(),
            UnsupportedSearchParams = Array.Empty<Tuple<string, string>>(),
            Sort = sort,
            ResourceVersionTypes = ResourceVersionType.Latest,
        })
        {
            PreparedVectorQuery = new PreparedVectorSearchQuery(
                vectorSearchParameter,
                embeddingModelId: 3,
                Enumerable.Repeat(0.25f, VectorSearchConfiguration.SupportedDimensions).ToArray(),
                minimumScore: 0.65m),
        };
    }

    [Fact]
    public void GivenReferenceSearchParameterWithMultipleTargetTypes_WhenSqlGenerated_ThenSqlIncludesOrClauseForReferenceResourceTypeId()
    {
        // Setup mock to return resource type IDs
        _fhirModel.TryGetResourceTypeId("Patient", out Arg.Any<short>())
            .Returns(x =>
            {
                x[1] = (short)1;
                return true;
            });
        _fhirModel.TryGetResourceTypeId("Practitioner", out Arg.Any<short>())
            .Returns(x =>
            {
                x[1] = (short)2;
                return true;
            });
        _fhirModel.GetSearchParamId(Arg.Any<Uri>()).Returns((short)100);

        // Create a reference search parameter with multiple target types (like Observation.patient)
        var referenceParam = new SearchParameterInfo(
            "patient",
            "patient",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-patient"),
            null,
            "Observation.subject",
            new[] { "Patient", "Practitioner" });

        // Create expression with OR of multiple target types + IS NULL (simulating UntypedReferenceRewriter output)
        Expression predicate = Expression.SearchParameter(
            referenceParam,
            Expression.And(
                Expression.StringEquals(FieldName.ReferenceResourceId, null, "test-id", false),
                Expression.Or(
                    Expression.StringEquals(FieldName.ReferenceResourceType, null, "Patient", false),
                    Expression.StringEquals(FieldName.ReferenceResourceType, null, "Practitioner", false),
                    Expression.Missing(FieldName.ReferenceResourceType, null))));

        var queryGenerator = predicate.AcceptVisitor(_queryGeneratorFactory, null);
        SqlRootExpression sqlExpression = new([new(queryGenerator, predicate, SearchParamTableExpressionKind.Normal)], new List<SearchParameterExpressionBase>());
        SearchOptions searchOptions = new()
        {
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        string generatedSql = _strBuilder.ToString();

        // Verify the SQL contains ReferenceResourceTypeId with OR clause and IS NULL
        // This confirms that the VisitMultiary method in SearchParameterQueryGenerator
        // correctly handles the OR expression generated by UntypedReferenceRewriter
        Assert.Contains("ReferenceResourceTypeId", generatedSql);
        Assert.Contains(" OR ", generatedSql);
        Assert.Contains("IS NULL", generatedSql);

        // Verify both target resource type IDs appear as separate equality checks
        // Patient=1 and Practitioner=2, each generating "ReferenceResourceTypeId = @pN"
        int typeIdOccurrences = generatedSql.Split("ReferenceResourceTypeId").Length - 1;
        Assert.True(typeIdOccurrences >= 3, $"Expected ReferenceResourceTypeId to appear at least 3 times (2 type equality checks + 1 IS NULL), but found {typeIdOccurrences} in: {generatedSql}");

        // Verify both type IDs were passed as parameters by checking the mock was called
        _fhirModel.Received(1).TryGetResourceTypeId("Patient", out Arg.Any<short>());
        _fhirModel.Received(1).TryGetResourceTypeId("Practitioner", out Arg.Any<short>());
    }
}
