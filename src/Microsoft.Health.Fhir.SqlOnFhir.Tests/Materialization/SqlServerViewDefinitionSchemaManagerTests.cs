// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlOnFhir.Tests.Materialization;

/// <summary>
/// Unit tests for <see cref="SqlServerViewDefinitionSchemaManager"/>.
/// </summary>
public class SqlServerViewDefinitionSchemaManagerTests
{
    private readonly ISqlRetryService _sqlRetryService;
    private readonly SqlServerViewDefinitionSchemaManager _schemaManager;

    private const string SimpleViewDefinitionJson = """
        {
            "name": "patient_demographics",
            "resource": "Patient",
            "select": [
                {
                    "column": [
                        { "name": "id", "path": "id" },
                        { "name": "gender", "path": "gender" },
                        { "name": "birth_date", "path": "birthDate" }
                    ]
                }
            ]
        }
        """;

    private const string BloodPressureViewDefinitionJson = """
        {
            "name": "blood_pressure_components",
            "resource": "Observation",
            "select": [
                {
                    "column": [
                        { "name": "id", "path": "id" },
                        { "name": "status", "path": "status" }
                    ]
                },
                {
                    "forEach": "component",
                    "column": [
                        { "name": "component_code", "path": "code.coding.first().code" },
                        { "name": "component_value", "path": "value.ofType(Quantity).value" }
                    ]
                }
            ]
        }
        """;

    public SqlServerViewDefinitionSchemaManagerTests()
    {
        _sqlRetryService = Substitute.For<ISqlRetryService>();
        _schemaManager = new SqlServerViewDefinitionSchemaManager(
            _sqlRetryService,
            NullLogger<SqlServerViewDefinitionSchemaManager>.Instance);
    }

    [Fact]
    public void GivenASimpleViewDefinition_WhenGettingColumnDefinitions_ThenResourceKeyColumnIsFirst()
    {
        IReadOnlyList<MaterializedColumnDefinition> columns = _schemaManager.GetColumnDefinitions(SimpleViewDefinitionJson);

        Assert.NotEmpty(columns);
        Assert.Equal(IViewDefinitionSchemaManager.ResourceKeyColumnName, columns[0].ColumnName);
        Assert.Equal("nvarchar(128)", columns[0].SqlType);
    }

    [Fact]
    public void GivenASimpleViewDefinition_WhenGettingColumnDefinitions_ThenAllColumnsPresent()
    {
        IReadOnlyList<MaterializedColumnDefinition> columns = _schemaManager.GetColumnDefinitions(SimpleViewDefinitionJson);

        var columnNames = columns.Select(c => c.ColumnName).ToList();

        Assert.Contains("_resource_key", columnNames);
        Assert.Contains("id", columnNames);
        Assert.Contains("gender", columnNames);
        Assert.Contains("birth_date", columnNames);
    }

    [Fact]
    public void GivenAViewDefinitionWithForEach_WhenGettingColumnDefinitions_ThenAllColumnsIncluded()
    {
        IReadOnlyList<MaterializedColumnDefinition> columns = _schemaManager.GetColumnDefinitions(BloodPressureViewDefinitionJson);

        var columnNames = columns.Select(c => c.ColumnName).ToList();

        Assert.Contains("_resource_key", columnNames);
        Assert.Contains("id", columnNames);
        Assert.Contains("status", columnNames);
        Assert.Contains("component_code", columnNames);
        Assert.Contains("component_value", columnNames);
    }

    [Fact]
    public void GivenASimpleViewDefinition_WhenGeneratingDdl_ThenCreateTableStatementIsValid()
    {
        string ddl = _schemaManager.GenerateCreateTableDdl(SimpleViewDefinitionJson);

        Assert.Contains("CREATE TABLE [sqlfhir].[patient_demographics]", ddl);
        Assert.Contains("[_resource_key] nvarchar(128) NOT NULL", ddl);
        Assert.Contains("[id]", ddl);
        Assert.Contains("[gender]", ddl);
        Assert.Contains("[birth_date]", ddl);
        Assert.Contains("CREATE NONCLUSTERED INDEX", ddl);
        Assert.Contains("[IX_patient_demographics__resource_key]", ddl);
    }

    [Fact]
    public void GivenAViewDefinitionWithForEach_WhenGeneratingDdl_ThenAllColumnsInDdl()
    {
        string ddl = _schemaManager.GenerateCreateTableDdl(BloodPressureViewDefinitionJson);

        Assert.Contains("CREATE TABLE [sqlfhir].[blood_pressure_components]", ddl);
        Assert.Contains("[_resource_key] nvarchar(128) NOT NULL", ddl);
        Assert.Contains("[id]", ddl);
        Assert.Contains("[status]", ddl);
        Assert.Contains("[component_code]", ddl);
        Assert.Contains("[component_value]", ddl);
    }

    [Fact]
    public void GivenAViewDefinition_WhenGeneratingDdl_ThenResourceKeyIsNotNull()
    {
        string ddl = _schemaManager.GenerateCreateTableDdl(SimpleViewDefinitionJson);

        Assert.Contains("[_resource_key] nvarchar(128) NOT NULL", ddl);
    }

    [Fact]
    public void GivenAViewDefinition_WhenGeneratingDdl_ThenDataColumnsAreNullable()
    {
        string ddl = _schemaManager.GenerateCreateTableDdl(SimpleViewDefinitionJson);

        // Data columns should be NULL (not NOT NULL)
        Assert.Contains("[id]", ddl);
        Assert.DoesNotContain("[id] nvarchar(128) NOT NULL", ddl);
    }

    [Fact]
    public void GivenAViewDefinitionWithoutName_WhenGeneratingDdl_ThenExceptionThrown()
    {
        string invalidJson = """
            {
                "resource": "Patient",
                "select": [{ "column": [{ "name": "id", "path": "id" }] }]
            }
            """;

        Assert.Throws<ArgumentException>(() => _schemaManager.GenerateCreateTableDdl(invalidJson));
    }

    [Fact]
    public void GetQualifiedTableName_ShouldReturnBracketedSchemaAndTable()
    {
        string result = SqlServerViewDefinitionSchemaManager.GetQualifiedTableName("patient_demographics");
        Assert.Equal("[sqlfhir].[patient_demographics]", result);
    }

    [Fact]
    public void ExtractViewDefinitionName_GivenValidJson_ShouldReturnName()
    {
        string name = SqlServerViewDefinitionSchemaManager.ExtractViewDefinitionName(SimpleViewDefinitionJson);
        Assert.Equal("patient_demographics", name);
    }

    [Fact]
    public void ExtractViewDefinitionName_GivenJsonWithoutName_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(
            () => SqlServerViewDefinitionSchemaManager.ExtractViewDefinitionName("""{ "resource": "Patient" }"""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GivenEmptyViewDefinition_WhenGettingColumnDefinitions_ThenExceptionThrown(string json)
    {
        Assert.Throws<ArgumentException>(() => _schemaManager.GetColumnDefinitions(json));
    }

    [Fact]
    public void GivenNullViewDefinition_WhenGettingColumnDefinitions_ThenExceptionThrown()
    {
        Assert.Throws<ArgumentNullException>(() => _schemaManager.GetColumnDefinitions(null!));
    }
}
