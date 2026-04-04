// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlOnFhir.Tests.Materialization;

/// <summary>
/// End-to-end pipeline tests for the materialization layer. These use the real Ignixa
/// ViewDefinition evaluator and schema evaluator, with only the SQL execution layer mocked.
/// This validates the full data flow: ViewDefinition JSON → Ignixa evaluation → schema inference
/// → DDL generation → row materialization → correct SQL parameter binding.
/// </summary>
public class MaterializationPipelineTests
{
    private readonly IViewDefinitionEvaluator _evaluator;
    private readonly ISqlRetryService _sqlRetryService;
    private readonly SqlServerViewDefinitionSchemaManager _schemaManager;
    private readonly SqlServerViewDefinitionMaterializer _materializer;

    private SqlCommand? _lastCapturedCommand;

    public MaterializationPipelineTests()
    {
        _evaluator = new ViewDefinitionEvaluator(NullLogger<ViewDefinitionEvaluator>.Instance);
        _sqlRetryService = Substitute.For<ISqlRetryService>();

        _schemaManager = new SqlServerViewDefinitionSchemaManager(
            _sqlRetryService,
            NullLogger<SqlServerViewDefinitionSchemaManager>.Instance);

        _materializer = new SqlServerViewDefinitionMaterializer(
            _evaluator,
            _schemaManager,
            _sqlRetryService,
            NullLogger<SqlServerViewDefinitionMaterializer>.Instance);

        // Default setup: capture all SQL commands executed via the retry service.
        // Do NOT execute the action callback — it would try to use a real SQL connection.
        _sqlRetryService.ExecuteSql(
            Arg.Any<SqlCommand>(),
            Arg.Any<Func<SqlCommand, CancellationToken, Task>>(),
            Arg.Any<ILogger>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<string>())
        .Returns(callInfo =>
        {
            _lastCapturedCommand = callInfo.ArgAt<SqlCommand>(0);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void GivenPatientDemographicsView_WhenFullPipeline_ThenSchemaAndDdlAreConsistent()
    {
        // Arrange
        string viewDefJson = """
            {
                "name": "patient_demographics",
                "resource": "Patient",
                "select": [{
                    "column": [
                        { "name": "id", "path": "id" },
                        { "name": "gender", "path": "gender" },
                        { "name": "birth_date", "path": "birthDate" }
                    ]
                }]
            }
            """;

        // Act — schema inference via Ignixa
        IReadOnlyList<MaterializedColumnDefinition> columns = _schemaManager.GetColumnDefinitions(viewDefJson);
        string ddl = _schemaManager.GenerateCreateTableDdl(viewDefJson);

        // Assert — columns
        Assert.Equal(4, columns.Count); // _resource_key + 3 data columns
        Assert.Equal("_resource_key", columns[0].ColumnName);
        Assert.Equal("id", columns[1].ColumnName);
        Assert.Equal("gender", columns[2].ColumnName);
        Assert.Equal("birth_date", columns[3].ColumnName);

        // Assert — DDL matches schema
        foreach (var col in columns)
        {
            Assert.Contains($"[{col.ColumnName}]", ddl);
            Assert.Contains(col.SqlType, ddl);
        }

        Assert.Contains("CREATE TABLE [sqlfhir].[patient_demographics]", ddl);
        Assert.Contains("CREATE NONCLUSTERED INDEX", ddl);
    }

    [Fact]
    public void GivenBloodPressureView_WhenFullPipeline_ThenForEachColumnsIncluded()
    {
        // Arrange
        string viewDefJson = """
            {
                "name": "us_core_blood_pressures",
                "resource": "Observation",
                "constant": [
                    {"name": "systolic_bp", "valueCode": "8480-6"},
                    {"name": "diastolic_bp", "valueCode": "8462-4"},
                    {"name": "bp_code", "valueCode": "85354-9"}
                ],
                "select": [
                    {"column": [
                        {"path": "id", "name": "id"},
                        {"path": "effective.ofType(dateTime)", "name": "effective_date_time"}
                    ]},
                    {"forEach": "component.where(code.coding.exists(system='http://loinc.org' and code=%systolic_bp)).first()",
                     "column": [
                        {"path": "value.ofType(Quantity).value", "name": "sbp_quantity_value"}
                    ]},
                    {"forEach": "component.where(code.coding.exists(system='http://loinc.org' and code=%diastolic_bp)).first()",
                     "column": [
                        {"path": "value.ofType(Quantity).value", "name": "dbp_quantity_value"}
                    ]}
                ],
                "where": [{"path": "code.coding.exists(system='http://loinc.org' and code=%bp_code)"}]
            }
            """;

        // Act
        IReadOnlyList<MaterializedColumnDefinition> columns = _schemaManager.GetColumnDefinitions(viewDefJson);
        string ddl = _schemaManager.GenerateCreateTableDdl(viewDefJson);

        // Assert — all columns present including forEach columns
        var colNames = columns.Select(c => c.ColumnName).ToList();
        Assert.Contains("_resource_key", colNames);
        Assert.Contains("id", colNames);
        Assert.Contains("effective_date_time", colNames);
        Assert.Contains("sbp_quantity_value", colNames);
        Assert.Contains("dbp_quantity_value", colNames);

        Assert.Contains("CREATE TABLE [sqlfhir].[us_core_blood_pressures]", ddl);
    }

    [Fact]
    public async Task GivenPatientResource_WhenFullMaterializationPipeline_ThenCorrectSqlParametersGenerated()
    {
        // Arrange
        var patient = new Patient
        {
            Id = "test-p1",
            Gender = AdministrativeGender.Female,
            BirthDate = "1990-03-15",
        };

        string viewDefJson = """
            {
                "name": "patient_demographics",
                "resource": "Patient",
                "select": [{
                    "column": [
                        { "name": "id", "path": "id" },
                        { "name": "gender", "path": "gender" },
                        { "name": "birth_date", "path": "birthDate" }
                    ]
                }]
            }
            """;

        // Capture the SQL command that would be executed (constructor setup handles mock)
        ResourceElement resourceElement = ToResourceElement(patient);

        // Act
        int rowCount = await _materializer.UpsertResourceAsync(
            viewDefJson,
            "patient_demographics",
            resourceElement,
            "Patient/test-p1",
            CancellationToken.None);

        // Assert
        Assert.Equal(1, rowCount);
        Assert.NotNull(_lastCapturedCommand);

        string sql = _lastCapturedCommand!.CommandText;
        Assert.Contains("DELETE FROM [sqlfhir].[patient_demographics]", sql);
        Assert.Contains("INSERT INTO [sqlfhir].[patient_demographics]", sql);
        Assert.Contains("@ResourceKey", sql);

        // Verify parameter values from real Ignixa evaluation
        Assert.Equal("Patient/test-p1", _lastCapturedCommand.Parameters["@ResourceKey"].Value);
        Assert.Equal("test-p1", _lastCapturedCommand.Parameters["@r0_id"].Value?.ToString());
        Assert.Equal("female", _lastCapturedCommand.Parameters["@r0_gender"].Value?.ToString());
        Assert.NotEqual(DBNull.Value, _lastCapturedCommand.Parameters["@r0_birth_date"].Value);
    }

    [Fact]
    public async Task GivenPatientWithMultipleNames_WhenMaterialized_ThenMultipleRowsWithSameResourceKey()
    {
        // Arrange
        var patient = new Patient
        {
            Id = "test-p2",
            Name =
            {
                new HumanName { Use = HumanName.NameUse.Official, Family = "Smith" },
                new HumanName { Use = HumanName.NameUse.Maiden, Family = "Jones" },
            },
        };

        string viewDefJson = """
            {
                "name": "patient_names",
                "resource": "Patient",
                "select": [
                    { "column": [{ "name": "id", "path": "id" }] },
                    { "forEach": "name", "column": [
                        { "name": "family", "path": "family" },
                        { "name": "name_use", "path": "use" }
                    ]}
                ]
            }
            """;

        // SQL capture is handled by constructor setup

        ResourceElement resourceElement = ToResourceElement(patient);

        // Act
        int rowCount = await _materializer.UpsertResourceAsync(
            viewDefJson,
            "patient_names",
            resourceElement,
            "Patient/test-p2",
            CancellationToken.None);

        // Assert — forEach produces 2 rows (one per name)
        Assert.Equal(2, rowCount);
        Assert.NotNull(_lastCapturedCommand);

        string sql = _lastCapturedCommand!.CommandText;

        // Both rows share the same resource key
        Assert.Equal("Patient/test-p2", _lastCapturedCommand.Parameters["@ResourceKey"].Value);

        // Row 0 and Row 1 have different family values
        Assert.Contains("@r0_family", sql);
        Assert.Contains("@r1_family", sql);

        var families = new[]
        {
            _lastCapturedCommand.Parameters["@r0_family"].Value?.ToString(),
            _lastCapturedCommand.Parameters["@r1_family"].Value?.ToString(),
        };

        Assert.Contains("Smith", families);
        Assert.Contains("Jones", families);
    }

    [Fact]
    public async Task GivenBloodPressureObservation_WhenMaterialized_ThenComponentValuesExtracted()
    {
        // Arrange
        var observation = new Observation
        {
            Id = "bp-1",
            Status = ObservationStatus.Final,
            Code = new CodeableConcept("http://loinc.org", "85354-9", "Blood pressure panel"),
            Effective = new FhirDateTime("2024-01-15T10:30:00Z"),
            Component =
            {
                new Observation.ComponentComponent
                {
                    Code = new CodeableConcept("http://loinc.org", "8480-6", "Systolic BP"),
                    Value = new Quantity(120, "mmHg", "http://unitsofmeasure.org"),
                },
                new Observation.ComponentComponent
                {
                    Code = new CodeableConcept("http://loinc.org", "8462-4", "Diastolic BP"),
                    Value = new Quantity(80, "mmHg", "http://unitsofmeasure.org"),
                },
            },
        };

        string viewDefJson = """
            {
                "name": "us_core_blood_pressures",
                "resource": "Observation",
                "constant": [
                    {"name": "systolic_bp", "valueCode": "8480-6"},
                    {"name": "diastolic_bp", "valueCode": "8462-4"},
                    {"name": "bp_code", "valueCode": "85354-9"}
                ],
                "select": [
                    {"column": [
                        {"path": "id", "name": "id"},
                        {"path": "effective.ofType(dateTime)", "name": "effective_date_time"}
                    ]},
                    {"forEach": "component.where(code.coding.exists(system='http://loinc.org' and code=%systolic_bp)).first()",
                     "column": [{"path": "value.ofType(Quantity).value", "name": "sbp_quantity_value"}]},
                    {"forEach": "component.where(code.coding.exists(system='http://loinc.org' and code=%diastolic_bp)).first()",
                     "column": [{"path": "value.ofType(Quantity).value", "name": "dbp_quantity_value"}]}
                ],
                "where": [{"path": "code.coding.exists(system='http://loinc.org' and code=%bp_code)"}]
            }
            """;

        // SQL capture is handled by constructor setup

        ResourceElement resourceElement = ToResourceElement(observation);

        // Act
        int rowCount = await _materializer.UpsertResourceAsync(
            viewDefJson,
            "us_core_blood_pressures",
            resourceElement,
            "Observation/bp-1",
            CancellationToken.None);

        // Assert — BP observation produces exactly 1 row (the two forEach each produce one column)
        Assert.Equal(1, rowCount);
        Assert.NotNull(_lastCapturedCommand);
        Assert.Equal("Observation/bp-1", _lastCapturedCommand!.Parameters["@ResourceKey"].Value);
        Assert.Equal("bp-1", _lastCapturedCommand.Parameters["@r0_id"].Value?.ToString());
    }

    [Fact]
    public async Task GivenNonMatchingObservation_WhenMaterialized_ThenZeroRowsAndDeleteOnly()
    {
        // Arrange — Heart rate observation, NOT blood pressure
        var observation = new Observation
        {
            Id = "hr-1",
            Status = ObservationStatus.Final,
            Code = new CodeableConcept("http://loinc.org", "8867-4", "Heart rate"),
            Effective = new FhirDateTime("2024-01-15T10:30:00Z"),
            Value = new Quantity(72, "/min", "http://unitsofmeasure.org"),
        };

        string bpViewDefJson = """
            {
                "name": "us_core_blood_pressures",
                "resource": "Observation",
                "constant": [{"name": "bp_code", "valueCode": "85354-9"}],
                "select": [{"column": [{"path": "id", "name": "id"}]}],
                "where": [{"path": "code.coding.exists(system='http://loinc.org' and code=%bp_code)"}]
            }
            """;

        // Track whether DeleteResourceAsync's SQL path is called (constructor mock handles capture)
        bool deleteCalled = false;
        _sqlRetryService.ExecuteSql(
            Arg.Any<SqlCommand>(),
            Arg.Any<Func<SqlCommand, CancellationToken, Task>>(),
            Arg.Any<ILogger>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<string>())
        .Returns(callInfo =>
        {
            deleteCalled = true;
            _lastCapturedCommand = callInfo.ArgAt<SqlCommand>(0);
            return Task.CompletedTask;
        });

        ResourceElement resourceElement = ToResourceElement(observation);

        // Act
        int rowCount = await _materializer.UpsertResourceAsync(
            bpViewDefJson,
            "us_core_blood_pressures",
            resourceElement,
            "Observation/hr-1",
            CancellationToken.None);

        // Assert — where filter excludes heart rate → 0 rows, delete path taken
        Assert.Equal(0, rowCount);
        Assert.True(deleteCalled, "DELETE should have been called for non-matching resource");
    }

    [Fact]
    public async Task GivenMultiplePatients_WhenEvaluatedAndMaterialized_ThenEachGetsCorrectResourceKey()
    {
        // Arrange
        var patients = new[]
        {
            new Patient { Id = "p1", Gender = AdministrativeGender.Male },
            new Patient { Id = "p2", Gender = AdministrativeGender.Female },
            new Patient { Id = "p3", Gender = AdministrativeGender.Other },
        };

        string viewDefJson = """
            {
                "name": "patient_genders",
                "resource": "Patient",
                "select": [{"column": [
                    { "name": "id", "path": "id" },
                    { "name": "gender", "path": "gender" }
                ]}]
            }
            """;

        var capturedResourceKeys = new List<string>();
        _sqlRetryService.ExecuteSql(
            Arg.Any<SqlCommand>(),
            Arg.Any<Func<SqlCommand, CancellationToken, Task>>(),
            Arg.Any<ILogger>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<string>())
        .Returns(callInfo =>
        {
            var cmd = callInfo.ArgAt<SqlCommand>(0);
            if (cmd.Parameters.Contains("@ResourceKey"))
            {
                capturedResourceKeys.Add(cmd.Parameters["@ResourceKey"].Value?.ToString()!);
            }

            _lastCapturedCommand = cmd;
            return Task.CompletedTask;
        });

        // Act — materialize each patient
        foreach (var patient in patients)
        {
            ResourceElement resourceElement = ToResourceElement(patient);
            await _materializer.UpsertResourceAsync(
                viewDefJson,
                "patient_genders",
                resourceElement,
                $"Patient/{patient.Id}",
                CancellationToken.None);
        }

        // Assert — each patient materialized with unique resource key
        Assert.Contains("Patient/p1", capturedResourceKeys);
        Assert.Contains("Patient/p2", capturedResourceKeys);
        Assert.Contains("Patient/p3", capturedResourceKeys);
    }

    private static ResourceElement ToResourceElement(Resource resource)
    {
        ITypedElement typedElement = resource.ToTypedElement();
        return new ResourceElement(typedElement);
    }
}
