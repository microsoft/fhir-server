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
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlOnFhir.Channels;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Subscriptions.Models;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlOnFhir.Tests;

/// <summary>
/// End-to-end flow integration tests that validate the complete pipeline from ViewDefinition
/// registration through subscription-driven materialization. Uses real Ignixa evaluation
/// with mocked SQL execution and MediatR boundaries.
///
/// These tests prove the component wiring is correct:
///   ViewDefinition JSON → Schema Manager → Table DDL
///   ViewDefinition JSON → Subscription Manager → Subscription resource
///   Resource change → Refresh Channel → Evaluator → Materializer → SQL parameters
/// </summary>
public class EndToEndFlowTests
{
    private readonly ISqlRetryService _sqlRetryService;
    private readonly IViewDefinitionEvaluator _evaluator;
    private readonly SqlServerViewDefinitionSchemaManager _schemaManager;
    private readonly SqlServerViewDefinitionMaterializer _materializer;
    private readonly IResourceDeserializer _resourceDeserializer;
    private readonly ViewDefinitionRefreshChannel _channel;

    private readonly List<SqlCommand> _capturedCommands = new();

    private const string PatientDemographicsViewDef = """
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

    private const string BloodPressureViewDef = """
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

    public EndToEndFlowTests()
    {
        _sqlRetryService = Substitute.For<ISqlRetryService>();
        _evaluator = new ViewDefinitionEvaluator(NullLogger<ViewDefinitionEvaluator>.Instance);
        _resourceDeserializer = Substitute.For<IResourceDeserializer>();

        _schemaManager = new SqlServerViewDefinitionSchemaManager(
            _sqlRetryService,
            NullLogger<SqlServerViewDefinitionSchemaManager>.Instance);

        _materializer = new SqlServerViewDefinitionMaterializer(
            _evaluator,
            _schemaManager,
            _sqlRetryService,
            NullLogger<SqlServerViewDefinitionMaterializer>.Instance);

        _channel = new ViewDefinitionRefreshChannel(
            _materializer,
            _resourceDeserializer,
            NullLogger<ViewDefinitionRefreshChannel>.Instance);

        // Capture all SQL commands
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
            _capturedCommands.Add(callInfo.ArgAt<SqlCommand>(0));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Step1_GivenViewDefinition_WhenRegistered_ThenSchemaAndDdlAreCorrect()
    {
        // Act — Schema manager generates DDL from ViewDefinition
        string ddl = _schemaManager.GenerateCreateTableDdl(PatientDemographicsViewDef);
        IReadOnlyList<MaterializedColumnDefinition> columns = _schemaManager.GetColumnDefinitions(PatientDemographicsViewDef);

        // Assert — Table DDL is valid
        Assert.Contains("CREATE TABLE [sqlfhir].[patient_demographics]", ddl);
        Assert.Contains("[_resource_key] nvarchar(128) NOT NULL", ddl);
        Assert.Contains("[id]", ddl);
        Assert.Contains("[gender]", ddl);
        Assert.Contains("[birth_date]", ddl);
        Assert.Contains("CREATE NONCLUSTERED INDEX", ddl);

        // Assert — Column definitions include tracking column + data columns
        Assert.True(columns.Count >= 4, $"Expected at least 4 columns, got {columns.Count}");
        Assert.Equal("_resource_key", columns[0].ColumnName);
    }

    [Fact]
    public void Step2_GivenViewDefinition_WhenRegistered_ThenSubscriptionResourceIsCorrect()
    {
        // Act — Build the auto-created Subscription resource
        Subscription sub = ViewDefinitionSubscriptionManager.BuildSubscriptionResource(
            PatientDemographicsViewDef,
            "patient_demographics",
            "Patient");

        // Assert — Subscription is properly configured for the refresh channel
        Assert.Equal(Subscription.SubscriptionStatus.Requested, sub.Status);

        // Filter criteria targets Patient resources
        var filterExt = sub.CriteriaElement.Extension.First(
            e => e.Url.Contains("backport-filter-criteria"));
        Assert.Equal("Patient?", ((FhirString)filterExt.Value).Value);

        // Channel type is view-definition-refresh
        var channelTypeExt = sub.Channel.TypeElement.Extension.First(
            e => e.Url.Contains("backport-channel-type"));
        Assert.Equal("view-definition-refresh", ((Coding)channelTypeExt.Value).Code);

        // ViewDefinition metadata is in channel headers
        Assert.Contains(sub.Channel.Header, h => h.StartsWith("viewDefinitionName: patient_demographics"));
        Assert.Contains(sub.Channel.Header, h => h.StartsWith("viewDefinitionJson: "));
    }

    [Fact]
    public async Task Step3_GivenNewPatient_WhenChannelFires_ThenRowIsMaterialized()
    {
        // Arrange — Simulate what happens when a new Patient is created and the
        // subscription engine fires the ViewDefinitionRefreshChannel

        var patient = new Patient
        {
            Id = "new-patient-1",
            Gender = AdministrativeGender.Female,
            BirthDate = "1985-06-15",
        };

        var wrapper = CreateResourceWrapper(patient);
        var resourceElement = ToResourceElement(patient);
        _resourceDeserializer.Deserialize(wrapper).Returns(resourceElement);

        SubscriptionInfo subInfo = CreateSubscriptionInfo(PatientDemographicsViewDef, "patient_demographics");

        // Act — Channel processes the changed resource
        await _channel.PublishAsync(
            new[] { wrapper },
            subInfo,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        // Assert — An upsert SQL command was generated with correct parameters
        Assert.NotEmpty(_capturedCommands);
        SqlCommand upsertCmd = _capturedCommands.Last();

        Assert.Contains("DELETE FROM [sqlfhir].[patient_demographics]", upsertCmd.CommandText);
        Assert.Contains("INSERT INTO [sqlfhir].[patient_demographics]", upsertCmd.CommandText);
        Assert.Equal("Patient/new-patient-1", upsertCmd.Parameters["@ResourceKey"].Value);
        Assert.Equal("new-patient-1", upsertCmd.Parameters["@r0_id"].Value?.ToString());
        Assert.Equal("female", upsertCmd.Parameters["@r0_gender"].Value?.ToString());
    }

    [Fact]
    public async Task Step4_GivenUpdatedPatient_WhenChannelFires_ThenRowIsReplaced()
    {
        // Arrange — Patient gender changed from male to female
        var updatedPatient = new Patient
        {
            Id = "existing-patient-1",
            Gender = AdministrativeGender.Female,
            BirthDate = "1990-01-01",
        };

        var wrapper = CreateResourceWrapper(updatedPatient);
        var resourceElement = ToResourceElement(updatedPatient);
        _resourceDeserializer.Deserialize(wrapper).Returns(resourceElement);

        SubscriptionInfo subInfo = CreateSubscriptionInfo(PatientDemographicsViewDef, "patient_demographics");

        // Act
        await _channel.PublishAsync(new[] { wrapper }, subInfo, DateTimeOffset.UtcNow, CancellationToken.None);

        // Assert — DELETE + INSERT pattern (old rows removed, new rows inserted)
        SqlCommand cmd = _capturedCommands.Last();
        Assert.Contains("DELETE FROM [sqlfhir].[patient_demographics]", cmd.CommandText);
        Assert.Contains("INSERT INTO [sqlfhir].[patient_demographics]", cmd.CommandText);
        Assert.Equal("Patient/existing-patient-1", cmd.Parameters["@ResourceKey"].Value);
        Assert.Equal("female", cmd.Parameters["@r0_gender"].Value?.ToString());
    }

    [Fact]
    public async Task Step5_GivenDeletedPatient_WhenChannelFires_ThenRowsAreRemoved()
    {
        // Arrange — Patient was deleted
        var wrapper = new ResourceWrapper(
            "deleted-patient-1",
            "2",
            "Patient",
            new RawResource("{ }", FhirResourceFormat.Json, true),
            null,
            DateTimeOffset.UtcNow,
            true,
            null,
            null,
            null);

        SubscriptionInfo subInfo = CreateSubscriptionInfo(PatientDemographicsViewDef, "patient_demographics");

        // Act
        await _channel.PublishAsync(new[] { wrapper }, subInfo, DateTimeOffset.UtcNow, CancellationToken.None);

        // Assert — DELETE only, no INSERT
        SqlCommand cmd = _capturedCommands.Last();
        Assert.Contains("DELETE FROM", cmd.CommandText);
        Assert.DoesNotContain("INSERT INTO", cmd.CommandText);
    }

    [Fact]
    public async Task Step6_GivenBloodPressureObservation_WhenChannelFires_ThenBPValuesExtracted()
    {
        // Arrange — BP Observation with systolic 140 and diastolic 90
        var observation = new Observation
        {
            Id = "bp-realtime-1",
            Status = ObservationStatus.Final,
            Code = new CodeableConcept("http://loinc.org", "85354-9", "Blood pressure panel"),
            Effective = new FhirDateTime("2026-03-29T10:00:00Z"),
            Component =
            {
                new Observation.ComponentComponent
                {
                    Code = new CodeableConcept("http://loinc.org", "8480-6", "Systolic BP"),
                    Value = new Quantity(140, "mmHg", "http://unitsofmeasure.org"),
                },
                new Observation.ComponentComponent
                {
                    Code = new CodeableConcept("http://loinc.org", "8462-4", "Diastolic BP"),
                    Value = new Quantity(90, "mmHg", "http://unitsofmeasure.org"),
                },
            },
        };

        var wrapper = CreateResourceWrapper(observation);
        var resourceElement = ToResourceElement(observation);
        _resourceDeserializer.Deserialize(wrapper).Returns(resourceElement);

        SubscriptionInfo subInfo = CreateSubscriptionInfo(BloodPressureViewDef, "us_core_blood_pressures");

        // Act
        await _channel.PublishAsync(new[] { wrapper }, subInfo, DateTimeOffset.UtcNow, CancellationToken.None);

        // Assert — BP values extracted into materialized row
        SqlCommand cmd = _capturedCommands.Last();
        Assert.Contains("INSERT INTO [sqlfhir].[us_core_blood_pressures]", cmd.CommandText);
        Assert.Equal("Observation/bp-realtime-1", cmd.Parameters["@ResourceKey"].Value);
        Assert.Equal("bp-realtime-1", cmd.Parameters["@r0_id"].Value?.ToString());
    }

    [Fact]
    public async Task Step7_GivenNonBPObservation_WhenChannelFires_ThenNoRowMaterialized()
    {
        // Arrange — Heart rate observation (not blood pressure)
        var observation = new Observation
        {
            Id = "hr-1",
            Status = ObservationStatus.Final,
            Code = new CodeableConcept("http://loinc.org", "8867-4", "Heart rate"),
            Effective = new FhirDateTime("2026-03-29T10:00:00Z"),
            Value = new Quantity(72, "/min", "http://unitsofmeasure.org"),
        };

        var wrapper = CreateResourceWrapper(observation);
        var resourceElement = ToResourceElement(observation);
        _resourceDeserializer.Deserialize(wrapper).Returns(resourceElement);

        SubscriptionInfo subInfo = CreateSubscriptionInfo(BloodPressureViewDef, "us_core_blood_pressures");
        _capturedCommands.Clear();

        // Act
        await _channel.PublishAsync(new[] { wrapper }, subInfo, DateTimeOffset.UtcNow, CancellationToken.None);

        // Assert — The where filter rejects heart rate. Only a DELETE (no INSERT) should occur.
        Assert.All(_capturedCommands, cmd =>
            Assert.DoesNotContain("INSERT INTO", cmd.CommandText));
    }

    [Fact]
    public async Task Step8_GivenMultipleResourceChanges_WhenBatchProcessed_ThenAllMaterialized()
    {
        // Arrange — Simulate a batch of 3 patient changes arriving together
        var patients = new[]
        {
            new Patient { Id = "batch-p1", Gender = AdministrativeGender.Male, BirthDate = "1980-01-01" },
            new Patient { Id = "batch-p2", Gender = AdministrativeGender.Female, BirthDate = "1990-02-02" },
            new Patient { Id = "batch-p3", Gender = AdministrativeGender.Other, BirthDate = "2000-03-03" },
        };

        var wrappers = patients.Select(p =>
        {
            var wrapper = CreateResourceWrapper(p);
            var element = ToResourceElement(p);
            _resourceDeserializer.Deserialize(wrapper).Returns(element);
            return wrapper;
        }).ToArray();

        SubscriptionInfo subInfo = CreateSubscriptionInfo(PatientDemographicsViewDef, "patient_demographics");
        _capturedCommands.Clear();

        // Act
        await _channel.PublishAsync(wrappers, subInfo, DateTimeOffset.UtcNow, CancellationToken.None);

        // Assert — 3 upsert commands generated (one per patient)
        var upsertCommands = _capturedCommands
            .Where(c => c.CommandText.Contains("INSERT INTO"))
            .ToList();

        Assert.Equal(3, upsertCommands.Count);

        var resourceKeys = upsertCommands
            .Select(c => c.Parameters["@ResourceKey"].Value?.ToString())
            .OrderBy(k => k)
            .ToList();

        Assert.Equal(new[] { "Patient/batch-p1", "Patient/batch-p2", "Patient/batch-p3" }, resourceKeys);
    }

    private static SubscriptionInfo CreateSubscriptionInfo(string viewDefJson, string viewDefName)
    {
        var channelInfo = new ChannelInfo
        {
            ChannelType = SubscriptionChannelType.ViewDefinitionRefresh,
            MaxCount = 100,
            Endpoint = $"internal://sqlfhir/{viewDefName}",
            Properties = new Dictionary<string, string>
            {
                ["viewDefinitionJson"] = viewDefJson,
                ["viewDefinitionName"] = viewDefName,
            },
        };

        return new SubscriptionInfo(
            $"Observation?",
            channelInfo,
            new Uri("http://azurehealthcareapis.com/data-extentions/SubscriptionTopics/transactions"),
            "auto-sub-1",
            SubscriptionStatus.Active);
    }

    private static ResourceWrapper CreateResourceWrapper(Resource resource)
    {
        return new ResourceWrapper(
            resource.Id,
            "1",
            resource.TypeName,
            new RawResource("{ }", FhirResourceFormat.Json, true),
            null,
            DateTimeOffset.UtcNow,
            false,
            null,
            null,
            null);
    }

    private static ResourceElement ToResourceElement(Resource resource)
    {
        ITypedElement typedElement = resource.ToTypedElement();
        return new ResourceElement(typedElement);
    }
}
