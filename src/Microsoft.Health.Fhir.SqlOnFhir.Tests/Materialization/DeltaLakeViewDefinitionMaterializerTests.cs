// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Apache.Arrow;
using Apache.Arrow.Types;
using Ignixa.SqlOnFhir.Evaluation;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using Xunit;

namespace Microsoft.Health.Fhir.SqlOnFhir.Tests.Materialization;

/// <summary>
/// Unit tests for <see cref="DeltaLakeViewDefinitionMaterializer"/>.
/// Tests the static helper methods (Arrow schema, RecordBatch, MERGE SQL) that don't require
/// an actual Delta Lake engine or storage connection.
/// </summary>
public class DeltaLakeViewDefinitionMaterializerTests
{
    [Fact]
    public void BuildArrowSchema_IncludesResourceKeyAndViewDefinitionColumns()
    {
        var columns = new List<ColumnSchema>
        {
            new ColumnSchema("id", "string", false),
            new ColumnSchema("gender", "string", false),
            new ColumnSchema("active", "boolean", false),
        };

        Schema schema = DeltaLakeViewDefinitionMaterializer.BuildArrowSchema(columns);

        Assert.Equal(4, schema.FieldsList.Count);
        Assert.Equal(IViewDefinitionSchemaManager.ResourceKeyColumnName, schema.FieldsList[0].Name);
        Assert.IsType<StringType>(schema.FieldsList[0].DataType);
        Assert.Equal("id", schema.FieldsList[1].Name);
        Assert.Equal("gender", schema.FieldsList[2].Name);
        Assert.Equal("active", schema.FieldsList[3].Name);
        Assert.IsType<BooleanType>(schema.FieldsList[3].DataType);
    }

    [Fact]
    public void BuildArrowSchema_MapsAllFhirTypes()
    {
        var columns = new List<ColumnSchema>
        {
            new ColumnSchema("boolCol", "boolean", false),
            new ColumnSchema("intCol", "integer", false),
            new ColumnSchema("posIntCol", "positiveInt", false),
            new ColumnSchema("int64Col", "integer64", false),
            new ColumnSchema("decCol", "decimal", false),
            new ColumnSchema("strCol", "string", false),
            new ColumnSchema("dateCol", "date", false),
            new ColumnSchema("nullTypeCol", null, false),
        };

        Schema schema = DeltaLakeViewDefinitionMaterializer.BuildArrowSchema(columns);

        // +1 for _resource_key
        Assert.Equal(9, schema.FieldsList.Count);
        Assert.IsType<BooleanType>(schema.GetFieldByName("boolCol").DataType);
        Assert.IsType<Int32Type>(schema.GetFieldByName("intCol").DataType);
        Assert.IsType<Int32Type>(schema.GetFieldByName("posIntCol").DataType);
        Assert.IsType<Int64Type>(schema.GetFieldByName("int64Col").DataType);
        Assert.IsType<DoubleType>(schema.GetFieldByName("decCol").DataType);
        Assert.IsType<StringType>(schema.GetFieldByName("strCol").DataType);
        Assert.IsType<Date32Type>(schema.GetFieldByName("dateCol").DataType);
        Assert.IsType<StringType>(schema.GetFieldByName("nullTypeCol").DataType);
    }

    [Fact]
    public void BuildRecordBatch_CreatesCorrectRowCount()
    {
        var columns = new List<ColumnSchema>
        {
            new ColumnSchema("id", "string", false),
            new ColumnSchema("gender", "string", false),
        };
        Schema schema = DeltaLakeViewDefinitionMaterializer.BuildArrowSchema(columns);

        var rows = new List<ViewDefinitionRow>
        {
            new ViewDefinitionRow(new Dictionary<string, object?> { ["id"] = "p1", ["gender"] = "male" }),
            new ViewDefinitionRow(new Dictionary<string, object?> { ["id"] = "p2", ["gender"] = "female" }),
            new ViewDefinitionRow(new Dictionary<string, object?> { ["id"] = "p3", ["gender"] = "other" }),
        };

        RecordBatch batch = DeltaLakeViewDefinitionMaterializer.BuildRecordBatch(
            schema, columns, rows, "Patient/p1");

        Assert.Equal(3, batch.Length);
        Assert.Equal(3, batch.ColumnCount);
    }

