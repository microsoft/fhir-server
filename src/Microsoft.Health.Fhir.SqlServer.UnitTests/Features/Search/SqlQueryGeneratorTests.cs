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
using Microsoft.Health.Fhir.Core.Features.Definition;
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

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Search)]
public class SqlQueryGeneratorTests : IClassFixture<ModelInfoProviderFixture>
{
    private readonly ISqlServerFhirModel _fhirModel;
    private readonly SearchParamTableExpressionQueryGeneratorFactory _queryGeneratorFactory;
    private readonly SchemaInformation _schemaInformation = new(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
    private readonly IndentedStringBuilder _strBuilder = new(new StringBuilder());
    private readonly SqlQueryGenerator _queryGenerator;

    public SqlQueryGeneratorTests(ModelInfoProviderFixture modelInfoProviderFixture)
    {
        _ = modelInfoProviderFixture;
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
            rewriter);

        // Assert
        SmartCompartmentMembershipRule rule = Assert.Single(membership.MembershipRules);
        Assert.Equal("Observation", rule.ResourceType);
        Assert.Equal(subject.Url, Assert.Single(rule.SearchParameterUrls));
        Assert.DoesNotContain(focus.Url, rule.SearchParameterUrls);
    }

    [Fact]
    public void GivenUnmaterializedClinicalPatientParameter_WhenMembershipCreated_ThenSubjectEquivalentIsUsed()
    {
        // Arrange
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
            rewriter);

        // Assert
        SmartCompartmentMembershipRule rule = Assert.Single(membership.MembershipRules);
        Assert.Equal(subject.Url, Assert.Single(rule.SearchParameterUrls));
        Assert.DoesNotContain(clinicalPatient.Url, rule.SearchParameterUrls);
    }

    [Fact]
    public void GivenClinicalPatientParameterWithoutEquivalent_WhenMembershipCreated_ThenFormalParameterIsRetained()
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
            rewriter);

        // Assert
        SmartCompartmentMembershipRule rule = Assert.Single(membership.MembershipRules);
        Assert.Equal(clinicalPatient.Url, Assert.Single(rule.SearchParameterUrls));
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
        IReadOnlyDictionary<string, SearchParameterInfo> searchParameters)
    {
        ICompartmentDefinitionManager compartmentDefinitionManager = Substitute.For<ICompartmentDefinitionManager>();
        compartmentDefinitionManager.TryGetResourceTypes(CompartmentType.Patient, out Arg.Any<HashSet<string>>())
            .Returns(call =>
            {
                call[1] = new HashSet<string>(StringComparer.Ordinal) { resourceType };
                return true;
            });
        compartmentDefinitionManager.TryGetSearchParams(resourceType, CompartmentType.Patient, out Arg.Any<HashSet<string>>())
            .Returns(call =>
            {
                call[2] = compartmentParameterCodes;
                return true;
            });

        ISearchParameterDefinitionManager searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
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
}
