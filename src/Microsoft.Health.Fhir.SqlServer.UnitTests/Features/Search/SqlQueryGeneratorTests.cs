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
    public void GivenChainedBirthdateUnion_WhenSqlGenerated_ThenUnionLowersAndBranchesRestrictAgainstChainLink()
    {
        // Reproduces the production chained shape after ChainFlatteningRewriter:
        // MedicationDispense?patient:Patient.birthdate=<exact day>. ScalarTemporalEqualityRewriter
        // rewrites the birthdate into a day-split UNION ALL nested inside the chain. The chain link CTE
        // is the predecessor; both union branches must restrict against it, and the chained union must
        // lower to SQL without the predecessor math breaking (ICM 21000001063947 / 815288838).
        SetupChainModel();

        SearchParamTableExpression chainLink = BuildChainLinkTableExpression();
        SearchParamTableExpression unionTableExpression = BuildBirthdateUnionTableExpression(chainLevel: 1);

        SqlRootExpression sqlExpression = new(
            new List<SearchParamTableExpression> { chainLink, unionTableExpression },
            new List<SearchParameterExpressionBase>());
        SearchOptions searchOptions = new()
        {
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string sql = _strBuilder.ToString();

        // Generation order: cte0 = chain link (T1/Sid1 = MedicationDispense source, T2/Sid2 = Patient target),
        // cte1/cte2 = day-split union branches, cte3 = union aggregate. Each branch must JOIN the chain link
        // (cte0) on the chain target columns (T2/Sid2) so the birthdate is filtered to the chained Patient while
        // the MedicationDispense source columns (T1/Sid1) flow through. The branches must NOT restrict against
        // each other (no JOIN cte1).
        Assert.Contains("UNION ALL", sql);
        Assert.Equal(2, CountOccurrences(sql, "JOIN cte0 ON ResourceTypeId = T2 AND ResourceSurrogateId = Sid2"));
        Assert.DoesNotContain("JOIN cte1", sql);

        // The aggregate unions the two branches and the final query joins it on the source (T1/Sid1).
        Assert.Contains("SELECT * FROM cte1", sql);
        Assert.Contains("UNION ALL SELECT * FROM cte2", sql);
        Assert.Contains("JOIN cte3 ON r.ResourceTypeId = cte3.T1 AND r.ResourceSurrogateId = cte3.Sid1", sql);
    }

    [Fact]
    public void GivenReverseChainedBirthdateUnion_WhenSqlGenerated_ThenBranchesRestrictAgainstChainLinkIdenticallyToForwardChain()
    {
        // Reverse-chain (_has) shape, e.g. Organization?_has:Patient:organization:birthdate=<exact day>.
        // The day-split UNION is nested inside a reversed chain link. The union branches must JOIN the chain
        // link exactly the way a plain chained birthdate predicate would (on T2/Sid2), so reverse chains inherit
        // the proven baseline chained-predicate behavior. This locks reverse-chain (_has) correctness.
        SetupChainModel();

        SearchParamTableExpression chainLink = BuildReverseChainLinkTableExpression();
        SearchParamTableExpression unionTableExpression = BuildBirthdateUnionTableExpression(chainLevel: 1);

        SqlRootExpression sqlExpression = new(
            new List<SearchParamTableExpression> { chainLink, unionTableExpression },
            new List<SearchParameterExpressionBase>());
        SearchOptions searchOptions = new()
        {
            Sort = [],
            ResourceVersionTypes = ResourceVersionType.Latest,
        };

        _queryGenerator.VisitSqlRoot(sqlExpression, searchOptions);
        string sql = _strBuilder.ToString();

        // Identical branch structure to the forward chain: each branch JOINs the chain link (cte0) on T2/Sid2,
        // the branches are unioned, and the final query joins the aggregate on the source (T1/Sid1). The only
        // difference vs the forward chain is what cte0 itself produces - which is the proven baseline chain link.
        Assert.Contains("UNION ALL", sql);
        Assert.Equal(2, CountOccurrences(sql, "JOIN cte0 ON ResourceTypeId = T2 AND ResourceSurrogateId = Sid2"));
        Assert.DoesNotContain("JOIN cte1", sql);
        Assert.Contains("UNION ALL SELECT * FROM cte2", sql);
        Assert.Contains("JOIN cte3 ON r.ResourceTypeId = cte3.T1 AND r.ResourceSurrogateId = cte3.Sid1", sql);
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

    private static SearchParamTableExpression BuildChainLinkTableExpression()
    {
        var referenceParam = new SearchParameterInfo(
            "patient",
            "patient",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/clinical-patient"),
            expression: "MedicationDispense.subject",
            baseResourceTypes: new[] { "MedicationDispense" },
            targetResourceTypes: new[] { "Patient" });

        var chainLink = new SqlChainLinkExpression(
            new[] { "MedicationDispense" },
            referenceParam,
            new[] { "Patient" },
            reversed: false);

        return new SearchParamTableExpression(ChainLinkQueryGenerator.Instance, chainLink, SearchParamTableExpressionKind.Chain, chainLevel: 1);
    }

    private static SearchParamTableExpression BuildReverseChainLinkTableExpression()
    {
        // Reverse chain (_has): Organization?_has:Patient:organization:birthdate=...
        // The reference lives on Patient (the source resource type), pointing at Organization (the search root).
        var referenceParam = new SearchParameterInfo(
            "organization",
            "organization",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"),
            expression: "Patient.managingOrganization",
            baseResourceTypes: new[] { "Patient" },
            targetResourceTypes: new[] { "Organization" });

        var chainLink = new SqlChainLinkExpression(
            new[] { "Patient" },
            referenceParam,
            new[] { "Organization" },
            reversed: true);

        return new SearchParamTableExpression(ChainLinkQueryGenerator.Instance, chainLink, SearchParamTableExpressionKind.Chain, chainLevel: 1);
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

    private static int CountOccurrences(string haystack, string needle) => haystack.Split(needle).Length - 1;
}