    [Fact]
    public void BuildRecordBatch_ResourceKeyColumnPopulated()
    {
        var columns = new List<ColumnSchema>
        {
            new ColumnSchema("id", "string", false),
        };
        Schema schema = DeltaLakeViewDefinitionMaterializer.BuildArrowSchema(columns);

        var rows = new List<ViewDefinitionRow>
        {
            new ViewDefinitionRow(new Dictionary<string, object?> { ["id"] = "p1" }),
            new ViewDefinitionRow(new Dictionary<string, object?> { ["id"] = "p2" }),
        };

        RecordBatch batch = DeltaLakeViewDefinitionMaterializer.BuildRecordBatch(
            schema, columns, rows, "Patient/abc");

        var resourceKeyArray = (StringArray)batch.Column(IViewDefinitionSchemaManager.ResourceKeyColumnName);
        Assert.Equal("Patient/abc", resourceKeyArray.GetString(0));
        Assert.Equal("Patient/abc", resourceKeyArray.GetString(1));
    }

    [Fact]
    public void BuildRecordBatch_HandlesNullValues()
    {
        var columns = new List<ColumnSchema>
        {
            new ColumnSchema("id", "string", false),
            new ColumnSchema("gender", "string", false),
        };
        Schema schema = DeltaLakeViewDefinitionMaterializer.BuildArrowSchema(columns);

        var rows = new List<ViewDefinitionRow>
        {
            new ViewDefinitionRow(new Dictionary<string, object?> { ["id"] = "p1", ["gender"] = null }),
        };

        RecordBatch batch = DeltaLakeViewDefinitionMaterializer.BuildRecordBatch(
            schema, columns, rows, "Patient/p1");

        var genderArray = (StringArray)batch.Column("gender");
        Assert.True(genderArray.IsNull(0));
    }

    [Fact]
    public void BuildRecordBatch_HandlesTypedColumns()
    {
        var columns = new List<ColumnSchema>
        {
            new ColumnSchema("active", "boolean", false),
            new ColumnSchema("count", "integer", false),
            new ColumnSchema("score", "decimal", false),
        };
        Schema schema = DeltaLakeViewDefinitionMaterializer.BuildArrowSchema(columns);

        var rows = new List<ViewDefinitionRow>
        {
            new ViewDefinitionRow(new Dictionary<string, object?>
            {
                ["active"] = true,
                ["count"] = 42,
                ["score"] = 3.14,
            }),
        };

        RecordBatch batch = DeltaLakeViewDefinitionMaterializer.BuildRecordBatch(
            schema, columns, rows, "Observation/o1");

        var activeArray = (BooleanArray)batch.Column("active");
        var countArray = (Int32Array)batch.Column("count");
        var scoreArray = (DoubleArray)batch.Column("score");

        Assert.True(activeArray.GetValue(0));
        Assert.Equal(42, countArray.GetValue(0));
        Assert.Equal(3.14, scoreArray.GetValue(0));
    }

    [Fact]
    public void BuildMergeSql_GeneratesCorrectMergeStatement()
    {
        var columns = new List<ColumnSchema>
        {
            new ColumnSchema("id", "string", false),
            new ColumnSchema("gender", "string", false),
        };

        string sql = DeltaLakeViewDefinitionMaterializer.BuildMergeSql("patient_demographics", columns);

        Assert.Contains("MERGE INTO patient_demographics AS target", sql);
        Assert.Contains("USING source AS source", sql);
        Assert.Contains($"ON target.{IViewDefinitionSchemaManager.ResourceKeyColumnName} = source.{IViewDefinitionSchemaManager.ResourceKeyColumnName}", sql);
        Assert.Contains("WHEN MATCHED THEN UPDATE SET", sql);
        Assert.Contains("WHEN NOT MATCHED THEN INSERT", sql);
        Assert.Contains("target.id = source.id", sql);
        Assert.Contains("target.gender = source.gender", sql);
    }

    [Fact]
    public void BuildMergeSql_IncludesResourceKeyInInsert()
    {
        var columns = new List<ColumnSchema>
        {
            new ColumnSchema("id", "string", false),
        };

        string sql = DeltaLakeViewDefinitionMaterializer.BuildMergeSql("test_view", columns);

        Assert.Contains(IViewDefinitionSchemaManager.ResourceKeyColumnName, sql);
        Assert.Contains($"source.{IViewDefinitionSchemaManager.ResourceKeyColumnName}", sql);
    }
}
