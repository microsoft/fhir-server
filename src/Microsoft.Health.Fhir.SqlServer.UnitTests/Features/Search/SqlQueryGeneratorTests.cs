// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
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
using Microsoft.Health.Fhir.SqlServer.Registration;
using Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Xunit.Sdk;
using SqlParameter = Microsoft.Data.SqlClient.SqlParameter;
using SqlParameterCollection = Microsoft.Data.SqlClient.SqlParameterCollection;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Search)]
[Collection(ModelInfoProviderSerialCollection.Name)]
public class SqlQueryGeneratorTests : IClassFixture<ModelInfoProviderFixture>
{
    private readonly ISqlServerFhirModel _fhirModel;
    private readonly SearchParamTableExpressionQueryGeneratorFactory _queryGeneratorFactory;
    private readonly SchemaInformation _schemaInformation = new(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
    private readonly IndentedStringBuilder _strBuilder = new(new StringBuilder());
    private readonly SqlQueryGenerator _queryGenerator;
    private readonly SqlParameterCollection _sqlParameters;

    public SqlQueryGeneratorTests(ModelInfoProviderFixture modelInfoProviderFixture)
    {
        _ = modelInfoProviderFixture;
        _fhirModel = Substitute.For<ISqlServerFhirModel>();

        // Create real instances instead of mocking since factory is internal
        var searchParameterToSearchValueTypeMap = new SearchParameterToSearchValueTypeMap();
        _queryGeneratorFactory = new SearchParamTableExpressionQueryGeneratorFactory(searchParameterToSearchValueTypeMap);

        _schemaInformation.Current = SchemaVersionConstants.Max;

        using Data.SqlClient.SqlCommand command = new();
        _sqlParameters = command.Parameters;
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
        Assert.DoesNotContain("SemanticEvidence", generatedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("FOR JSON PATH", generatedSql, StringComparison.Ordinal);
        Assert.Contains("AND v.SearchParamId =", generatedSql, StringComparison.Ordinal);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GivenPreparedVectorQuery_WhenSqlGenerated_ThenSemanticContinuationIsAppliedOnlyWhenPresent(bool hasContinuation)
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
        if (hasContinuation)
        {
            Assert.True(SemanticSearchContinuationToken.TryCreate(0.125, 103, 12345, out SemanticSearchContinuationToken continuationToken));
            searchOptions.SemanticContinuationToken = continuationToken;
        }

        // Act
        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string generatedSql = _strBuilder.ToString();

        // Assert
        Assert.Contains("ORDER BY SemanticDistance ASC", generatedSql, StringComparison.Ordinal);
        if (hasContinuation)
        {
            SqlParameter distanceParameter = Assert.Single(
                _sqlParameters.Cast<SqlParameter>(), parameter => parameter.Value is double value && Math.Abs(value - 0.125) < 1e-12);
            SqlParameter resourceTypeParameter = Assert.Single(
                _sqlParameters.Cast<SqlParameter>(), parameter => parameter.Value is short value && value == 103);
            SqlParameter surrogateIdParameter = Assert.Single(
                _sqlParameters.Cast<SqlParameter>(), parameter => parameter.Value is long value && value == 12345L);

            Assert.Contains($"semantic.SemanticDistance > {distanceParameter.ParameterName}", generatedSql, StringComparison.Ordinal);
            Assert.Contains($"semantic.SemanticDistance = {distanceParameter.ParameterName}", generatedSql, StringComparison.Ordinal);
            Assert.Contains($"ResourceTypeId > {resourceTypeParameter.ParameterName}", generatedSql, StringComparison.Ordinal);
            Assert.Contains($"ResourceTypeId = {resourceTypeParameter.ParameterName}", generatedSql, StringComparison.Ordinal);
            Assert.Contains($"ResourceSurrogateId > {surrogateIdParameter.ParameterName}", generatedSql, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("semantic.SemanticDistance >", generatedSql, StringComparison.Ordinal);
            Assert.DoesNotContain("semantic.SemanticDistance =", generatedSql, StringComparison.Ordinal);
        }
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
    }

    private static SqlSearchOptions CreateVectorSearchOptions(
        SearchParameterInfo vectorSearchParameter,
        IReadOnlyList<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)> sort,
        int? approximateCandidateMultiplier = null,
        bool countOnly = false)
    {
        return new SqlSearchOptions(new SearchOptions
        {
            MaxItemCount = 10,
            CountOnly = countOnly,
            SearchParameters = Array.Empty<SearchParameterInfo>(),
            UnsupportedSearchParams = Array.Empty<Tuple<string, string>>(),
            Sort = sort,
            ResourceVersionTypes = ResourceVersionType.Latest,
        })
        {
            ApproximateVectorCandidateMultiplier = approximateCandidateMultiplier,
            PreparedVectorQuery = new PreparedVectorSearchQuery(
                vectorSearchParameter,
                embeddingModelId: 3,
                Enumerable.Repeat(0.25f, VectorSearchConfiguration.SupportedDimensions).ToArray(),
                minimumScore: 0.65m),
        };
    }

    [Fact]
    public void GivenApproximateVectorSearchAndStructuredCandidates_WhenSqlGenerated_ThenFiltersAreAppliedInsideTheApproximateSearch()
    {
        // Arrange
        SearchParameterInfo vectorSearchParameter = CreateSemanticSearchParameter();
        SqlRootExpression sqlExpression = CreatePatientTypeCandidates();
        SqlSearchOptions searchOptions = CreateVectorSearchOptions(vectorSearchParameter, [], approximateCandidateMultiplier: 5);

        // Act
        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string generatedSql = _strBuilder.ToString();

        // Assert
        Assert.StartsWith("DECLARE @SemanticQuery VECTOR(1536) = CAST(@p", generatedSql.TrimStart(), StringComparison.Ordinal);
        SqlParameter embeddingParameter = Assert.Single(
            _sqlParameters.Cast<SqlParameter>(), parameter => parameter.Value is string value && value.StartsWith("[0.25,", StringComparison.Ordinal));
        Assert.Contains($"CAST({embeddingParameter.ParameterName} AS VECTOR(1536));", generatedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("[0.25,0.25", generatedSql, StringComparison.Ordinal);

        // One page of ten plus the look-ahead row, five candidate chunks for each.
        SqlParameter candidateLimit = Assert.Single(_sqlParameters.Cast<SqlParameter>(), parameter => parameter.Value is int value && value == 55);
        Assert.Contains($"SELECT TOP ({candidateLimit.ParameterName}) WITH APPROXIMATE v.ResourceTypeId, v.ResourceSurrogateId, s.distance", generatedSql, StringComparison.Ordinal);
        Assert.Contains("FROM VECTOR_SEARCH(TABLE = dbo.VectorSearchParam AS v, COLUMN = Embedding, SIMILAR_TO = @SemanticQuery, METRIC = 'cosine') AS s", generatedSql, StringComparison.Ordinal);
        Assert.Contains("AND s.distance <=", generatedSql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY c.ResourceTypeId, c.ResourceSurrogateId", generatedSql, StringComparison.Ordinal);
        Assert.Contains(") AS semantic ON semantic.ResourceTypeId = r.ResourceTypeId AND semantic.ResourceSurrogateId = r.ResourceSurrogateId", generatedSql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY SemanticDistance ASC", generatedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("CROSS APPLY", generatedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("VECTOR_DISTANCE(", generatedSql, StringComparison.Ordinal);

        // The structured candidates must narrow the search itself, not filter its already-limited output.
        int searchStart = generatedSql.IndexOf("VECTOR_SEARCH(", StringComparison.Ordinal);
        int searchEnd = generatedSql.IndexOf("ORDER BY s.distance", StringComparison.Ordinal);
        int candidateFilter = generatedSql.IndexOf("EXISTS (SELECT 1 FROM cte0 AS f WHERE f.T1 = v.ResourceTypeId AND f.Sid1 = v.ResourceSurrogateId)", StringComparison.Ordinal);
        Assert.True(searchStart >= 0 && searchEnd > searchStart, generatedSql);
        Assert.InRange(candidateFilter, searchStart, searchEnd);
        Assert.True(generatedSql.IndexOf("DECLARE @SemanticQuery", StringComparison.Ordinal) < generatedSql.IndexOf("WITH", StringComparison.Ordinal), generatedSql);
    }

    [Fact]
    public void GivenApproximateVectorSearchAndResourceTableFilters_WhenSqlGenerated_ThenFiltersAreAppliedInsideTheApproximateSearch()
    {
        // Arrange
        SearchParameterInfo vectorSearchParameter = CreateSemanticSearchParameter();
        var sqlExpression = SqlRootExpression.WithResourceTableExpressions(
            new SearchParameterExpression(
                new SearchParameterInfo("_type", "_type"),
                new StringExpression(StringOperator.Equals, FieldName.String, null, "Patient", false)));
        SqlSearchOptions searchOptions = CreateVectorSearchOptions(vectorSearchParameter, [], approximateCandidateMultiplier: 10);

        // Act
        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string generatedSql = _strBuilder.ToString();

        // Assert
        int searchStart = generatedSql.IndexOf("VECTOR_SEARCH(", StringComparison.Ordinal);
        int searchEnd = generatedSql.IndexOf("ORDER BY s.distance", StringComparison.Ordinal);
        int filterJoin = generatedSql.IndexOf("INNER JOIN dbo.Resource AS rr ON rr.ResourceTypeId = v.ResourceTypeId AND rr.ResourceSurrogateId = v.ResourceSurrogateId", StringComparison.Ordinal);
        int filterPredicate = generatedSql.IndexOf("rr.ResourceTypeId =", filterJoin + 1, StringComparison.Ordinal);
        Assert.True(searchStart >= 0 && searchEnd > searchStart, generatedSql);
        Assert.InRange(filterJoin, searchStart, searchEnd);
        Assert.InRange(filterPredicate, searchStart, searchEnd);
        Assert.DoesNotContain("EXISTS (SELECT 1 FROM", generatedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenApproximateVectorSearchWithContinuation_WhenSqlGenerated_ThenIndexSkipsEarlierPagesAndOuterQueryAppliesTheExactBoundary()
    {
        // Arrange
        SearchParameterInfo vectorSearchParameter = CreateSemanticSearchParameter();
        SqlRootExpression sqlExpression = CreatePatientTypeCandidates();
        SqlSearchOptions searchOptions = CreateVectorSearchOptions(vectorSearchParameter, [(SearchParameterInfo.ScoreSearchParameter, SortOrder.Ascending)], approximateCandidateMultiplier: 5);
        Assert.True(SemanticSearchContinuationToken.TryCreate(0.125, 103, 12345, out SemanticSearchContinuationToken continuationToken));
        searchOptions.SemanticContinuationToken = continuationToken;

        // Act
        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string generatedSql = _strBuilder.ToString();

        // Assert
        SqlParameter[] distanceParameters = _sqlParameters.Cast<SqlParameter>()
            .Where(parameter => parameter.Value is double value && Math.Abs(value - 0.125) < 1e-12)
            .ToArray();
        Assert.Equal(2, distanceParameters.Length);
        Assert.Contains(distanceParameters, parameter => generatedSql.Contains($"AND s.distance >= {parameter.ParameterName}", StringComparison.Ordinal));
        Assert.Contains(distanceParameters, parameter => generatedSql.Contains($"semantic.SemanticDistance > {parameter.ParameterName}", StringComparison.Ordinal));
        Assert.True(generatedSql.IndexOf("AND s.distance >=", StringComparison.Ordinal) < generatedSql.IndexOf("ORDER BY s.distance", StringComparison.Ordinal), generatedSql);

        // A resource whose best chunk precedes the boundary was served on an earlier page and must not return with a worse chunk.
        SqlParameter outerBoundary = Assert.Single(distanceParameters, parameter => generatedSql.Contains($"semantic.SemanticDistance > {parameter.ParameterName}", StringComparison.Ordinal));
        Assert.Contains("NOT EXISTS (SELECT 1 FROM dbo.VectorSearchParam AS b", generatedSql, StringComparison.Ordinal);
        Assert.Contains($"VECTOR_DISTANCE('cosine', b.Embedding, @SemanticQuery) < {outerBoundary.ParameterName})", generatedSql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void GivenApproximateVectorSearchThatMustSeeEveryMatch_WhenSqlGenerated_ThenRanksExactly(bool countOnly, bool explicitSort)
    {
        // Arrange
        SearchParameterInfo vectorSearchParameter = CreateSemanticSearchParameter();
        SqlRootExpression sqlExpression = CreatePatientTypeCandidates();
        SqlSearchOptions searchOptions = CreateVectorSearchOptions(
            vectorSearchParameter,
            explicitSort ? [(new SearchParameterInfo(SearchParameterNames.LastUpdated, SearchParameterNames.LastUpdated), SortOrder.Descending)] : [],
            approximateCandidateMultiplier: 5,
            countOnly: countOnly);

        // Act
        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string generatedSql = _strBuilder.ToString();

        // Assert
        Assert.Contains("CROSS APPLY", generatedSql, StringComparison.Ordinal);
        Assert.Contains("VECTOR_DISTANCE(", generatedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("WITH APPROXIMATE", generatedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("@SemanticQuery", generatedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("VECTOR_SEARCH(", generatedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenApproximateCandidateMultiplierBeyondIntegerRange_WhenSqlGenerated_ThenCandidateLimitIsCapped()
    {
        // Arrange
        SearchParameterInfo vectorSearchParameter = CreateSemanticSearchParameter();
        SqlRootExpression sqlExpression = CreatePatientTypeCandidates();
        SqlSearchOptions searchOptions = CreateVectorSearchOptions(vectorSearchParameter, [], approximateCandidateMultiplier: int.MaxValue);

        // Act
        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        // Assert
        Assert.Single(_sqlParameters.Cast<SqlParameter>(), parameter => parameter.Value is int value && value == int.MaxValue);
    }

    private SearchParameterInfo CreateSemanticSearchParameter()
    {
        var vectorSearchParameter = new SearchParameterInfo(
            name: "SemanticText",
            code: "semantic-text",
            searchParamType: SearchParamType.Special,
            url: new Uri("https://example.org/fhir/SearchParameter/semantic-text"));
        _fhirModel.GetSearchParamId(vectorSearchParameter.Url).Returns((short)71);
        return vectorSearchParameter;
    }

    private static SqlRootExpression CreatePatientTypeCandidates()
    {
        Expression predicate = Expression.And(
            [new SearchParameterExpression(
                new SearchParameterInfo("_type", "_type"),
                new StringExpression(StringOperator.Equals, FieldName.String, null, "Patient", false))]);
        return new SqlRootExpression(
            [new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All)],
            new List<SearchParameterExpressionBase>());
    }

    [Fact]
    public void GivenGeneratedSql_WhenNormalizedQueryShapeAdded_ThenHashCommentContainsSafeQueryShapeWithoutChangingHash()
    {
        // Arrange
        const string rawValue = "Alice-Recognizable-Value";
        const string normalizedQuery = "Patient?birthdate&name";
        var (generator, sqlWithoutAnnotation) = GenerateSqlWithHashedParameter(rawValue);
        var queryHashCalculator = new SqlQueryHashCalculator();

        // Act
        string sqlWithAnnotation = generator.CalculateHashThenAddNormalizedQueryShape(
            sqlWithoutAnnotation,
            normalizedQuery,
            queryHashCalculator,
            out string queryHash);

        // Assert
        Assert.Contains($" fhir={normalizedQuery} */", sqlWithAnnotation, StringComparison.Ordinal);
        Assert.DoesNotContain(rawValue, sqlWithAnnotation, StringComparison.Ordinal);
        Assert.Equal(
            SqlServerSearchService.ExtractParameterHash(sqlWithoutAnnotation),
            SqlServerSearchService.ExtractParameterHash(sqlWithAnnotation));
        Assert.Equal(queryHashCalculator.CalculateHash(sqlWithoutAnnotation), queryHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SET STATISTICS IO ON;\r\nSET STATISTICS TIME ON;\r\nDECLARE @preamble int = 1;\r\n")]
    public void GivenQueryPlanReuse_WhenNormalizedQueryShapesDiffer_ThenShapesAreInsideStatementsWithoutChangingHash(string preamble)
    {
        // Arrange
        const string rawValue = "Alice-Recognizable-Value";
        var (generator, sqlWithoutHashComment) = GenerateSqlWithHashedParameter(rawValue, reuseQueryPlans: true, preamble: preamble);
        var queryHashCalculator = new SqlQueryHashCalculator();

        // Act
        string patientSql = generator.CalculateHashThenAddNormalizedQueryShape(
            sqlWithoutHashComment,
            "Patient?name",
            queryHashCalculator,
            out string patientQueryHash);
        string observationSql = generator.CalculateHashThenAddNormalizedQueryShape(
            sqlWithoutHashComment,
            "Observation?code",
            queryHashCalculator,
            out string observationQueryHash);

        // Assert
        Assert.DoesNotContain(SqlQueryGenerator.ParametersHashStart, sqlWithoutHashComment, StringComparison.Ordinal);
        Assert.Contains("/* fhir=Patient?name */", patientSql, StringComparison.Ordinal);
        Assert.Contains("/* fhir=Observation?code */", observationSql, StringComparison.Ordinal);
        Assert.True(patientSql.IndexOf("/* fhir=", StringComparison.Ordinal) > patientSql.IndexOf(";WITH", StringComparison.Ordinal));
        Assert.True(patientSql.IndexOf("/* fhir=", StringComparison.Ordinal) < patientSql.IndexOf("SELECT * FROM (", StringComparison.Ordinal));
        Assert.Equal(sqlWithoutHashComment, patientSql.Replace("/* fhir=Patient?name */ ", string.Empty, StringComparison.Ordinal));
        Assert.Equal(sqlWithoutHashComment, observationSql.Replace("/* fhir=Observation?code */ ", string.Empty, StringComparison.Ordinal));
        Assert.Equal(queryHashCalculator.CalculateHash(sqlWithoutHashComment), patientQueryHash);
        Assert.Equal(patientQueryHash, observationQueryHash);
        Assert.DoesNotContain(rawValue, patientSql, StringComparison.Ordinal);
        Assert.DoesNotContain(rawValue, observationSql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GivenNoParametersToHash_WhenShapeAdded_ThenSelectAndCountShapesAreInsideStatements(bool withCtes, bool countOnly)
    {
        // Arrange
        var (generator, queryText) = GenerateSqlWithHashedParameter(null, withCtes: withCtes, countOnly: countOnly);
        var calculator = Substitute.For<ISqlQueryHashCalculator>();
        calculator.CalculateHash(queryText).Returns("original-hash");

        // Act
        string annotated = generator.CalculateHashThenAddNormalizedQueryShape(queryText, "Patient", calculator, out string hash);

        // Assert
        calculator.Received(1).CalculateHash(queryText);
        Assert.Equal("original-hash", hash);
        Assert.DoesNotContain(SqlQueryGenerator.ParametersHashStart, annotated, StringComparison.Ordinal);
        Assert.Contains("/* fhir=Patient */", annotated, StringComparison.Ordinal);
        Assert.Equal(queryText, annotated.Replace("/* fhir=Patient */ ", string.Empty, StringComparison.Ordinal));
        if (withCtes)
        {
            Assert.True(annotated.IndexOf("/* fhir=", StringComparison.Ordinal) > annotated.IndexOf(";WITH", StringComparison.Ordinal));
        }
        else
        {
            Assert.StartsWith("SELECT /* fhir=Patient */ ", annotated.TrimStart(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    public void GivenEmptyShape_WhenSqlGenerated_ThenSqlAndHashAreUnchanged(bool reuseQueryPlans, string shape)
    {
        // Arrange
        var (generator, queryText) = GenerateSqlWithHashedParameter("value", reuseQueryPlans);
        var calculator = new SqlQueryHashCalculator();

        // Act
        string annotated = generator.CalculateHashThenAddNormalizedQueryShape(queryText, shape, calculator, out string hash);

        // Assert
        Assert.Equal(queryText, annotated);
        Assert.Equal(calculator.CalculateHash(queryText), hash);
    }

    [Fact]
    public void GivenSameShapeWithDifferentValuesAndNameOrder_WhenPlansReused_ThenSqlAndHashAreIdentical()
    {
        // Arrange
        var (firstGenerator, firstSql) = GenerateSqlWithHashedParameter("Alice", reuseQueryPlans: true);
        var (secondGenerator, secondSql) = GenerateSqlWithHashedParameter("Bob", reuseQueryPlans: true);
        string firstShape = FhirQueryNormalizer.Normalize("Patient", ["name", "birthdate"]);
        string secondShape = FhirQueryNormalizer.Normalize("Patient", ["birthdate", "name"]);
        var calculator = new SqlQueryHashCalculator();

        // Act
        string first = firstGenerator.CalculateHashThenAddNormalizedQueryShape(firstSql, firstShape, calculator, out string firstHash);
        string second = secondGenerator.CalculateHashThenAddNormalizedQueryShape(secondSql, secondShape, calculator, out string secondHash);

        // Assert
        Assert.Contains("/* fhir=Patient?birthdate&name */", first, StringComparison.Ordinal);
        Assert.Equal(first, second);
        Assert.Equal(firstHash, secondHash);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void GivenIncludesAndUnions_WhenShapeAdded_ThenBothStatementsAreAnnotated(
        bool reuseQueryPlans,
        bool withUnion,
        bool smartScopes)
    {
        // Arrange
        const string shape = "Observation?_include&subject";
        var referenceParameter = new SearchParameterInfo(
            "subject",
            "subject",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"),
            null,
            "Observation.subject",
            ["Patient"]);
        var include = new IncludeExpression(["Observation"], referenceParameter, "Observation", "Patient", null, false, false, false);
        var tables = new List<SearchParamTableExpression>();
        if (withUnion)
        {
            var union = Expression.Union(
                UnionOperator.All,
                [
                    Expression.SearchParameter(referenceParameter, Expression.StringEquals(FieldName.ReferenceResourceId, null, "patient-a", false)),
                    Expression.SearchParameter(referenceParameter, Expression.StringEquals(FieldName.ReferenceResourceId, null, "patient-b", false)),
                ]);
            union.IsSmartV2UnionExpressionForScopesSearchParameters = smartScopes;
            tables.Add(new SearchParamTableExpression(ReferenceQueryGenerator.Instance, Expression.And(union), SearchParamTableExpressionKind.Normal));
        }
        else
        {
            tables.Add(new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.All));
        }

        tables.Add(new SearchParamTableExpression(IncludeQueryGenerator.Instance, include, SearchParamTableExpressionKind.Include));
        tables.Add(new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.IncludeLimit));
        tables.Add(new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.IncludeUnionAll));
        ConfigureResourceTypeIds();
        _fhirModel.GetSearchParamId(referenceParameter.Url).Returns((short)40);
        using Data.SqlClient.SqlCommand command = new();
        var parameters = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters));
        parameters.AddParameter("filter-value", includeInHash: true);
        var generator = new SqlQueryGenerator(_strBuilder, parameters, _fhirModel, _schemaInformation, _queryGeneratorFactory, reuseQueryPlans, false);
        generator.VisitSqlRoot(new SqlRootExpression(tables, []), new SearchOptions { Sort = [], ResourceVersionTypes = ResourceVersionType.Latest });
        SqlCommandSimplifier.RemoveRedundantParameters(_strBuilder, command.Parameters, NullLogger.Instance);
        string queryText = _strBuilder.ToString();
        var calculator = new SqlQueryHashCalculator();

        // Act
        string annotated = generator.CalculateHashThenAddNormalizedQueryShape(queryText, shape, calculator, out string hash);

        // Assert
        var fragments = SqlServerSearchService.SplitIntoSearchFragments(annotated);
        Assert.Equal(2, fragments.Count);
        Assert.All(fragments, fragment => Assert.Contains($"fhir={shape}", fragment, StringComparison.Ordinal));
        Assert.Equal(reuseQueryPlans ? 0 : smartScopes ? 2 : 1, annotated.Split(SqlQueryGenerator.ParametersHashStart).Length - 1);
        Assert.Equal(
            queryText,
            annotated.Replace($"/* fhir={shape} */ ", string.Empty, StringComparison.Ordinal).Replace($" fhir={shape}", string.Empty, StringComparison.Ordinal));
        Assert.Equal(calculator.CalculateHash(queryText), hash);
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

    [Fact]
    public void GivenCompartmentMembership_WhenSqlGenerated_ThenReferenceIdentityUsesTheSameLocalIndexRow()
    {
        var subject = new SearchParameterInfo(
            "subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        SqlCompartmentSearchRewriter rewriter = CreateCompartmentRewriter(
            "Observation",
            new HashSet<string> { "subject" },
            new Dictionary<string, SearchParameterInfo> { ["subject"] = subject });
        _fhirModel.TryGetResourceTypeId(Arg.Any<string>(), out Arg.Any<short>())
            .Returns(call =>
            {
                call[1] = (short)1;
                return true;
            });
        _fhirModel.GetSearchParamId(subject.Url).Returns((short)100);
        Expression predicate = Assert.Single(rewriter.BuildCompartmentSearchExpressionsGroup(
            Expression.CompartmentSearch("Patient", "patient-a", new[] { "Observation" })));
        var generator = predicate.AcceptVisitor(_queryGeneratorFactory);
        SqlRootExpression root = new([new(generator, predicate, SearchParamTableExpressionKind.Normal)], []);

        _queryGenerator.VisitSqlRoot(root, new SearchOptions { Sort = [], ResourceVersionTypes = ResourceVersionType.Latest });

        string sql = _strBuilder.ToString();
        Match referenceRow = Regex.Match(sql, @"FROM dbo\.ReferenceSearchParam\r?\n(?<predicate>[\s\S]*?)\r?\n\)");
        Assert.True(referenceRow.Success, sql);
        string predicateSql = referenceRow.Groups["predicate"].Value;
        Assert.Contains("AND BaseUri IS NULL", predicateSql);
        Assert.Contains("AND ReferenceResourceTypeId = ", predicateSql);
        Assert.Contains("AND ReferenceResourceId = ", predicateSql);
        Assert.Contains("SearchParamId = ", predicateSql);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void GivenChainSourceDeduplicationConfiguration_WhenBound_ThenDefaultAndRollbackAreSupported(string value, bool expected)
    {
        // Arrange
        var settings = new Dictionary<string, string>();
        if (value != null)
        {
            settings["FhirSqlServer:EnableChainSourceDeduplication"] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var options = new FhirSqlServerConfiguration();

        // Act
        configuration.GetSection("FhirSqlServer").Bind(options);

        // Assert
        Assert.Equal(expected, options.EnableChainSourceDeduplication);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(5, false)]
    [InlineData(5, true)]
    [InlineData(1, false, false)]
    [InlineData(1, true, false)]
    [InlineData(5, false, false)]
    [InlineData(5, true, false)]
    public void GivenIndependentChains_WhenSqlGenerated_ThenPredecessorOnlyEstablishesSourceExistence(int chainCount, bool reversed, bool enabled = true)
    {
        // Arrange
        var reference = new SearchParameterInfo(
            "subject",
            "subject",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"),
            null,
            "Observation.subject",
            ["Patient"]);
        _fhirModel.GetResourceTypeId("Observation").Returns((short)10);
        _fhirModel.GetResourceTypeId("Patient").Returns((short)11);
        _fhirModel.GetSearchParamId(reference.Url).Returns((short)20);
        var tables = new List<SearchParamTableExpression>
        {
            new(null, null, SearchParamTableExpressionKind.All),
        };
        for (int i = 0; i < chainCount; i++)
        {
            tables.Add(new SearchParamTableExpression(
                ChainLinkQueryGenerator.Instance,
                new SqlChainLinkExpression(["Observation"], reference, ["Patient"], reversed),
                SearchParamTableExpressionKind.Chain,
                chainLevel: 1));
        }

        // Act
        using Data.SqlClient.SqlCommand command = new();
        var generator = new SqlQueryGenerator(
            _strBuilder,
            new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters)),
            _fhirModel,
            _schemaInformation,
            _queryGeneratorFactory,
            false,
            false,
            enableChainSourceDeduplication: enabled);
        generator.VisitSqlRoot(new SqlRootExpression(tables, []), new SearchOptions
        {
            Sort = [],
            CountOnly = true,
            ResourceVersionTypes = ResourceVersionType.Latest,
        });

        // Assert
        string sql = _strBuilder.ToString();
        string alias = reversed ? "refTarget" : "refSource";
        if (chainCount == 1)
        {
            Assert.Contains($"EXISTS (SELECT * FROM cte0 WHERE {alias}.ResourceTypeId = T1 AND {alias}.ResourceSurrogateId = Sid1)", sql);
        }
        else
        {
            for (int i = 0; i < chainCount; i++)
            {
                if (enabled)
                {
                    Assert.Contains($"JOIN (SELECT DISTINCT T1, Sid1 FROM cte{i}) predecessor ON {alias}.ResourceTypeId = T1 AND {alias}.ResourceSurrogateId = Sid1", sql);
                    Assert.DoesNotContain($"JOIN cte{i} ON {alias}.ResourceTypeId = T1", sql);
                }
                else
                {
                    Assert.Contains($"JOIN cte{i} ON {alias}.ResourceTypeId = T1 AND {alias}.ResourceSurrogateId = Sid1", sql);
                    Assert.DoesNotContain(") predecessor", sql);
                }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GivenNestedChain_WhenSqlGenerated_ThenTargetBindingIsPreserved(bool reversed)
    {
        // Arrange
        var reference = new SearchParameterInfo(
            "subject",
            "subject",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"),
            null,
            "Observation.subject",
            ["Patient"]);
        var tables = new List<SearchParamTableExpression> { new(null, null, SearchParamTableExpressionKind.All) };
        for (int level = 1; level <= 5; level++)
        {
            tables.Add(new SearchParamTableExpression(
                ChainLinkQueryGenerator.Instance,
                new SqlChainLinkExpression(["Observation"], reference, ["Patient"], reversed),
                SearchParamTableExpressionKind.Chain,
                level));
        }

        // Act
        _queryGenerator.VisitSqlRoot(new SqlRootExpression(tables, []), new SearchOptions
        {
            Sort = [],
            CountOnly = true,
            ResourceVersionTypes = ResourceVersionType.Latest,
        });

        // Assert
        string sql = _strBuilder.ToString();
        Assert.Contains("JOIN (SELECT DISTINCT T1, Sid1 FROM cte0) predecessor", sql);
        string alias = reversed ? "refTarget" : "refSource";
        for (int i = 1; i < 5; i++)
        {
            Assert.Contains($"JOIN cte{i} ON {alias}.ResourceTypeId = T2 AND {alias}.ResourceSurrogateId = Sid2", sql);
        }
    }

    private (SqlQueryGenerator Generator, string Sql) GenerateSqlWithHashedParameter(
        string parameterValue,
        bool reuseQueryPlans = false,
        string preamble = "",
        bool withCtes = true,
        bool countOnly = false)
    {
        var stringBuilder = new IndentedStringBuilder(new StringBuilder(preamble));
        using Data.SqlClient.SqlCommand command = new();
        var parameters = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters));
        if (parameterValue != null)
        {
            parameters.AddParameter(parameterValue, includeInHash: true);
        }

        var queryGenerator = new SqlQueryGenerator(
            stringBuilder,
            parameters,
            _fhirModel,
            _schemaInformation,
            _queryGeneratorFactory,
            reuseQueryPlans,
            isAsyncOperation: false);
        var sqlExpression = new SqlRootExpression(
            withCtes ? [new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.All)] : [],
            new List<SearchParameterExpressionBase>());
        var searchOptions = new SearchOptions
        {
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
            CountOnly = countOnly,
        };

        queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        SqlCommandSimplifier.RemoveRedundantParameters(stringBuilder, command.Parameters, NullLogger.Instance);

        return (queryGenerator, stringBuilder.ToString());
    }

    [Theory]
    [InlineData(null, 11)]
    [InlineData("[10,100,300,null,null,false]", 11)]
    [InlineData("[10,100,300,null,null,false,null,null,3]", 3)]
    public void GivenMatchPageScope_WhenSqlGenerated_ThenOnlyReplayedIncludesOmitTheLookaheadRow(string includesToken, int expectedLimit)
    {
        // Arrange
        using Data.SqlClient.SqlCommand command = new();
        var generator = new SqlQueryGenerator(
            _strBuilder,
            new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters)),
            _fhirModel,
            _schemaInformation,
            _queryGeneratorFactory,
            false,
            false);
        SqlRootExpression expression = new(
            [
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.All),
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top),
            ],
            []);
        SearchOptions options = new()
        {
            MaxItemCount = 10,
            IncludesContinuationToken = includesToken,
            Sort = [(new SearchParameterInfo(SearchParameterNames.LastUpdated, SearchParameterNames.LastUpdated), SortOrder.Ascending)],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        // Act
        generator.VisitSqlRoot(expression, options);

        // Assert
        var top = Regex.Match(_strBuilder.ToString(), @"SELECT DISTINCT TOP \((@\w+)\)");
        Assert.True(top.Success);
        Assert.Equal(expectedLimit, command.Parameters[top.Groups[1].Value].Value);
    }

    [Theory]
    [InlineData(false, "refTarget")]
    [InlineData(true, "refSource")]
    public void GivenSmartCompartmentInclude_WhenSqlGenerated_ThenCandidateMembershipIsCheckedBeforeBranchLimit(
        bool reversed,
        string candidateAlias)
    {
        // Arrange
        var includeParameterUrl = new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject");
        var membershipParameterUrl = new Uri("http://hl7.org/fhir/SearchParameter/DiagnosticReport-subject");
        var includeParameter = new SearchParameterInfo(
            "subject",
            "subject",
            SearchParamType.Reference,
            includeParameterUrl,
            null,
            "Observation.subject",
            ["Patient"]);
        var includeExpression = new IncludeExpression(
            ["Observation"],
            includeParameter,
            "Observation",
            "Patient",
            null,
            false,
            reversed,
            false);
        var membership = new SmartCompartmentMembershipContext(
            "Patient",
            "patient-a",
            ["Practitioner"],
            [new SmartCompartmentMembershipRule("DiagnosticReport", [membershipParameterUrl])]);
        SqlRootExpression sqlExpression = new(
            [
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.All),
                new SearchParamTableExpression(IncludeQueryGenerator.Instance, includeExpression, SearchParamTableExpressionKind.Include),
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.IncludeLimit),
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.IncludeUnionAll),
            ],
            [],
            membership);
        SearchOptions searchOptions = new()
        {
            IncludeCount = 1,
            MaxItemCount = 10,
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        ConfigureResourceTypeIds();
        _fhirModel.GetSearchParamId(includeParameterUrl).Returns((short)40);
        _fhirModel.GetSearchParamId(membershipParameterUrl).Returns((short)41);

        // Act
        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        // Assert
        string generatedSql = _strBuilder.ToString();
        Assert.Contains("smartCompartmentMembership", generatedSql);
        Assert.Contains($"smartCompartmentMembership.ResourceTypeId = {candidateAlias}.ResourceTypeId", generatedSql);
        Assert.Contains($"smartCompartmentMembership.ResourceSurrogateId = {candidateAlias}.ResourceSurrogateId", generatedSql);
        Assert.Contains("smartCompartmentMembership.BaseUri IS NULL", generatedSql);
        Assert.DoesNotContain("OPTION (RECOMPILE)", generatedSql);
        _fhirModel.Received(1).GetSearchParamId(membershipParameterUrl);
    }

    [Theory]
    [InlineData(false, "refSource.ReferenceResourceTypeId = T1 AND refTarget.ResourceSurrogateId = Sid1")]
    [InlineData(true, "refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1")]
    public void GivenWildcardScopeWithSearchParameterUnionAndInclude_WhenSqlGenerated_ThenCandidateIsFilteredBySmartScopeUnionCte(
        bool reversed,
        string expectedScopeJoinClause)
    {
        // Arrange: a wildcard SMART scope with search parameters (e.g. patient/*.read?_tag=allowed) sets
        // AllowedResourceTypesByScope to ["all"] AND emits a SMART v2 union expression. The include CTE must
        // still join against _smartv2ScopeUnionCTE so "all" is not treated as unconstrained.
        var includeParameterUrl = new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject");
        var tagParameterUrl = new Uri("http://hl7.org/fhir/SearchParameter/Resource-tag");
        var includeParameter = new SearchParameterInfo(
            "subject",
            "subject",
            SearchParamType.Reference,
            includeParameterUrl,
            null,
            "Observation.subject",
            ["Patient"]);
        var tagParameter = new SearchParameterInfo(
            "_tag",
            "_tag",
            SearchParamType.Token,
            tagParameterUrl,
            null,
            "Resource.meta.tag",
            ["Resource"]);
        var includeExpression = new IncludeExpression(
            [reversed ? "Patient" : "Observation"],
            includeParameter,
            "Observation",
            "Patient",
            null,
            false,
            reversed,
            false,
            allowedResourceTypesByScope: [KnownResourceTypes.All]);

        var individualAnd = Expression.And(Expression.SearchParameter(tagParameter, Expression.StringEquals(FieldName.TokenCode, null, "allowed", false)));
        individualAnd.IsSmartV2UnionExpressionForScopesSearchParameters = true;
        var scopeAnd = Expression.And(individualAnd);
        scopeAnd.IsSmartV2UnionExpressionForScopesSearchParameters = true;
        var smartUnion = Expression.Union(UnionOperator.All, [scopeAnd]);
        smartUnion.IsSmartV2UnionExpressionForScopesSearchParameters = true;

        SqlRootExpression sqlExpression = new(
            [
                new SearchParamTableExpression(TokenQueryGenerator.Instance, Expression.And(smartUnion), SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(IncludeQueryGenerator.Instance, includeExpression, SearchParamTableExpressionKind.Include),
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.IncludeLimit),
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.IncludeUnionAll),
            ],
            []);
        SearchOptions searchOptions = new()
        {
            IncludeCount = 10,
            MaxItemCount = 10,
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        ConfigureResourceTypeIds();
        _fhirModel.GetResourceTypeId("Patient").Returns((short)1);
        _fhirModel.GetSearchParamId(includeParameterUrl).Returns((short)40);
        _fhirModel.GetSearchParamId(tagParameterUrl).Returns((short)42);

        // Act
        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        // Assert
        string generatedSql = _strBuilder.ToString();
        Assert.Contains("refSource.SearchParamId = 40", generatedSql, StringComparison.Ordinal);
        Assert.Contains("refSource.ReferenceResourceTypeId = 1", generatedSql, StringComparison.Ordinal);
        Assert.Contains($"EXISTS (SELECT * FROM cte1 WHERE {expectedScopeJoinClause})", generatedSql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GivenIncludeWithoutSmartCompartmentMembership_WhenSqlGenerated_ThenNoCandidatePredicateIsEmitted(bool reversed)
    {
        // Arrange - same include shape as the SMART compartment test, but no membership attached
        // (non-SMART request). The candidate authorization predicate must not appear.
        var includeParameterUrl = new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject");
        var includeParameter = new SearchParameterInfo(
            "subject",
            "subject",
            SearchParamType.Reference,
            includeParameterUrl,
            null,
            "Observation.subject",
            ["Patient"]);
        var includeExpression = new IncludeExpression(
            ["Observation"],
            includeParameter,
            "Observation",
            "Patient",
            null,
            false,
            reversed,
            false);
        SqlRootExpression sqlExpression = new(
            [
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.All),
                new SearchParamTableExpression(IncludeQueryGenerator.Instance, includeExpression, SearchParamTableExpressionKind.Include),
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.IncludeLimit),
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.IncludeUnionAll),
            ],
            []);
        SearchOptions searchOptions = new()
        {
            IncludeCount = 1,
            MaxItemCount = 10,
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        ConfigureResourceTypeIds();
        _fhirModel.GetSearchParamId(includeParameterUrl).Returns((short)40);

        // Act
        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        // Assert
        string generatedSql = _strBuilder.ToString();
        Assert.DoesNotContain("smartCompartmentMembership", generatedSql);
        Assert.DoesNotContain("smartCompartmentRoot", generatedSql);
    }

    [Fact]
    public void GivenObservationCompartmentDefinition_WhenMembershipCreated_ThenFocusIsNotAMembershipParameter()
    {
        // Arrange
        var subject = new SearchParameterInfo(
            "subject",
            "subject",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"),
            null,
            "Observation.subject",
            ["Patient"]);
        var focus = new SearchParameterInfo(
            "focus",
            "focus",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-focus"),
            null,
            "Observation.focus",
            ["Patient"]);
        SqlCompartmentSearchRewriter rewriter = CreateCompartmentRewriter(
            "Observation",
            ["subject"],
            new Dictionary<string, SearchParameterInfo>
            {
                ["subject"] = subject,
                ["focus"] = focus,
            });

        // Act
        SmartCompartmentMembershipContext membership = SmartCompartmentMembershipContextFactory.Create(
            Expression.SmartCompartmentSearch("Patient", "patient-a", "Observation"),
            rewriter,
            CreateSmartRewriter());

        // Assert
        SmartCompartmentMembershipRule rule = Assert.Single(membership.MembershipRules);
        Assert.Equal("Observation", rule.ResourceType);
        Assert.Equal(subject.Url, Assert.Single(rule.SearchParameterUrls));
        Assert.DoesNotContain(focus.Url, rule.SearchParameterUrls);
    }

    [Fact]
    public void GivenACompartmentParameterWithASiblingReferenceParameter_WhenMembershipCreated_ThenOnlyTheFormalParameterIsUsed()
    {
        // Arrange - Condition-subject is a supported reference parameter targeting Patient, but the Patient
        // CompartmentDefinition nominates only `patient` for Condition. Membership must follow the formal
        // definition and must not absorb sibling parameters, which would widen the compartment.
        var clinicalPatient = new SearchParameterInfo(
            "patient",
            "patient",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/clinical-patient"),
            null,
            "Condition.subject.where(resolve() is Patient)",
            ["Patient"]);
        var subject = new SearchParameterInfo(
            "subject",
            "subject",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Condition-subject"),
            null,
            "Condition.subject",
            ["Patient"]);
        SqlCompartmentSearchRewriter rewriter = CreateCompartmentRewriter(
            "Condition",
            ["patient"],
            new Dictionary<string, SearchParameterInfo>
            {
                ["patient"] = clinicalPatient,
                ["subject"] = subject,
            });

        // Act
        SmartCompartmentMembershipContext membership = SmartCompartmentMembershipContextFactory.Create(
            Expression.SmartCompartmentSearch("Patient", "patient-a", "Condition"),
            rewriter,
            CreateSmartRewriter());

        // Assert
        SmartCompartmentMembershipRule rule = Assert.Single(membership.MembershipRules);
        Assert.Equal(clinicalPatient.Url, Assert.Single(rule.SearchParameterUrls));
        Assert.DoesNotContain(subject.Url, rule.SearchParameterUrls);
    }

    [Fact]
    public void GivenClinicalPatientParameter_WhenMembershipCreated_ThenFormalParameterIsUsed()
    {
        // Arrange
        var clinicalPatient = new SearchParameterInfo(
            "patient",
            "patient",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/clinical-patient"),
            null,
            "AllergyIntolerance.patient",
            ["Patient"]);
        SqlCompartmentSearchRewriter rewriter = CreateCompartmentRewriter(
            "AllergyIntolerance",
            ["patient"],
            new Dictionary<string, SearchParameterInfo>
            {
                ["patient"] = clinicalPatient,
            });

        // Act
        SmartCompartmentMembershipContext membership = SmartCompartmentMembershipContextFactory.Create(
            Expression.SmartCompartmentSearch("Patient", "patient-a", "AllergyIntolerance"),
            rewriter,
            CreateSmartRewriter());

        // Assert
        SmartCompartmentMembershipRule rule = Assert.Single(membership.MembershipRules);
        Assert.Equal(clinicalPatient.Url, Assert.Single(rule.SearchParameterUrls));
    }

    [Fact]
    public void GivenPractitionerCompartmentEncounter_WhenMembershipCreated_ThenOnlyTheFormalParameterIsUsed()
    {
        // Arrange - Encounter-participant is a supported reference parameter targeting Practitioner, but the
        // Practitioner CompartmentDefinition nominates only `practitioner` for Encounter. Membership must
        // follow the formal definition; absorbing `participant` would also admit RelatedPerson-keyed rows.
        var practitioner = new SearchParameterInfo(
            "practitioner",
            "practitioner",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Encounter-practitioner"),
            null,
            "Encounter.participant.individual.where(resolve() is Practitioner)",
            ["Practitioner"]);
        var participant = new SearchParameterInfo(
            "participant",
            "participant",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Encounter-participant"),
            null,
            "Encounter.participant.individual",
            ["Practitioner", "RelatedPerson"]);
        SqlCompartmentSearchRewriter rewriter = CreateCompartmentRewriter(
            "Encounter",
            ["practitioner"],
            new Dictionary<string, SearchParameterInfo>
            {
                ["practitioner"] = practitioner,
                ["participant"] = participant,
            },
            CompartmentType.Practitioner);

        // Act
        SmartCompartmentMembershipContext membership = SmartCompartmentMembershipContextFactory.Create(
            Expression.SmartCompartmentSearch("Practitioner", "practitioner-a", "Encounter"),
            rewriter,
            CreateSmartRewriter());

        // Assert
        SmartCompartmentMembershipRule rule = Assert.Single(membership.MembershipRules);
        Assert.Equal("Encounter", rule.ResourceType);
        Assert.Equal(practitioner.Url, Assert.Single(rule.SearchParameterUrls));
        Assert.DoesNotContain(participant.Url, rule.SearchParameterUrls);
    }

    [Fact]
    public void GivenEpisodeOfCareCareManager_WhenPractitionerMembershipCreated_ThenFormalParameterIsUsed()
    {
        // Arrange - EpisodeOfCare-care-manager is the sole Practitioner compartment parameter for
        // EpisodeOfCare. It is resolve()-based, which the indexer evaluates via
        // LightweightReferenceToElementResolver, so it is materialized like any other reference parameter.
        var careManager = new SearchParameterInfo(
            "care-manager",
            "care-manager",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/EpisodeOfCare-care-manager"),
            null,
            "EpisodeOfCare.careManager.where(resolve() is Practitioner)",
            ["Practitioner"]);
        SqlCompartmentSearchRewriter rewriter = CreateCompartmentRewriter(
            "EpisodeOfCare",
            ["care-manager"],
            new Dictionary<string, SearchParameterInfo>
            {
                ["care-manager"] = careManager,
            },
            CompartmentType.Practitioner);

        // Act
        SmartCompartmentMembershipContext membership = SmartCompartmentMembershipContextFactory.Create(
            Expression.SmartCompartmentSearch("Practitioner", "practitioner-a", "EpisodeOfCare"),
            rewriter,
            CreateSmartRewriter());

        // Assert
        SmartCompartmentMembershipRule rule = Assert.Single(membership.MembershipRules);
        Assert.Equal(careManager.Url, Assert.Single(rule.SearchParameterUrls));
    }

    [Fact]
    public void GivenSmartCompartmentExpressionInsideAndTree_WhenMembershipCreatedAndSqlGenerated_ThenIncludeCandidatePredicateIsEmitted()
    {
        // Canary for the SMART include enforcement chain. SqlServerSearchService derives the membership
        // context by locating the SmartCompartmentSearchExpression inside the core expression tree and
        // attaching the result to the SqlRootExpression; the query generator emits the candidate
        // authorization predicate only when that context is present. If any link breaks — the compartment
        // expression gets wrapped in a node the factory cannot traverse, a rewriter drops the attached
        // context, or the generator stops honoring it — include CTEs silently revert to the pre-fix
        // cross-compartment leak. This test exercises the factory -> attach -> generate chain end to end
        // with the same tree shape SearchOptionsFactory produces (smart node as a child of the top-level And).
        var includeParameterUrl = new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject");
        var membershipParameterUrl = new Uri("http://hl7.org/fhir/SearchParameter/DiagnosticReport-subject");
        var membershipParameter = new SearchParameterInfo(
            "subject",
            "subject",
            SearchParamType.Reference,
            membershipParameterUrl,
            null,
            "DiagnosticReport.subject",
            ["Patient"]);
        SqlCompartmentSearchRewriter rewriter = CreateCompartmentRewriter(
            "DiagnosticReport",
            ["subject"],
            new Dictionary<string, SearchParameterInfo>
            {
                ["subject"] = membershipParameter,
            });

        Expression coreExpression = Expression.And(
            new SearchParameterExpression(new SearchParameterInfo("_type", "_type"), new StringExpression(StringOperator.Equals, FieldName.String, null, "Observation", false)),
            Expression.SmartCompartmentSearch("Patient", "patient-a", "Observation"));

        SmartCompartmentMembershipContext membership = SmartCompartmentMembershipContextFactory.Create(coreExpression, rewriter, CreateSmartRewriter());

        Assert.NotNull(membership);
        Assert.Equal("Patient", membership.CompartmentResourceType);
        Assert.Equal("patient-a", membership.CompartmentResourceId);
        SmartCompartmentMembershipRule rule = Assert.Single(membership.MembershipRules);
        Assert.Equal("DiagnosticReport", rule.ResourceType);
        Assert.Equal(membershipParameterUrl, Assert.Single(rule.SearchParameterUrls));

        var includeParameter = new SearchParameterInfo(
            "subject",
            "subject",
            SearchParamType.Reference,
            includeParameterUrl,
            null,
            "Observation.subject",
            ["Patient"]);
        var includeExpression = new IncludeExpression(
            ["Observation"],
            includeParameter,
            "Observation",
            "Patient",
            null,
            false,
            false,
            false);
        SqlRootExpression sqlExpression = new SqlRootExpression(
            [
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.All),
                new SearchParamTableExpression(IncludeQueryGenerator.Instance, includeExpression, SearchParamTableExpressionKind.Include),
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.IncludeLimit),
                new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.IncludeUnionAll),
            ],
            []).WithSmartCompartmentMembership(membership);
        SearchOptions searchOptions = new()
        {
            IncludeCount = 1,
            MaxItemCount = 10,
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        ConfigureResourceTypeIds();
        _fhirModel.GetSearchParamId(includeParameterUrl).Returns((short)40);
        _fhirModel.GetSearchParamId(membershipParameterUrl).Returns((short)41);

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        string generatedSql = _strBuilder.ToString();
        Assert.Contains("smartCompartmentMembership.ResourceTypeId = refTarget.ResourceTypeId", generatedSql);
        Assert.Contains("smartCompartmentMembership.ResourceSurrogateId = refTarget.ResourceSurrogateId", generatedSql);
        Assert.Contains("smartCompartmentMembership.BaseUri IS NULL", generatedSql);
        _fhirModel.Received(1).GetSearchParamId(membershipParameterUrl);
    }

    [Fact]
    public void GivenTheDeviceRestrictionCannotBeEnforced_WhenMembershipCreated_ThenDeviceAuthorizesNothing()
    {
        // Fail-closed canary for the SQL include/revinclude path. When Device.patient is unavailable the
        // "unassigned device" leg cannot be trusted, so GetConditionalCompartmentRules emits a Never rule.
        // Never must be dropped from ConditionalRules (SqlQueryGenerator would otherwise treat any
        // non-HasNoReference visibility as EXISTS and authorize every Device with a patient index row)
        // while still being counted when subtracting conditionally visible types from the shared types.
        // Dropping the rule outright instead would put Device back in SharedResourceTypes — universally
        // visible, the opposite of fail closed.
        SqlCompartmentSearchRewriter compartmentRewriter = CreateCompartmentRewriter(
            "DiagnosticReport",
            ["subject"],
            new Dictionary<string, SearchParameterInfo>(StringComparer.Ordinal));

        Expression coreExpression = Expression.SmartCompartmentSearch("Patient", "patient-a", "DomainResource");

        SmartCompartmentMembershipContext membership = SmartCompartmentMembershipContextFactory.Create(
            coreExpression,
            compartmentRewriter,
            CreateDeviceRestrictedSmartRewriter(devicePatientParameterIsSearchable: false));

        Assert.NotNull(membership);
        Assert.Empty(membership.ConditionalRules);
        Assert.DoesNotContain("Device", membership.SharedResourceTypes, StringComparer.Ordinal);
    }

    [Fact]
    public void GivenTheDeviceRestrictionIsEnforceable_WhenMembershipCreated_ThenDeviceIsAuthorizedOnlyConditionally()
    {
        SqlCompartmentSearchRewriter compartmentRewriter = CreateCompartmentRewriter(
            "DiagnosticReport",
            ["subject"],
            new Dictionary<string, SearchParameterInfo>(StringComparer.Ordinal));

        Expression coreExpression = Expression.SmartCompartmentSearch("Patient", "patient-a", "DomainResource");

        SmartCompartmentMembershipContext membership = SmartCompartmentMembershipContextFactory.Create(
            coreExpression,
            compartmentRewriter,
            CreateDeviceRestrictedSmartRewriter(devicePatientParameterIsSearchable: true));

        Assert.NotNull(membership);
        Assert.DoesNotContain("Device", membership.SharedResourceTypes, StringComparer.Ordinal);
        Assert.All(membership.ConditionalRules, rule => Assert.Equal("Device", rule.ResourceType));
        Assert.Contains(membership.ConditionalRules, rule => rule.Visibility == SmartCompartmentConditionalVisibility.HasNoReference);
        Assert.Contains(membership.ConditionalRules, rule => rule.Visibility == SmartCompartmentConditionalVisibility.ReferencesCompartmentRoot);
    }

    private void ConfigureResourceTypeIds()
    {
        var resourceTypeIds = new Dictionary<string, short>(StringComparer.Ordinal)
        {
            ["Patient"] = 1,
            ["Practitioner"] = 2,
            ["Observation"] = 3,
            ["DiagnosticReport"] = 4,
        };

        _fhirModel.TryGetResourceTypeId(Arg.Any<string>(), out Arg.Any<short>())
            .Returns(call =>
            {
                call[1] = resourceTypeIds[(string)call[0]];
                return true;
            });
    }

    private static SqlCompartmentSearchRewriter CreateCompartmentRewriter(
        string resourceType,
        HashSet<string> compartmentParameterCodes,
        IReadOnlyDictionary<string, SearchParameterInfo> searchParameters,
        CompartmentType compartmentType = CompartmentType.Patient)
    {
        ICompartmentDefinitionManager compartmentDefinitionManager = Substitute.For<ICompartmentDefinitionManager>();
        compartmentDefinitionManager.TryGetResourceTypes(compartmentType, out Arg.Any<HashSet<string>>())
            .Returns(call =>
            {
                call[1] = new HashSet<string>(StringComparer.Ordinal) { resourceType };
                return true;
            });
        compartmentDefinitionManager.TryGetSearchParams(resourceType, compartmentType, out Arg.Any<HashSet<string>>())
            .Returns(call =>
            {
                call[2] = compartmentParameterCodes;
                return true;
            });

        ISearchParameterDefinitionManager searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.ResourceType)
            .Returns(new SearchParameterInfo(SearchParameterNames.ResourceType, SearchParameterNames.ResourceType, SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-type")));
        searchParameterDefinitionManager.TryGetSearchParameter(resourceType, Arg.Any<string>(), out Arg.Any<SearchParameterInfo>())
            .Returns(call =>
            {
                bool found = searchParameters.TryGetValue((string)call[1], out SearchParameterInfo parameter);
                call[2] = parameter;
                return found;
            });

        return new SqlCompartmentSearchRewriter(
            new Lazy<ICompartmentDefinitionManager>(() => compartmentDefinitionManager),
            new Lazy<ISearchParameterDefinitionManager>(() => searchParameterDefinitionManager));
    }

    // Production always supplies a SmartCompartmentSearchRewriter to the factory, so the parameter is
    // required. The SMART Device conditional-visibility rules are exercised end to end in
    // SmartCompartmentSearchRewriterTests; these membership tests assert only formal compartment
    // membership parameters, so the Device restriction is disabled here to keep the conditional-rule
    // set empty (preserving each test's original configuration and expectations).
    private static SmartCompartmentSearchRewriter CreateSmartRewriter()
    {
        ISearchParameterDefinitionManager searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        ICompartmentDefinitionManager compartmentDefinitionManager = Substitute.For<ICompartmentDefinitionManager>();

        var compartmentRewriter = new SqlCompartmentSearchRewriter(
            new Lazy<ICompartmentDefinitionManager>(() => compartmentDefinitionManager),
            new Lazy<ISearchParameterDefinitionManager>(() => searchParameterDefinitionManager));

        return new SmartCompartmentSearchRewriter(
            compartmentRewriter,
            new Lazy<ISearchParameterDefinitionManager>(() => searchParameterDefinitionManager),
            Options.Create(new CoreFeatureConfiguration { EnableSmartCompartmentDeviceRestriction = false }));
    }

    private static SmartCompartmentSearchRewriter CreateDeviceRestrictedSmartRewriter(bool devicePatientParameterIsSearchable)
    {
        var devicePatientParameter = new SearchParameterInfo(
            "patient",
            "patient",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Device-patient"),
            null,
            "Device.patient",
            ["Patient"])
        {
            IsSearchable = devicePatientParameterIsSearchable,
        };

        ISearchParameterDefinitionManager searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        searchParameterDefinitionManager.TryGetSearchParameter("Device", "patient", out Arg.Any<SearchParameterInfo>())
            .Returns(call =>
            {
                call[2] = devicePatientParameter;
                return true;
            });

        ICompartmentDefinitionManager compartmentDefinitionManager = Substitute.For<ICompartmentDefinitionManager>();

        var compartmentRewriter = new SqlCompartmentSearchRewriter(
            new Lazy<ICompartmentDefinitionManager>(() => compartmentDefinitionManager),
            new Lazy<ISearchParameterDefinitionManager>(() => searchParameterDefinitionManager));

        return new SmartCompartmentSearchRewriter(
            compartmentRewriter,
            new Lazy<ISearchParameterDefinitionManager>(() => searchParameterDefinitionManager),
            Options.Create(new CoreFeatureConfiguration { EnableSmartCompartmentDeviceRestriction = true }));
    }
}
