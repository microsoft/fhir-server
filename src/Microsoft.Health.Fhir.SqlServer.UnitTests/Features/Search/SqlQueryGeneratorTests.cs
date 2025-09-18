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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
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
    private readonly SchemaInformation _schemaInformation = new(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
    private readonly IndentedStringBuilder _strBuilder = new(new StringBuilder());
    private readonly SqlQueryGenerator _queryGenerator;

    public SqlQueryGeneratorTests()
    {
        _fhirModel = Substitute.For<ISqlServerFhirModel>();
        _schemaInformation.Current = SchemaVersionConstants.Max;

        using Data.SqlClient.SqlCommand command = new();
        HashingSqlQueryParameterManager parameters = new(new SqlQueryParameterManager(command.Parameters));

        _queryGenerator = new(
            _strBuilder,
            parameters,
            _fhirModel,
            _schemaInformation,
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
    public void GivenNoPaginationParameters_WhenSqlGenerated_ThenNoPaginationClauseIsAdded()
    {
        Expression predicate = new SearchParameterExpression(
            new SearchParameterInfo("_type", "_type"),
            Expression.Equals(FieldName.String, null, "Patient"));
        var sqlExpression = new SqlRootExpression(
            new List<SearchParamTableExpression> { new SearchParamTableExpression(null, predicate, SearchParamTableExpressionKind.All) },
            new List<SearchParameterExpressionBase>());
        var searchOptions = new SearchOptions
        {
            Sort = new List<(SearchParameterInfo, SortOrder)>(),
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string sql = _strBuilder.ToString();

        Assert.DoesNotContain("OFFSET", sql);
        Assert.DoesNotContain("FETCH NEXT", sql);
    }

    [Fact]
    public void GivenResourceTableExpressionWithTypePatient_WhenSqlGenerated_ThenSqlContainsCorrectParameter()
    {
        SqlRootExpression sqlExpression = new(
            new List<SearchParamTableExpression>(),
            new List<SearchParameterExpressionBase>
            {
                new SearchParameterExpression(
                    new SearchParameterInfo(SearchParameterNames.ResourceType, SearchParameterNames.ResourceType),
                    Expression.Equals(FieldName.String, null, "Patient")),
            });
        SearchOptions searchOptions = new()
        {
            Sort = new List<(SearchParameterInfo, SortOrder)>(),
            ResourceVersionTypes = ResourceVersionType.Latest,
            PageNumber = 3,
            PageSize = 5,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string sql = _strBuilder.ToString();

        Assert.Contains("OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY", sql);

        searchOptions = new()
        {
            Sort = new List<(SearchParameterInfo, SortOrder)>(),
            ResourceVersionTypes = ResourceVersionType.Latest,
            RowStart = 3,
            RowEnd = 5,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        sql = _strBuilder.ToString();

        Assert.Contains("OFFSET 2 ROWS FETCH NEXT 3 ROWS ONLY", sql);
    }

    [Fact]
    public void GivenTwoResourceTableExpressions_ForTypePatientAndId123_WhenSqlGeneratedWithPagination_ThenPaginationClauseIsAdded()
    {
        // Already fixed as per your new pattern:
        SqlRootExpression sqlExpression = new SqlRootExpression(
            new List<SearchParamTableExpression>(),
            new List<SearchParameterExpressionBase>
            {
                    new SearchParameterExpression(
                        new SearchParameterInfo(SearchParameterNames.ResourceType, SearchParameterNames.ResourceType),
                        Expression.Equals(FieldName.String, null, "Patient")),
                    new SearchParameterExpression(
                        new SearchParameterInfo(SearchParameterNames.Id, SearchParameterNames.Id),
                        Expression.Equals(FieldName.String, null, "123")),
            });
        SearchOptions searchOptions = new()
        {
            Sort = new List<(SearchParameterInfo, SortOrder)>(),
            ResourceVersionTypes = ResourceVersionType.Latest,
            PageNumber = 3,
            PageSize = 5,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string sql = _strBuilder.ToString();

        Assert.Contains("OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY", sql);

        searchOptions = new()
        {
            Sort = new List<(SearchParameterInfo, SortOrder)>(),
            ResourceVersionTypes = ResourceVersionType.Latest,
            RowStart = 3,
            RowEnd = 5,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        sql = _strBuilder.ToString();

        Assert.Contains("OFFSET 2 ROWS FETCH NEXT 3 ROWS ONLY", sql);
    }

    [Fact]
    public void GivenSearchParamTableExpressions_WithTokenSearchParamAndTopExpressions_WhenSqlGeneratedWithPagination_ThenPaginationClauseIsAdded()
    {
        // Instead of combining two predicates with Expression.And we add two separate table expressions.
        var normalTable1 = new SearchParamTableExpression(
            queryGenerator: new TokenQueryGenerator(),
            predicate: new SearchParameterExpression(
                new SearchParameterInfo("gender", "gender"),
                Expression.Equals(FieldName.String, null, "female")),
            kind: SearchParamTableExpressionKind.Normal);
        var normalTable2 = new SearchParamTableExpression(
            queryGenerator: new TokenQueryGenerator(),
            predicate: new SearchParameterExpression(
                new SearchParameterInfo("_type", "_type"),
                Expression.Equals(FieldName.String, null, "Patient")),
            kind: SearchParamTableExpressionKind.Normal);
        var topTable = new SearchParamTableExpression(
            queryGenerator: null,
            predicate: null,
            kind: SearchParamTableExpressionKind.Top);
        SqlRootExpression sqlExpression = new SqlRootExpression(
            new List<SearchParamTableExpression> { normalTable1, normalTable2, topTable },
            new List<SearchParameterExpressionBase>());
        SearchOptions searchOptions = new()
        {
            Sort = new List<(SearchParameterInfo, SortOrder)>(),
            ResourceVersionTypes = ResourceVersionType.Latest,
            RowStart = 5,
            RowEnd = 15,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string sql = _strBuilder.ToString();

        Assert.Contains("OFFSET 4 ROWS FETCH NEXT 11 ROWS ONLY", sql);

        searchOptions = new()
        {
            Sort = new List<(SearchParameterInfo, SortOrder)>(),
            ResourceVersionTypes = ResourceVersionType.Latest,
            RowStart = 3,
            RowEnd = 5,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        sql = _strBuilder.ToString();

        Assert.Contains("OFFSET 2 ROWS FETCH NEXT 3 ROWS ONLY", sql);
    }

    [Fact]
    public void GivenChainAndNormalChainAndTopExpressions_WhenSqlGeneratedWithPagination_ThenPaginationClauseIsAdded()
    {
        // Chain expression using SqlChainLinkExpression remains unchanged.
        Expression chainPredicate = new SqlChainLinkExpression(
            resourceTypes: new[] { "Observation" },
            referenceSearchParameter: new SearchParameterInfo("_type", "_type"),
            targetResourceTypes: new[] { "Patient" },
            reversed: false,
            expressionOnSource: null,
            expressionOnTarget: null);
        var chainTable = new SearchParamTableExpression(
            queryGenerator: new ChainLinkQueryGenerator(),
            predicate: chainPredicate,
            kind: SearchParamTableExpressionKind.Chain,
            chainLevel: 1);

        // Create normal chain expression without wrapping in Expression.And.
        Expression normalPredicate = new SearchParameterExpression(
            new SearchParameterInfo("name", "name"),
            Expression.StartsWith(FieldName.String, null, "Smith", true));
        var normalTable = new SearchParamTableExpression(
            queryGenerator: new StringQueryGenerator(),
            predicate: normalPredicate,
            kind: SearchParamTableExpressionKind.Normal,
            chainLevel: 1);

        var topTable = new SearchParamTableExpression(
            queryGenerator: null,
            predicate: null,
            kind: SearchParamTableExpressionKind.Top);
        SqlRootExpression sqlExpression = new SqlRootExpression(
            new List<SearchParamTableExpression> { chainTable, normalTable, topTable },
            new List<SearchParameterExpressionBase>());
        SearchOptions searchOptions = new()
        {
            Sort = new List<(SearchParameterInfo, SortOrder)>(),
            ResourceVersionTypes = ResourceVersionType.Latest,
            RowStart = 5,
            RowEnd = 15,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string sql = _strBuilder.ToString();

        Assert.Contains("OFFSET 4 ROWS FETCH NEXT 11 ROWS ONLY", sql);

        searchOptions = new()
        {
            Sort = new List<(SearchParameterInfo, SortOrder)>(),
            ResourceVersionTypes = ResourceVersionType.Latest,
            RowStart = 3,
            RowEnd = 5,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        sql = _strBuilder.ToString();

        Assert.Contains("OFFSET 2 ROWS FETCH NEXT 3 ROWS ONLY", sql);
    }

    [Fact]
    public void GivenReferenceAndTokenExpressions_WhenSqlGeneratedWithPagination_ThenPaginationClauseIsAdded()
    {
        // For reference expression, build conditions separately.
        var subjectExpr = new SearchParameterExpression(
            new SearchParameterInfo("subject", "subject"),
            Expression.Equals(FieldName.String, null, "dummy")); // placeholder for inner composite
        var typeExpr = new SearchParameterExpression(
            new SearchParameterInfo("_type", "_type"),
            Expression.Equals(FieldName.String, null, "Observation"));

        // We simulate that the reference predicate combines subject and _type by adding them as separate conditions.
        Expression referencePredicate = new MultiaryExpression(MultiaryOperator.And, new Expression[] { subjectExpr, typeExpr });
        var referenceTable = new SearchParamTableExpression(
            queryGenerator: new ReferenceQueryGenerator(),
            predicate: referencePredicate,
            kind: SearchParamTableExpressionKind.Normal,
            chainLevel: 0);

        var tokenPredicate = new MultiaryExpression(MultiaryOperator.And, new Expression[]
        {
            new SearchParameterExpression(
                new SearchParameterInfo("code", "code"),
                Expression.Equals(FieldName.String, null, "78910-1")),
            new SearchParameterExpression(
                new SearchParameterInfo("_type", "_type"),
                Expression.Equals(FieldName.String, null, "Observation")),
        });
        var tokenTable = new SearchParamTableExpression(
            queryGenerator: new TokenQueryGenerator(),
            predicate: tokenPredicate,
            kind: SearchParamTableExpressionKind.Normal,
            chainLevel: 0);

        var topTable = new SearchParamTableExpression(
            queryGenerator: null,
            predicate: null,
            kind: SearchParamTableExpressionKind.Top,
            chainLevel: 0);

        SqlRootExpression sqlExpression = new SqlRootExpression(
            new List<SearchParamTableExpression> { referenceTable, tokenTable, topTable },
            new List<SearchParameterExpressionBase>());
        SearchOptions searchOptions = new()
        {
            Sort = new List<(SearchParameterInfo, SortOrder)>(),
            ResourceVersionTypes = ResourceVersionType.Latest,
            RowStart = 5,
            RowEnd = 15,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string sql = _strBuilder.ToString();

        Assert.Contains("OFFSET 4 ROWS FETCH NEXT 11 ROWS ONLY", sql);

        searchOptions = new()
        {
            Sort = new List<(SearchParameterInfo, SortOrder)>(),
            ResourceVersionTypes = ResourceVersionType.Latest,
            RowStart = 3,
            RowEnd = 5,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        sql = _strBuilder.ToString();

        Assert.Contains("OFFSET 2 ROWS FETCH NEXT 3 ROWS ONLY", sql);
    }

    [Fact]
    public void GivenReferenceSearchForEncounter_WhenSqlGeneratedWithPagination_ThenPaginationClauseIsAdded()
    {
        var patientExpr = new SearchParameterExpression(
            new SearchParameterInfo("patient", "patient"),
            Expression.Equals(FieldName.String, null, "dummy")); // placeholder for composite
        var typeExpr = new SearchParameterExpression(
            new SearchParameterInfo("_type", "_type"),
            Expression.Equals(FieldName.String, null, "Encounter"));
        Expression referencePredicate = new MultiaryExpression(MultiaryOperator.And, new Expression[] { patientExpr, typeExpr });
        var referenceTable = new SearchParamTableExpression(
            queryGenerator: new ReferenceQueryGenerator(),
            predicate: referencePredicate,
            kind: SearchParamTableExpressionKind.Normal,
            chainLevel: 0);

        var topTable = new SearchParamTableExpression(
            queryGenerator: null,
            predicate: null,
            kind: SearchParamTableExpressionKind.Top,
            chainLevel: 0);
        SqlRootExpression sqlExpression = new SqlRootExpression(
            new List<SearchParamTableExpression> { referenceTable, topTable },
            new List<SearchParameterExpressionBase>());
        SearchOptions searchOptions = new()
        {
            Sort = new List<(SearchParameterInfo, SortOrder)>(),
            ResourceVersionTypes = ResourceVersionType.Latest,
            RowStart = 5,
            RowEnd = 15,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string sql = _strBuilder.ToString();

        Assert.Contains("OFFSET 4 ROWS FETCH NEXT 11 ROWS ONLY", sql);

        searchOptions = new()
        {
            Sort = new List<(SearchParameterInfo, SortOrder)>(),
            ResourceVersionTypes = ResourceVersionType.Latest,
            RowStart = 3,
            RowEnd = 5,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        sql = _strBuilder.ToString();

        Assert.Contains("OFFSET 2 ROWS FETCH NEXT 3 ROWS ONLY", sql);
    }

    [Fact]
    public void GivenUnionIncludeExpressions_WhenSqlGeneratedWithPagination_ThenPaginationClauseIsAdded()
    {
        var allPredicate = new SearchParameterExpression(
            new SearchParameterInfo("_type", "_type"),
            Expression.Equals(FieldName.String, null, "Patient"));
        var idPredicate = new SearchParameterExpression(
            new SearchParameterInfo("_id", "_id"),
            Expression.Equals(FieldName.String, null, "123"));
        var allTable = new SearchParamTableExpression(
            queryGenerator: null,
            predicate: new MultiaryExpression(MultiaryOperator.And, new Expression[] { allPredicate, idPredicate }),
            kind: SearchParamTableExpressionKind.All,
            chainLevel: 0);
        var topTable = new SearchParamTableExpression(
            queryGenerator: null,
            predicate: null,
            kind: SearchParamTableExpressionKind.Top,
            chainLevel: 0);

        // Include expression: wildcard include.
        Expression includePredicate = Expression.Include(
            new[] { "Patient" },
            new SearchParameterInfo("dummy", "dummy"),
            "SourceType",
            "TargetType",
            new List<string> { "Patient" },
            wildCard: true,
            reversed: false,
            iterate: false);
        var includeTable = new SearchParamTableExpression(
            queryGenerator: new IncludeQueryGenerator(),
            predicate: includePredicate,
            kind: SearchParamTableExpressionKind.Include,
            chainLevel: 0);
        var includeLimitTable = new SearchParamTableExpression(
            queryGenerator: null,
            predicate: null,
            kind: SearchParamTableExpressionKind.IncludeLimit,
            chainLevel: 0);
        var includeUnionAllTable = new SearchParamTableExpression(
            queryGenerator: null,
            predicate: null,
            kind: SearchParamTableExpressionKind.IncludeUnionAll,
            chainLevel: 0);
        SqlRootExpression sqlExpression = new SqlRootExpression(
            new List<SearchParamTableExpression> { allTable, topTable, includeTable, includeLimitTable, includeUnionAllTable },
            new List<SearchParameterExpressionBase>());
        SearchOptions searchOptions = new()
        {
            IncludeCount = 5,
            Sort = new List<(SearchParameterInfo, SortOrder)>(),
            ResourceVersionTypes = ResourceVersionType.Latest,
            IncludesContinuationToken = null,
            RowStart = 5,
            RowEnd = 15,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string sql = _strBuilder.ToString();

        Assert.Contains("OFFSET 4 ROWS FETCH NEXT 11 ROWS ONLY", sql);

        searchOptions = new()
        {
            Sort = new List<(SearchParameterInfo, SortOrder)>(),
            ResourceVersionTypes = ResourceVersionType.Latest,
            RowStart = 3,
            RowEnd = 5,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        sql = _strBuilder.ToString();

        Assert.Contains("OFFSET 2 ROWS FETCH NEXT 3 ROWS ONLY", sql);
    }

    [Fact]
    public void GivenReversedIncludeWildcardExpressions_WhenSqlGeneratedWithPagination_ThenPaginationClauseIsAdded()
    {
        var allPredicate = new SearchParameterExpression(
            new SearchParameterInfo("_type", "_type"),
            Expression.Equals(FieldName.String, null, "Patient"));
        var allTable = new SearchParamTableExpression(
            queryGenerator: null,
            predicate: allPredicate,
            kind: SearchParamTableExpressionKind.All,
            chainLevel: 0);
        var topTable = new SearchParamTableExpression(
            queryGenerator: null,
            predicate: null,
            kind: SearchParamTableExpressionKind.Top,
            chainLevel: 0);
        Expression reversedIncludePredicate = Expression.Include(
            new[] { "Patient" },
            new SearchParameterInfo("dummy", "dummy"),
            "SourceType",
            "TargetType",
            new List<string> { "Patient" },
            wildCard: true,
            reversed: true,
            iterate: false);
        var includeTable = new SearchParamTableExpression(
            queryGenerator: new IncludeQueryGenerator(),
            predicate: reversedIncludePredicate,
            kind: SearchParamTableExpressionKind.Include,
            chainLevel: 0);
        var includeLimitTable = new SearchParamTableExpression(
            queryGenerator: null,
            predicate: null,
            kind: SearchParamTableExpressionKind.IncludeLimit,
            chainLevel: 0);
        var includeUnionAllTable = new SearchParamTableExpression(
            queryGenerator: null,
            predicate: null,
            kind: SearchParamTableExpressionKind.IncludeUnionAll,
            chainLevel: 0);
        SqlRootExpression sqlExpression = new SqlRootExpression(
            new List<SearchParamTableExpression> { allTable, topTable, includeTable, includeLimitTable, includeUnionAllTable },
            new List<SearchParameterExpressionBase>());
        SearchOptions searchOptions = new()
        {
            IncludeCount = 5,
            Sort = new List<(SearchParameterInfo, SortOrder)>(),
            ResourceVersionTypes = ResourceVersionType.Latest,
            IncludesContinuationToken = null,
            RowStart = 5,
            RowEnd = 15,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string sql = _strBuilder.ToString();

        Assert.Contains("OFFSET 4 ROWS FETCH NEXT 11 ROWS ONLY", sql);

        searchOptions = new()
        {
            Sort = new List<(SearchParameterInfo, SortOrder)>(),
            ResourceVersionTypes = ResourceVersionType.Latest,
            RowStart = 3,
            RowEnd = 5,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        sql = _strBuilder.ToString();

        Assert.Contains("OFFSET 2 ROWS FETCH NEXT 3 ROWS ONLY", sql);
    }
}
