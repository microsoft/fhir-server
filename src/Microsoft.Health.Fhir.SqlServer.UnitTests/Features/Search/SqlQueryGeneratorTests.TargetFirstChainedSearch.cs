// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.ValueSets;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search;

/// <summary>
/// Tests for target-first chained SQL query generation.
/// </summary>
public partial class SqlQueryGeneratorTests
{
    [Fact]
    public void GivenChainedReferenceSearchWithSelectiveTargetPredicate_WhenSqlGenerated_ThenTargetPredicateIsEvaluatedFirst()
    {
        (List<SearchParamTableExpression> tableExpressions, _) = CreateTargetFirstChainedSearchExpressions();
        SqlRootExpression sqlExpression = new(tableExpressions, []);
        SearchOptions searchOptions = new()
        {
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        string generatedSql = _strBuilder.ToString();
        Assert.Contains("FROM dbo.ReferenceSearchParam chainTarget", generatedSql);
        Assert.Contains("INNER LOOP JOIN dbo.Resource refTarget ON chainTarget.ResourceTypeId = refTarget.ResourceTypeId AND chainTarget.ResourceSurrogateId = refTarget.ResourceSurrogateId", generatedSql);
        Assert.Contains("refTarget.IsHistory = 0", generatedSql);
        Assert.Contains("refTarget.IsDeleted = 0", generatedSql);
        Assert.Contains("FROM cte0", generatedSql);
        Assert.Contains("INNER LOOP JOIN dbo.ReferenceSearchParam refSource ON refSource.ReferenceResourceTypeId = T2 AND refSource.ReferenceResourceId = Id2", generatedSql);
        Assert.Contains("FROM dbo.DateTimeSearchParam", generatedSql);
        Assert.Contains("INNER HASH JOIN cte1 ON ResourceTypeId = T1 AND ResourceSurrogateId = Sid1", generatedSql);
        Assert.DoesNotContain("refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId", generatedSql);
        Assert.True(
            generatedSql.IndexOf("FROM dbo.ReferenceSearchParam chainTarget", StringComparison.Ordinal) <
            generatedSql.IndexOf("INNER LOOP JOIN dbo.ReferenceSearchParam refSource", StringComparison.Ordinal));
        Assert.Equal<short>([(short)1242, (short)1273, (short)1277], _queryGenerator.SearchParamIds.Order());
    }

    [Fact]
    public void GivenChainedReferenceSearchWithPrecedingSourceFilter_WhenSqlGenerated_ThenExistingQueryShapeIsUsed()
    {
        (List<SearchParamTableExpression> tableExpressions, _) = CreateTargetFirstChainedSearchExpressions();
        var sourceFilterParam = new SearchParameterInfo(
            "status",
            "status",
            SearchParamType.Token,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        Expression sourceFilter = Expression.SearchParameter(
            sourceFilterParam,
            Expression.StringEquals(FieldName.TokenCode, null, "final", false));
        _fhirModel.GetSearchParamId(sourceFilterParam.Url).Returns((short)1278);
        tableExpressions.Insert(0, new SearchParamTableExpression(TokenQueryGenerator.Instance, sourceFilter, SearchParamTableExpressionKind.Normal));
        SqlRootExpression sqlExpression = new(tableExpressions, []);
        SearchOptions searchOptions = new()
        {
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        string generatedSql = _strBuilder.ToString();
        Assert.DoesNotContain("chainTarget", generatedSql);
        Assert.Contains("FROM dbo.ReferenceSearchParam refSource", generatedSql);
        Assert.Contains("JOIN dbo.Resource refTarget", generatedSql);
    }

    [Theory]
    [InlineData(SortOrder.Ascending, "IsMin = 1", "SortValue ASC")]
    [InlineData(SortOrder.Descending, "IsMax = 1", "SortValue DESC")]
    public void GivenSortedChainedReferenceSearch_WhenSqlGenerated_ThenTargetFirstShapePreservesSortValue(
        SortOrder sortOrder,
        string minOrMaxPredicate,
        string orderBy)
    {
        (List<SearchParamTableExpression> tableExpressions, SearchParameterInfo dateParam) = CreateTargetFirstChainedSearchExpressions();
        AddSortExpression(tableExpressions, dateParam);
        SqlRootExpression sqlExpression = new(tableExpressions, []);
        SearchOptions searchOptions = new()
        {
            Sort = [(dateParam, sortOrder)],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        string generatedSql = _strBuilder.ToString();
        Assert.Contains("FROM dbo.ReferenceSearchParam chainTarget", generatedSql);
        Assert.Contains("INNER HASH JOIN cte1 ON ResourceTypeId = T1 AND ResourceSurrogateId = Sid1", generatedSql);
        Assert.Contains("cte3 AS", generatedSql);
        Assert.Contains("StartDateTime AS SortValue", generatedSql);
        Assert.Contains("JOIN cte2 ON ResourceTypeId = cte2.T1 AND ResourceSurrogateId = cte2.Sid1", generatedSql);
        Assert.Contains(minOrMaxPredicate, generatedSql);
        Assert.Contains("cte3.SortValue", generatedSql);
        Assert.Contains(orderBy, generatedSql);
    }

    [Theory]
    [InlineData(SortOrder.Ascending, " OR StartDateTime > ")]
    [InlineData(SortOrder.Descending, " OR StartDateTime < ")]
    public void GivenSortedChainedReferenceSearchWithContinuationToken_WhenSqlGenerated_ThenTargetFirstShapePreservesPagingSemantics(
        SortOrder sortOrder,
        string sortComparison)
    {
        (List<SearchParamTableExpression> tableExpressions, SearchParameterInfo dateParam) = CreateTargetFirstChainedSearchExpressions();
        AddSortExpression(tableExpressions, dateParam);
        SqlRootExpression sqlExpression = new(tableExpressions, []);
        SearchOptions searchOptions = new()
        {
            ContinuationToken = new ContinuationToken(["2026-09-05T17:00:00.0000000Z", (short)125, 42L]).ToString(),
            Sort = [(dateParam, sortOrder)],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        string generatedSql = _strBuilder.ToString();
        Assert.Contains("FROM dbo.ReferenceSearchParam chainTarget", generatedSql);
        Assert.Contains("StartDateTime =", generatedSql);
        Assert.Contains("ResourceSurrogateId >", generatedSql);
        Assert.Contains(sortComparison, generatedSql);
    }

    [Theory]
    [InlineData(SortOrder.Ascending)]
    [InlineData(SortOrder.Descending)]
    public void GivenSortedChainedReferenceSearchWithPrecedingSourceFilter_WhenSqlGenerated_ThenGenericShapeIsUsed(SortOrder sortOrder)
    {
        (List<SearchParamTableExpression> tableExpressions, SearchParameterInfo dateParam) = CreateTargetFirstChainedSearchExpressions();
        var sourceFilterParam = new SearchParameterInfo(
            "status",
            "status",
            SearchParamType.Token,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        Expression sourceFilter = Expression.SearchParameter(
            sourceFilterParam,
            Expression.StringEquals(FieldName.TokenCode, null, "final", false));
        _fhirModel.GetSearchParamId(sourceFilterParam.Url).Returns((short)1278);
        tableExpressions.Insert(0, new SearchParamTableExpression(TokenQueryGenerator.Instance, sourceFilter, SearchParamTableExpressionKind.Normal));
        AddSortExpression(tableExpressions, dateParam);
        SqlRootExpression sqlExpression = new(tableExpressions, []);
        SearchOptions searchOptions = new()
        {
            Sort = [(dateParam, sortOrder)],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        string generatedSql = _strBuilder.ToString();
        Assert.DoesNotContain("chainTarget", generatedSql);
        Assert.Contains("FROM dbo.ReferenceSearchParam refSource", generatedSql);
        Assert.Contains("StartDateTime AS SortValue", generatedSql);
        Assert.Contains(sortOrder == SortOrder.Ascending ? "SortValue ASC" : "SortValue DESC", generatedSql);
    }

    [Fact]
    public void GivenTargetFirstChainedSearchWithTop_WhenSqlGenerated_ThenOptimizedShapeIsPreserved()
    {
        (List<SearchParamTableExpression> tableExpressions, _) = CreateTargetFirstChainedSearchExpressions();
        tableExpressions.Add(new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top));
        SqlRootExpression sqlExpression = new(tableExpressions, []);
        SearchOptions searchOptions = new()
        {
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        string generatedSql = _strBuilder.ToString();
        Assert.Contains("FROM dbo.ReferenceSearchParam chainTarget", generatedSql);
        Assert.Contains("INNER HASH JOIN cte1 ON ResourceTypeId = T1 AND ResourceSurrogateId = Sid1", generatedSql);
        Assert.Contains("SELECT DISTINCT TOP (", generatedSql);
    }

    [Theory]
    [InlineData(SortOrder.Ascending)]
    [InlineData(SortOrder.Descending)]
    public void GivenSortedTargetFirstChainedSearchWithTop_WhenSqlGenerated_ThenTopUsesProjectedSortValue(SortOrder sortOrder)
    {
        (List<SearchParamTableExpression> tableExpressions, SearchParameterInfo dateParam) = CreateTargetFirstChainedSearchExpressions();
        AddSortExpression(tableExpressions, dateParam);
        tableExpressions.Add(new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top));
        SqlRootExpression sqlExpression = new(tableExpressions, []);
        SearchOptions searchOptions = new()
        {
            Sort = [(dateParam, sortOrder)],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        string generatedSql = _strBuilder.ToString();
        Assert.Contains("FROM dbo.ReferenceSearchParam chainTarget", generatedSql);
        Assert.Contains("SELECT DISTINCT TOP (", generatedSql);
        Assert.Contains("cte3.SortValue", generatedSql);
        Assert.Contains("FROM cte3", generatedSql);
        Assert.Contains(sortOrder == SortOrder.Ascending ? "ORDER BY SortValue  ASC" : "ORDER BY SortValue  DESC", generatedSql);
    }

    [Fact]
    public void GivenCountOnlyTargetFirstChainedSearch_WhenSqlGenerated_ThenOptimizedShapeIsPreserved()
    {
        (List<SearchParamTableExpression> tableExpressions, _) = CreateTargetFirstChainedSearchExpressions();
        SqlRootExpression sqlExpression = new(tableExpressions, []);
        SearchOptions searchOptions = new()
        {
            CountOnly = true,
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        string generatedSql = _strBuilder.ToString();
        Assert.Contains("FROM dbo.ReferenceSearchParam chainTarget", generatedSql);
        Assert.Contains("SELECT count_big(DISTINCT Sid1)", generatedSql);
        Assert.Contains("FROM cte2", generatedSql);
        Assert.DoesNotContain("SELECT DISTINCT TOP (", generatedSql);
    }

    [Fact]
    public void GivenTargetFirstChainedSearchWithInclude_WhenSqlGenerated_ThenOptimizedFilterIsPersisted()
    {
        (List<SearchParamTableExpression> tableExpressions, _) = CreateTargetFirstChainedSearchExpressions();
        var includeParam = new SearchParameterInfo(
            "subject",
            "subject",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var includeExpression = new IncludeExpression(
            ["Observation"],
            includeParam,
            "Observation",
            "Patient",
            null,
            wildCard: false,
            reversed: false,
            iterate: false);
        _fhirModel.GetResourceTypeId("Patient").Returns((short)71);
        _fhirModel.GetSearchParamId(includeParam.Url).Returns((short)1279);
        tableExpressions.Add(new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top));
        tableExpressions.Add(new SearchParamTableExpression(IncludeQueryGenerator.Instance, includeExpression, SearchParamTableExpressionKind.Include));
        SqlRootExpression sqlExpression = new(tableExpressions, []);
        SearchOptions searchOptions = new()
        {
            IncludeCount = 100,
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        string generatedSql = _strBuilder.ToString();
        Assert.Contains("FROM dbo.ReferenceSearchParam chainTarget", generatedSql);
        Assert.Contains("SELECT DISTINCT TOP (", generatedSql);
        Assert.Contains("INSERT INTO @FilteredData SELECT T1, Sid1, IsMatch, IsPartial, Row FROM cte3", generatedSql);
        Assert.Contains(";WITH cte3 AS (SELECT * FROM @FilteredData)", generatedSql);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GivenSortedTargetFirstChainedSearchWithIncludeOrRevInclude_WhenSqlGenerated_ThenSortValueIsPersisted(bool reversed)
    {
        (List<SearchParamTableExpression> tableExpressions, SearchParameterInfo dateParam) = CreateTargetFirstChainedSearchExpressions();
        var includeParam = new SearchParameterInfo(
            "subject",
            "subject",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var includeExpression = new IncludeExpression(
            ["Observation"],
            includeParam,
            "Observation",
            "Patient",
            null,
            wildCard: false,
            reversed,
            iterate: false);
        _fhirModel.GetResourceTypeId("Patient").Returns((short)71);
        _fhirModel.GetSearchParamId(includeParam.Url).Returns((short)1279);
        AddSortExpression(tableExpressions, dateParam);
        tableExpressions.Add(new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top));
        tableExpressions.Add(new SearchParamTableExpression(IncludeQueryGenerator.Instance, includeExpression, SearchParamTableExpressionKind.Include));
        SqlRootExpression sqlExpression = new(tableExpressions, []);
        SearchOptions searchOptions = new()
        {
            IncludeCount = 100,
            Sort = [(dateParam, SortOrder.Ascending)],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        string generatedSql = _strBuilder.ToString();
        Assert.Contains("FROM dbo.ReferenceSearchParam chainTarget", generatedSql);
        Assert.Contains("INSERT INTO @FilteredData SELECT T1, Sid1, IsMatch, IsPartial, Row, SortValue FROM cte4", generatedSql);
        Assert.Contains(";WITH cte4 AS (SELECT * FROM @FilteredData)", generatedSql);
    }

    [Fact]
    public void GivenTargetFirstChainedSearchWithFollowingSourcePredicate_WhenSqlGenerated_ThenOptimizedShapeFeedsFollowingPredicate()
    {
        (List<SearchParamTableExpression> tableExpressions, _) = CreateTargetFirstChainedSearchExpressions();
        var sourceFilterParam = new SearchParameterInfo(
            "status",
            "status",
            SearchParamType.Token,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        Expression sourceFilter = Expression.SearchParameter(
            sourceFilterParam,
            Expression.StringEquals(FieldName.TokenCode, null, "final", false));
        _fhirModel.GetSearchParamId(sourceFilterParam.Url).Returns((short)1278);
        tableExpressions.Add(new SearchParamTableExpression(TokenQueryGenerator.Instance, sourceFilter, SearchParamTableExpressionKind.Normal));
        SqlRootExpression sqlExpression = new(tableExpressions, []);
        SearchOptions searchOptions = new()
        {
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        string generatedSql = _strBuilder.ToString();
        Assert.Contains("FROM dbo.ReferenceSearchParam chainTarget", generatedSql);
        Assert.Contains("INNER HASH JOIN cte1 ON ResourceTypeId = T1 AND ResourceSurrogateId = Sid1", generatedSql);
        Assert.Contains("FROM dbo.TokenSearchParam", generatedSql);
        Assert.Contains("EXISTS (SELECT * FROM cte2", generatedSql);
        Assert.Equal<short>([(short)1242, (short)1273, (short)1277, (short)1278], _queryGenerator.SearchParamIds.Order());
    }

    [Theory]
    [InlineData(SortOrder.Ascending)]
    [InlineData(SortOrder.Descending)]
    public void GivenSortedTargetFirstChainedSearchWithFollowingSourcePredicate_WhenSqlGenerated_ThenSortValueIsProjectedAfterFollowingPredicate(SortOrder sortOrder)
    {
        (List<SearchParamTableExpression> tableExpressions, SearchParameterInfo dateParam) = CreateTargetFirstChainedSearchExpressions();
        var sourceFilterParam = new SearchParameterInfo(
            "status",
            "status",
            SearchParamType.Token,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        Expression sourceFilter = Expression.SearchParameter(
            sourceFilterParam,
            Expression.StringEquals(FieldName.TokenCode, null, "final", false));
        _fhirModel.GetSearchParamId(sourceFilterParam.Url).Returns((short)1278);
        tableExpressions.Add(new SearchParamTableExpression(TokenQueryGenerator.Instance, sourceFilter, SearchParamTableExpressionKind.Normal));
        AddSortExpression(tableExpressions, dateParam);
        SqlRootExpression sqlExpression = new(tableExpressions, []);
        SearchOptions searchOptions = new()
        {
            Sort = [(dateParam, sortOrder)],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        string generatedSql = _strBuilder.ToString();
        Assert.Contains("FROM dbo.ReferenceSearchParam chainTarget", generatedSql);
        Assert.Contains("FROM dbo.TokenSearchParam", generatedSql);
        Assert.Contains("EXISTS (SELECT * FROM cte3", generatedSql);
        Assert.Contains("cte4.SortValue", generatedSql);
        Assert.Contains(sortOrder == SortOrder.Ascending ? "SortValue ASC" : "SortValue DESC", generatedSql);
    }

    [Fact]
    public void GivenInterveningSourcePredicateInChainedSearch_WhenSqlGenerated_ThenExistingQueryShapeIsUsed()
    {
        (List<SearchParamTableExpression> tableExpressions, _) = CreateTargetFirstChainedSearchExpressions();
        var sourceFilterParam = new SearchParameterInfo(
            "status",
            "status",
            SearchParamType.Token,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        Expression sourceFilter = Expression.SearchParameter(
            sourceFilterParam,
            Expression.StringEquals(FieldName.TokenCode, null, "final", false));
        _fhirModel.GetSearchParamId(sourceFilterParam.Url).Returns((short)1278);
        tableExpressions.Insert(2, new SearchParamTableExpression(TokenQueryGenerator.Instance, sourceFilter, SearchParamTableExpressionKind.Normal));
        SqlRootExpression sqlExpression = new(tableExpressions, []);
        SearchOptions searchOptions = new()
        {
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        string generatedSql = _strBuilder.ToString();
        Assert.DoesNotContain("chainTarget", generatedSql);
        Assert.Contains("FROM dbo.ReferenceSearchParam refSource", generatedSql);
        Assert.Contains("FROM dbo.TokenSearchParam", generatedSql);
        Assert.Contains("FROM dbo.DateTimeSearchParam", generatedSql);
        Assert.Equal<short>([(short)1242, (short)1273, (short)1277, (short)1278], _queryGenerator.SearchParamIds.Order());
    }

    [Fact]
    public void GivenMultipleTargetPredicatesInChainedSearch_WhenSqlGenerated_ThenExistingQueryShapeIsUsed()
    {
        (List<SearchParamTableExpression> tableExpressions, _) = CreateTargetFirstChainedSearchExpressions();
        var targetFilterParam = new SearchParameterInfo(
            "identifier",
            "identifier",
            SearchParamType.Token,
            new Uri("http://hl7.org/fhir/SearchParameter/Encounter-identifier"));
        Expression targetFilter = Expression.SearchParameter(
            targetFilterParam,
            Expression.StringEquals(FieldName.TokenCode, null, "abc", false));
        _fhirModel.GetSearchParamId(targetFilterParam.Url).Returns((short)1280);
        tableExpressions.Insert(2, new SearchParamTableExpression(TokenQueryGenerator.Instance, targetFilter, SearchParamTableExpressionKind.Normal, chainLevel: 1));
        SqlRootExpression sqlExpression = new(tableExpressions, []);
        SearchOptions searchOptions = new()
        {
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        string generatedSql = _strBuilder.ToString();
        Assert.DoesNotContain("chainTarget", generatedSql);
        Assert.Contains("FROM dbo.ReferenceSearchParam refSource", generatedSql);
        Assert.Contains("FROM dbo.TokenSearchParam", generatedSql);
        Assert.Contains("FROM dbo.DateTimeSearchParam", generatedSql);
        Assert.Equal<short>([(short)1242, (short)1273, (short)1277, (short)1280], _queryGenerator.SearchParamIds.Order());
    }

    [Fact]
    public void GivenChainedTokenTargetWithoutSourceDate_WhenSqlGenerated_ThenExistingQueryShapeIsUsed()
    {
        (List<SearchParamTableExpression> tableExpressions, _) = CreateTargetFirstChainedSearchExpressions();
        var targetFilterParam = new SearchParameterInfo(
            "identifier",
            "identifier",
            SearchParamType.Token,
            new Uri("http://hl7.org/fhir/SearchParameter/Encounter-identifier"));
        Expression targetFilter = Expression.SearchParameter(
            targetFilterParam,
            Expression.StringEquals(FieldName.TokenCode, null, "organization-id", false));
        _fhirModel.GetSearchParamId(targetFilterParam.Url).Returns((short)1281);
        tableExpressions[1] = new SearchParamTableExpression(
            TokenQueryGenerator.Instance,
            targetFilter,
            SearchParamTableExpressionKind.Normal,
            chainLevel: 1);
        tableExpressions.RemoveAt(2);
        SqlRootExpression sqlExpression = new(tableExpressions, []);
        SearchOptions searchOptions = new()
        {
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);

        string generatedSql = _strBuilder.ToString();
        Assert.DoesNotContain("chainTarget", generatedSql);
        Assert.Contains("FROM dbo.ReferenceSearchParam refSource", generatedSql);
        Assert.Contains("FROM dbo.TokenSearchParam", generatedSql);
        Assert.DoesNotContain("FROM dbo.DateTimeSearchParam", generatedSql);
        Assert.Equal<short>([(short)1273, (short)1281], _queryGenerator.SearchParamIds.Order());
    }

    private (List<SearchParamTableExpression> TableExpressions, SearchParameterInfo DateParam) CreateTargetFirstChainedSearchExpressions()
    {
        var sourceReferenceParam = new SearchParameterInfo(
            "subject",
            "subject",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var targetReferenceParam = new SearchParameterInfo(
            "performer",
            "performer",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Encounter-performer"));
        var dateParam = new SearchParameterInfo(
            "date",
            "date",
            SearchParamType.Date,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-date"));

        _fhirModel.GetResourceTypeId("Observation").Returns((short)125);
        _fhirModel.GetResourceTypeId("Encounter").Returns((short)122);
        _fhirModel.TryGetResourceTypeId("Patient", out Arg.Any<short>())
            .Returns(x =>
            {
                x[1] = (short)71;
                return true;
            });
        _fhirModel.GetSearchParamId(sourceReferenceParam.Url).Returns((short)1273);
        _fhirModel.GetSearchParamId(targetReferenceParam.Url).Returns((short)1242);
        _fhirModel.GetSearchParamId(dateParam.Url).Returns((short)1277);

        var chainExpression = new SqlChainLinkExpression(
            ["Observation"],
            sourceReferenceParam,
            ["Encounter"],
            reversed: false);
        Expression targetPredicate = Expression.SearchParameter(
            targetReferenceParam,
            Expression.And(
                Expression.StringEquals(FieldName.ReferenceResourceType, null, "Patient", false),
                Expression.StringEquals(FieldName.ReferenceResourceId, null, "LUX0250", false)));
        Expression datePredicate = Expression.SearchParameter(
            dateParam,
            Expression.And(
                Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, new DateTimeOffset(2026, 9, 5, 17, 0, 0, TimeSpan.Zero)),
                Expression.LessThan(FieldName.DateTimeStart, null, new DateTimeOffset(2026, 9, 5, 18, 0, 0, TimeSpan.Zero))));

        return (
            [
                new SearchParamTableExpression(ChainLinkQueryGenerator.Instance, chainExpression, SearchParamTableExpressionKind.Chain, chainLevel: 1),
                new SearchParamTableExpression(ReferenceQueryGenerator.Instance, targetPredicate, SearchParamTableExpressionKind.Normal, chainLevel: 1),
                new SearchParamTableExpression(DateTimeQueryGenerator.Instance, datePredicate, SearchParamTableExpressionKind.Normal),
            ],
            dateParam);
    }

    private static void AddSortExpression(List<SearchParamTableExpression> tableExpressions, SearchParameterInfo dateParam)
    {
        tableExpressions.Add(
            new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                new SortExpression(dateParam),
                SearchParamTableExpressionKind.SortWithFilter));
    }
}
