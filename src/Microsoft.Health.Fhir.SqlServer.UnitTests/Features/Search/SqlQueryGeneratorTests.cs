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
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
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
    public void GivenUnionFollowedByConcatenationPair_WhenSqlGenerated_ThenBothBranchesShareUnionAggregatePredecessor()
    {
        // Reproduces the ScalarTemporalEqualityRewriter union + ConcatenationRewriter (DateTimeBoundedRangeRewriter)
        // interaction. The union inflates the CTE counter; the Normal/Concatenation pair that follows must both
        // restrict against the union aggregate, not against each other.
        SearchParamTableExpression unionTableExpression = BuildBareBirthdateUnionTableExpression();
        (SearchParamTableExpression normal, SearchParamTableExpression concatenation) = BuildBoundedDateRangePair();

        SqlRootExpression sqlExpression = new(
            new List<SearchParamTableExpression> { unionTableExpression, normal, concatenation },
            new List<SearchParameterExpressionBase>());
        SearchOptions searchOptions = new()
        {
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string sql = _strBuilder.ToString();

        // Generation order: cte0/cte1 = union branches, cte2 = union aggregate,
        // cte3 = Normal (long-range) sibling, cte4 = Concatenation (short-range) sibling.
        // Both the Normal sibling and the Concatenation must restrict against the shared union
        // aggregate (cte2). Before the fix, the Concatenation incorrectly restricted against its
        // own sibling cte3 because the union had inflated the CTE counter.
        Assert.Equal(2, CountOccurrences(sql, "EXISTS (SELECT * FROM cte2"));
        Assert.DoesNotContain("EXISTS (SELECT * FROM cte3", sql);

        // The Concatenation still unions in its Normal sibling (cte3) as a data source - that reference is correct.
        Assert.Contains("SELECT * FROM cte3", sql);
    }

    [Fact]
    public void GivenNormalFilterFollowedByConcatenationPair_WhenSqlGenerated_ThenBothBranchesShareLeadingPredecessor()
    {
        // Regression guard for the non-union path: a leading Normal filter followed by a Normal/Concatenation pair.
        // The predecessor finder must resolve both pair branches to the leading filter (cte0), matching the
        // behavior that existed before the union-aware rewrite.
        var leadingStart = new DateTimeOffset(2010, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var leadingPredicate = new SearchParameterExpression(BuildBirthdateParam(), Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, leadingStart));
        var leading = new SearchParamTableExpression(DateTimeQueryGenerator.Instance, leadingPredicate, SearchParamTableExpressionKind.Normal);
        (SearchParamTableExpression normal, SearchParamTableExpression concatenation) = BuildBoundedDateRangePair();

        SqlRootExpression sqlExpression = new(
            new List<SearchParamTableExpression> { leading, normal, concatenation },
            new List<SearchParameterExpressionBase>());
        SearchOptions searchOptions = new()
        {
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string sql = _strBuilder.ToString();

        // Generation order: cte0 = leading Normal, cte1 = Normal (long-range) sibling,
        // cte2 = Concatenation (short-range) sibling. With no union inflating the counter, both the
        // Normal sibling and the Concatenation restrict against the shared leading predecessor (cte0).
        Assert.Equal(2, CountOccurrences(sql, "EXISTS (SELECT * FROM cte0"));
        Assert.DoesNotContain("EXISTS (SELECT * FROM cte1", sql);
        Assert.Contains("SELECT * FROM cte1", sql);
    }

    [Fact]
    public void GivenTwoConsecutiveConcatenationPairs_WhenSqlGenerated_ThenSecondPairRestrictsAgainstFirstPairResult()
    {
        // Stacked pairs: cte0/cte1 = first (Normal, Concatenation) pair, cte2/cte3 = second pair. The predecessor
        // of the second pair is the second branch of the FIRST pair (cte1), exercising the case where a
        // Concatenation's predecessor is itself a Concatenation. Both second-pair branches must resolve to cte1,
        // never to the second pair's own Normal sibling (cte2).
        (SearchParamTableExpression normalA, SearchParamTableExpression concatA) = BuildBoundedDateRangePair();
        (SearchParamTableExpression normalB, SearchParamTableExpression concatB) = BuildBoundedDateRangePair();

        SqlRootExpression sqlExpression = new(
            new List<SearchParamTableExpression> { normalA, concatA, normalB, concatB },
            new List<SearchParameterExpressionBase>());
        SearchOptions searchOptions = new()
        {
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string sql = _strBuilder.ToString();

        Assert.Equal(2, CountOccurrences(sql, "EXISTS (SELECT * FROM cte1"));
        Assert.DoesNotContain("EXISTS (SELECT * FROM cte2", sql);
        Assert.Contains("SELECT * FROM cte2", sql); // concatB unions in its Normal sibling (cte2) as a data source
    }

    private static SearchParameterInfo BuildBirthdateParam() =>
        new SearchParameterInfo(
            "birthdate",
            "birthdate",
            SearchParamType.Date,
            new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
            expression: "Patient.birthDate",
            baseResourceTypes: new[] { "Patient" });

    private static SearchParamTableExpression BuildBareBirthdateUnionTableExpression() =>
        BuildBirthdateUnionTableExpression(chainLevel: 0);

    private static SearchParamTableExpression BuildBirthdateUnionTableExpression(int chainLevel)
    {
        var startOfDay = new DateTimeOffset(2016, 7, 6, 0, 0, 0, TimeSpan.Zero);
        var endOfDay = new DateTimeOffset(2016, 7, 6, 23, 59, 59, TimeSpan.Zero);
        var shortBranch = new SearchParameterExpression(BuildBirthdateParam(), Expression.Equals(FieldName.DateTimeEnd, null, endOfDay));
        var longBranch = new SearchParameterExpression(BuildBirthdateParam(), Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, startOfDay));
        UnionExpression union = Expression.Union(UnionOperator.All, new Expression[] { shortBranch, longBranch });

        return new SearchParamTableExpression(DateTimeQueryGenerator.Instance, union, SearchParamTableExpressionKind.Normal, chainLevel);
    }

    private void SetupChainModel()
    {
        _fhirModel.TryGetResourceTypeId("MedicationDispense", out Arg.Any<short>())
            .Returns(x =>
            {
                x[1] = (short)10;
                return true;
            });
        _fhirModel.TryGetResourceTypeId("Patient", out Arg.Any<short>())
            .Returns(x =>
            {
                x[1] = (short)1;
                return true;
            });
        _fhirModel.GetSearchParamId(Arg.Any<Uri>()).Returns((short)100);
    }

    // Produces a [Normal, Concatenation] pair from a bounded date range using the real DateTimeBoundedRangeRewriter,
    // preserving DateTimeQueryGenerator on both expressions so the SQL generator emits predecessor restrictions.
    private static (SearchParamTableExpression Normal, SearchParamTableExpression Concatenation) BuildBoundedDateRangePair()
    {
        var rangeStart = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var rangeEnd = new DateTimeOffset(2024, 1, 16, 0, 0, 0, TimeSpan.Zero);
        var rangeAnd = Expression.And(
            Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, rangeStart),
            Expression.LessThan(FieldName.DateTimeStart, null, rangeEnd));
        var predicate = new SearchParameterExpression(BuildBirthdateParam(), rangeAnd);

        SqlRootExpression root = new(
            new List<SearchParamTableExpression> { new(DateTimeQueryGenerator.Instance, predicate, SearchParamTableExpressionKind.Normal) },
            new List<SearchParameterExpressionBase>());

        var rewritten = (SqlRootExpression)root.AcceptVisitor(DateTimeBoundedRangeRewriter.Instance, null);

        Assert.Equal(2, rewritten.SearchParamTableExpressions.Count);
        Assert.Equal(SearchParamTableExpressionKind.Normal, rewritten.SearchParamTableExpressions[0].Kind);
        Assert.Equal(SearchParamTableExpressionKind.Concatenation, rewritten.SearchParamTableExpressions[1].Kind);

        return (rewritten.SearchParamTableExpressions[0], rewritten.SearchParamTableExpressions[1]);
    }

    [Fact]
    public void GivenTopLevelBirthdateUnionWithResourceTypeConstraint_WhenSqlGenerated_ThenConstraintIsDistributedIntoBranchesAndNoGroundingCteIsEmitted()
    {
        // Option A: a top-level (ChainLevel 0) exact-day birthdate becomes a UNION ALL, and the request also carries
        // a resource-column constraint (_type=Patient). ResourceColumnPredicatePushdownRewriter distributes that
        // constraint INTO each union branch - Union[And(b1, _type), And(b2, _type)] - instead of wrapping the whole
        // union - And(Union[b1, b2], _type). Distribution is valid because every branch reads the same table/partition.
        // The payoff: each branch carries the ResourceTypeId filter directly, so SplitExpressions finds no residual
        // resource predicate and emits NO extra dbo.Resource grounding CTE; the union aggregate is consumed directly.
        SetupChainModel();

        SearchParamTableExpression unionTableExpression = BuildBareBirthdateUnionTableExpression();
        var resourceType = new SearchParameterExpression(
            new SearchParameterInfo(SearchParameterNames.ResourceType, SearchParameterNames.ResourceType),
            Expression.StringEquals(FieldName.String, null, "Patient", false));

        SqlRootExpression sqlExpression = new(
            new List<SearchParamTableExpression> { unionTableExpression },
            new List<SearchParameterExpressionBase> { resourceType });

        var pushedDown = (SqlRootExpression)sqlExpression.AcceptVisitor(ResourceColumnPredicatePushdownRewriter.Instance);

        SearchOptions searchOptions = new()
        {
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(pushedDown, searchOptions);
        string sql = _strBuilder.ToString();

        // The union is preserved...
        Assert.Contains("UNION ALL", sql);

        // ...and the resource-type constraint (Patient -> ResourceTypeId = 1 per SetupChainModel) is pushed into BOTH
        // branches rather than a single trailing grounding CTE.
        Assert.Equal(2, CountOccurrences(sql, "AND ResourceTypeId = 1"));

        // Generation order with distribution: cte0/cte1 = branches, cte2 = union aggregate. No cte3: the redundant
        // dbo.Resource grounding CTE that the And(union, _type) shape would have produced is eliminated, and the final
        // SELECT joins the union aggregate (cte2) directly.
        Assert.DoesNotContain("cte3", sql);
        Assert.Contains("JOIN cte2 ON r.ResourceTypeId = cte2.T1 AND r.ResourceSurrogateId = cte2.Sid1", sql);
    }

    private static int CountOccurrences(string haystack, string needle) => haystack.Split(needle).Length - 1;
}
