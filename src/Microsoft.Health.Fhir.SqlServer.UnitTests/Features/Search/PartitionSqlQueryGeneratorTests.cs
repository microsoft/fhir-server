// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
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

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Search)]
public class PartitionSqlQueryGeneratorTests : IClassFixture<ModelInfoProviderFixture>
{
    private readonly ISqlServerFhirModel _model = Substitute.For<ISqlServerFhirModel>();
    private readonly SearchParamTableExpressionQueryGeneratorFactory _factory = new(new SearchParameterToSearchValueTypeMap());
    private readonly SchemaInformation _schema = new(SchemaVersionConstants.Min, SchemaVersionConstants.Max) { Current = SchemaVersionConstants.Max };

    public PartitionSqlQueryGeneratorTests(ModelInfoProviderFixture fixture)
    {
        _ = fixture;
        _model.GetResourceTypeId("Observation").Returns((short)10);
        _model.GetResourceTypeId("Patient").Returns((short)11);
        _model.GetSearchParamId(Arg.Any<Uri>()).Returns((short)40);
    }

    [Theory]
    [InlineData("partition-db", "[partition-db].[dbo].[Resource]")]
    [InlineData("partition.db", "[partition.db].[dbo].[Resource]")]
    [InlineData("partition]db", "[partition]]db].[dbo].[Resource]")]
    [InlineData("partition]; SELECT 1;--", "[partition]]; SELECT 1;--].[dbo].[Resource]")]
    [InlineData(null, "dbo.Resource")]
    public void GivenPartitionDatabase_WhenGeneratingResourceSearch_ThenTableIdentifierIsSafelyQualified(string database, string expectedTable)
    {
        // Arrange
        var root = new SqlRootExpression([], []);

        // Act
        string sql = Generate(root, database);

        // Assert
        Assert.Contains("FROM " + expectedTable + " r", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SearchParamType.Token, FieldName.TokenSystem, "code", "TokenSearchParam", "System")]
    [InlineData(SearchParamType.Quantity, FieldName.QuantitySystem, "value-quantity", "QuantitySearchParam", "System")]
    [InlineData(SearchParamType.Quantity, FieldName.QuantityCode, "value-quantity", "QuantitySearchParam", "QuantityCode")]
    public void GivenLookupSubquery_WhenGeneratingPartitionSearch_ThenIndexAndLookupTablesUseThePartitionDatabase(
        SearchParamType type,
        FieldName field,
        string name,
        string indexTable,
        string lookupTable)
    {
        // Arrange
        var parameter = new SearchParameterInfo(name, name, type, new Uri("http://example.org/SearchParameter/" + name));
        var predicate = Expression.SearchParameter(parameter, Expression.StringEquals(field, null, "test-system", false));
        var tableGenerator = predicate.AcceptVisitor(_factory);
        var root = new SqlRootExpression([new(tableGenerator, predicate, SearchParamTableExpressionKind.Normal)], []);

        // Act
        string sql = Generate(root, "partition-db");

        // Assert
        Assert.Contains("FROM [partition-db].[dbo].[" + indexTable + "]", sql, StringComparison.Ordinal);
        Assert.Contains("FROM [partition-db].[dbo].[" + lookupTable + "]", sql, StringComparison.Ordinal);
        Assert.Contains("FROM [partition-db].[dbo].[Resource] r", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("dbo.", sql, StringComparison.Ordinal);
        Assert.Contains("@p", sql, StringComparison.Ordinal);
        Assert.Contains("JOIN cte", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GivenChainAndInclude_WhenGeneratingPartitionSearch_ThenAllPhysicalTablesUseThePartitionDatabase(bool reversed, bool countOnly)
    {
        // Arrange
        var reference = CreateReference();
        var root = new SqlRootExpression(
            [
                new(null, null, SearchParamTableExpressionKind.All),
                new(ChainLinkQueryGenerator.Instance, new SqlChainLinkExpression(["Observation"], reference, ["Patient"], reversed), SearchParamTableExpressionKind.Chain, chainLevel: 1),
                new(null, null, SearchParamTableExpressionKind.Top),
                new(IncludeQueryGenerator.Instance, new IncludeExpression(["Observation"], reference, "Observation", "Patient", null, false, reversed, false), SearchParamTableExpressionKind.Include),
                new(null, null, SearchParamTableExpressionKind.IncludeLimit),
                new(null, null, SearchParamTableExpressionKind.IncludeUnionAll),
            ],
            []);

        // Act
        string sql = Generate(root, "partition-db", countOnly);

        // Assert
        Assert.Contains("FROM [partition-db].[dbo].[ReferenceSearchParam] refSource", sql, StringComparison.Ordinal);
        Assert.Contains("JOIN [partition-db].[dbo].[Resource] refTarget", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("dbo.", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("[dbo].[cte", sql, StringComparison.Ordinal);
        Assert.Contains("FROM cte", sql, StringComparison.Ordinal);
        if (!countOnly)
        {
            Assert.Contains("INSERT INTO @FilteredData", sql, StringComparison.Ordinal);
            Assert.Contains("FROM @FilteredData", sql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GivenAntiReferenceSubqueries_WhenGeneratingPartitionSearch_ThenReferenceTablesUseThePartitionDatabase()
    {
        // Arrange
        var root = new SqlRootExpression(
            [
                new(NotReferencedQueryGenerator.Instance, new NotReferencedExpression(CreateReference(), "Observation", false), SearchParamTableExpressionKind.Normal),
                new(NotReferencedQueryGenerator.Instance, new NotReferencingExpression("Observation", CreateReference()), SearchParamTableExpressionKind.Normal),
            ],
            []);

        // Act
        string sql = Generate(root, "partition-db");

        // Assert
        Assert.Contains("FROM [partition-db].[dbo].[Resource]", sql, StringComparison.Ordinal);
        Assert.Contains("FROM [partition-db].[dbo].[ReferenceSearchParam]) R", sql, StringComparison.Ordinal);
        Assert.Contains("RefResourceSurrogateId", sql, StringComparison.Ordinal);
        Assert.Contains("SourceResourceTypeId", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("dbo.", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GivenSmartCompartmentInclude_WhenGeneratingPartitionSearch_ThenMembershipSubqueryUsesThePartitionDatabase(bool reversed)
    {
        // Arrange
        _model.TryGetResourceTypeId("Observation", out Arg.Any<short>()).Returns(call =>
        {
            call[1] = (short)10;
            return true;
        });
        var membership = new SmartCompartmentMembershipContext(
            "Patient",
            "patient-a",
            [],
            [new SmartCompartmentMembershipRule("Observation", [CreateReference().Url])]);
        var root = new SqlRootExpression(
            [
                new(null, null, SearchParamTableExpressionKind.All),
                new(IncludeQueryGenerator.Instance, new IncludeExpression(["Observation"], CreateReference(), "Observation", "Patient", null, false, reversed, false), SearchParamTableExpressionKind.Include),
                new(null, null, SearchParamTableExpressionKind.IncludeLimit),
                new(null, null, SearchParamTableExpressionKind.IncludeUnionAll),
            ],
            [],
            membership);

        // Act
        string sql = Generate(root, "partition-db");

        // Assert
        Assert.Contains("FROM [partition-db].[dbo].[ReferenceSearchParam] smartCompartmentMembership", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("dbo.", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenDifferentPartitionDatabases_WhenCalculatingQueryHashes_ThenHashesAreDatabaseSpecific()
    {
        // Arrange
        var root = new SqlRootExpression([], []);
        var calculator = new SqlQueryHashCalculator();

        // Act
        string first = Generate(root, "database-a");
        string second = Generate(root, "database-b");

        // Assert
        Assert.NotEqual(calculator.CalculateHash(first), calculator.CalculateHash(second));
    }

    private string Generate(SqlRootExpression root, string database, bool countOnly = false)
    {
        using var command = new SqlCommand();
        var builder = new IndentedStringBuilder(new StringBuilder());
        var generator = new SqlQueryGenerator(
            builder,
            new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters)),
            _model,
            _schema,
            _factory,
            false,
            false,
            databaseName: database);
        generator.VisitSqlRoot(root, new SearchOptions { Sort = [], CountOnly = countOnly, ResourceVersionTypes = ResourceVersionType.Latest });
        return builder.ToString();
    }

    private static SearchParameterInfo CreateReference() => new(
        "subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"), null, "Observation.subject", ["Patient"]);
}
