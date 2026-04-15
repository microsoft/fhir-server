// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlOnFhir.Tests.Materialization;

/// <summary>
/// Unit tests for <see cref="SqlServerViewDefinitionMaterializer"/>.
/// Tests the SQL generation and parameter logic without requiring a real database.
/// </summary>
public class SqlServerViewDefinitionMaterializerTests
{
    private const string PatientViewDefinitionJson = """
        {
            "name": "patient_demographics",
            "resource": "Patient",
            "select": [
                {
                    "column": [
                        { "name": "id", "path": "id" },
                        { "name": "gender", "path": "gender" }
                    ]
                }
            ]
        }
        """;

    [Fact]
    public void GivenSingleRow_WhenBuildingUpsertSql_ThenDeleteAndInsertGenerated()
    {
        // Arrange
        var columnDefs = new List<MaterializedColumnDefinition>
        {
            new(IViewDefinitionSchemaManager.ResourceKeyColumnName, null, "nvarchar(128)", false),
            new("id", "id", "nvarchar(128)", false),
            new("gender", "code", "nvarchar(256)", false),
        };

        var rows = new List<ViewDefinitionRow>
        {
            new(new Dictionary<string, object?> { ["id"] = "p1", ["gender"] = "female" }),
        };

        // Act
        string sql = SqlServerViewDefinitionMaterializer.BuildUpsertSql(
            "[sqlfhir].[patient_demographics]",
            columnDefs,
            rows,
            "Patient/p1");

        // Assert
        Assert.Contains("DELETE FROM [sqlfhir].[patient_demographics]", sql);
        Assert.Contains("WHERE [_resource_key] = @ResourceKey", sql);
        Assert.Contains("INSERT INTO [sqlfhir].[patient_demographics]", sql);
        Assert.Contains("@ResourceKey", sql);
        Assert.Contains("@r0_id", sql);
        Assert.Contains("@r0_gender", sql);
    }

    [Fact]
    public void GivenMultipleRows_WhenBuildingUpsertSql_ThenMultipleInsertsGenerated()
    {
        // Arrange
        var columnDefs = new List<MaterializedColumnDefinition>
        {
            new(IViewDefinitionSchemaManager.ResourceKeyColumnName, null, "nvarchar(128)", false),
            new("id", "id", "nvarchar(128)", false),
            new("family", "string", "nvarchar(max)", false),
        };

        var rows = new List<ViewDefinitionRow>
        {
            new(new Dictionary<string, object?> { ["id"] = "p1", ["family"] = "Smith" }),
            new(new Dictionary<string, object?> { ["id"] = "p1", ["family"] = "Jones" }),
        };

        // Act
        string sql = SqlServerViewDefinitionMaterializer.BuildUpsertSql(
            "[sqlfhir].[patient_names]",
            columnDefs,
            rows,
            "Patient/p1");

        // Assert - should have one DELETE and two INSERTs
        int insertCount = sql.Split("INSERT INTO").Length - 1;
        Assert.Equal(2, insertCount);
        Assert.Contains("@r0_family", sql);
        Assert.Contains("@r1_family", sql);
    }

    [Fact]
    public void GivenRows_WhenAddingParameters_ThenResourceKeyParameterAdded()
    {
        // Arrange
        var columnDefs = new List<MaterializedColumnDefinition>
        {
            new(IViewDefinitionSchemaManager.ResourceKeyColumnName, null, "nvarchar(128)", false),
            new("id", "id", "nvarchar(128)", false),
        };

        var rows = new List<ViewDefinitionRow>
        {
            new(new Dictionary<string, object?> { ["id"] = "p1" }),
        };

        using var cmd = new SqlCommand();

        // Act
        SqlServerViewDefinitionMaterializer.AddRowParameters(cmd, columnDefs, rows, "Patient/p1");

        // Assert
        Assert.Equal("Patient/p1", cmd.Parameters["@ResourceKey"].Value);
        Assert.Equal("p1", cmd.Parameters["@r0_id"].Value?.ToString());
    }

    [Fact]
    public void GivenNullColumnValue_WhenAddingParameters_ThenDbNullUsed()
    {
        // Arrange
        var columnDefs = new List<MaterializedColumnDefinition>
        {
            new(IViewDefinitionSchemaManager.ResourceKeyColumnName, null, "nvarchar(128)", false),
            new("id", "id", "nvarchar(128)", false),
            new("gender", "code", "nvarchar(256)", false),
        };

        var rows = new List<ViewDefinitionRow>
        {
            new(new Dictionary<string, object?> { ["id"] = "p1", ["gender"] = null }),
        };

        using var cmd = new SqlCommand();

        // Act
        SqlServerViewDefinitionMaterializer.AddRowParameters(cmd, columnDefs, rows, "Patient/p1");

        // Assert
        Assert.Equal(DBNull.Value, cmd.Parameters["@r0_gender"].Value);
    }

    [Fact]
    public void GivenMissingColumnInRow_WhenAddingParameters_ThenDbNullUsed()
    {
        // Arrange — row has "id" but no "gender"
        var columnDefs = new List<MaterializedColumnDefinition>
        {
            new(IViewDefinitionSchemaManager.ResourceKeyColumnName, null, "nvarchar(128)", false),
            new("id", "id", "nvarchar(128)", false),
            new("gender", "code", "nvarchar(256)", false),
        };

        var rows = new List<ViewDefinitionRow>
        {
            new(new Dictionary<string, object?> { ["id"] = "p1" }),
        };

        using var cmd = new SqlCommand();

        // Act
        SqlServerViewDefinitionMaterializer.AddRowParameters(cmd, columnDefs, rows, "Patient/p1");

        // Assert
        Assert.Equal(DBNull.Value, cmd.Parameters["@r0_gender"].Value);
    }

    [Fact]
    public void GivenBooleanValue_WhenAddingParameters_ThenConvertedToInt()
    {
        // Arrange
        var columnDefs = new List<MaterializedColumnDefinition>
        {
            new(IViewDefinitionSchemaManager.ResourceKeyColumnName, null, "nvarchar(128)", false),
            new("active", "boolean", "bit", false),
        };

        var rows = new List<ViewDefinitionRow>
        {
            new(new Dictionary<string, object?> { ["active"] = true }),
        };

        using var cmd = new SqlCommand();

        // Act
        SqlServerViewDefinitionMaterializer.AddRowParameters(cmd, columnDefs, rows, "Patient/p1");

        // Assert
        Assert.Equal(1, cmd.Parameters["@r0_active"].Value);
    }

    [Fact]
    public void GivenMultipleRows_WhenAddingParameters_ThenParametersIndexedByRow()
    {
        // Arrange
        var columnDefs = new List<MaterializedColumnDefinition>
        {
            new(IViewDefinitionSchemaManager.ResourceKeyColumnName, null, "nvarchar(128)", false),
            new("id", "id", "nvarchar(128)", false),
        };

        var rows = new List<ViewDefinitionRow>
        {
            new(new Dictionary<string, object?> { ["id"] = "p1" }),
            new(new Dictionary<string, object?> { ["id"] = "p2" }),
        };

        using var cmd = new SqlCommand();

        // Act
        SqlServerViewDefinitionMaterializer.AddRowParameters(cmd, columnDefs, rows, "Patient/p1");

        // Assert
        Assert.Equal("p1", cmd.Parameters["@r0_id"].Value?.ToString());
        Assert.Equal("p2", cmd.Parameters["@r1_id"].Value?.ToString());
    }
}
