// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.ViewDefinitionRun;
using Microsoft.Health.Fhir.SqlOnFhir.Channels;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using Microsoft.Health.Fhir.SqlOnFhir.Operations;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlOnFhir.Tests.Operations;

/// <summary>
/// Unit tests for <see cref="ViewDefinitionExportHandler"/>.
/// </summary>
public class ViewDefinitionExportHandlerTests
{
    private readonly IViewDefinitionSubscriptionManager _subscriptionManager;
    private readonly IViewDefinitionSchemaManager _schemaManager;
    private readonly IQueueClient _queueClient;
    private readonly IOptions<SqlOnFhirMaterializationConfiguration> _config;
    private readonly ViewDefinitionExportHandler _handler;

    public ViewDefinitionExportHandlerTests()
    {
        _subscriptionManager = Substitute.For<IViewDefinitionSubscriptionManager>();
        _schemaManager = Substitute.For<IViewDefinitionSchemaManager>();
        _queueClient = Substitute.For<IQueueClient>();
        _config = Options.Create(new SqlOnFhirMaterializationConfiguration
        {
            StorageAccountUri = "https://mystorage.blob.core.windows.net",
            DefaultContainer = "sqlfhir",
            DefaultTarget = MaterializationTarget.SqlServer,
        });

        _handler = new ViewDefinitionExportHandler(
            _subscriptionManager,
            _schemaManager,
            _queueClient,
            _config,
            NullLogger<ViewDefinitionExportHandler>.Instance);
    }

    [Fact]
    public async Task GivenRegisteredSqlMaterializedView_WhenExportRequested_ThenFastPathReturnsImmediately()
    {
        // Arrange
        var registration = new ViewDefinitionRegistration
        {
            ViewDefinitionJson = "{}",
            ViewDefinitionName = "patient_demographics",
            ResourceType = "Patient",
            Target = MaterializationTarget.SqlServer,
        };

        _subscriptionManager.GetRegistration("patient_demographics").Returns(registration);

        var request = new ViewDefinitionExportRequest(
            viewDefinitionName: "patient_demographics",
            format: "json");

        // Act
        ViewDefinitionExportResponse response = await _handler.Handle(request, CancellationToken.None);

        // Assert — fast path: immediate completion
        Assert.True(response.IsComplete);
        Assert.NotEmpty(response.Outputs);
        Assert.Contains("patient_demographics/$run", response.Outputs[0].Location);

        // No async job should be enqueued
        await _queueClient.DidNotReceive().EnqueueAsync(
            Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenRegisteredParquetView_WhenParquetExportRequested_ThenFastPathReturnsStorageUrl()
    {
        // Arrange
        var registration = new ViewDefinitionRegistration
        {
            ViewDefinitionJson = "{}",
            ViewDefinitionName = "blood_pressures",
            ResourceType = "Observation",
            Target = MaterializationTarget.Parquet,
        };

        _subscriptionManager.GetRegistration("blood_pressures").Returns(registration);

        var request = new ViewDefinitionExportRequest(
            viewDefinitionName: "blood_pressures",
            format: "parquet");

        // Act
        ViewDefinitionExportResponse response = await _handler.Handle(request, CancellationToken.None);

        // Assert — fast path: points to storage
        Assert.True(response.IsComplete);
        Assert.NotEmpty(response.Outputs);
        Assert.Contains("mystorage.blob.core.windows.net", response.Outputs[0].Location);
        Assert.Contains("blood_pressures", response.Outputs[0].Location);
    }

    [Fact]
    public async Task GivenUnregisteredView_WhenExportRequested_ThenAsyncJobEnqueued()
    {
        // Arrange
        _subscriptionManager.GetRegistration("new_view").Returns((ViewDefinitionRegistration?)null);

        string viewDefJson = """
            { "name": "new_view", "resource": "Patient", "select": [{"column": [{"name": "id", "path": "id"}]}] }
            """;

        _queueClient.EnqueueAsync(
                Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<JobInfo> { new JobInfo { Id = 42 } });

        var request = new ViewDefinitionExportRequest(
            viewDefinitionJson: viewDefJson,
            format: "ndjson");

        // Act
        ViewDefinitionExportResponse response = await _handler.Handle(request, CancellationToken.None);

        // Assert — async path: job enqueued
        Assert.False(response.IsComplete);
        Assert.Equal("42", response.ExportId);
        Assert.NotNull(response.StatusUrl);

        await _queueClient.Received(1).EnqueueAsync(
            (byte)QueueType.ViewDefinitionPopulation,
            Arg.Any<string[]>(),
            Arg.Any<long?>(),
            true,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenSqlViewButParquetRequested_WhenExportRequested_ThenFastPathServesFromRun()
    {
        // Arrange — SQL materialized, but parquet format requested
        var registration = new ViewDefinitionRegistration
        {
            ViewDefinitionJson = "{}",
            ViewDefinitionName = "conditions",
            ResourceType = "Condition",
            Target = MaterializationTarget.SqlServer,
        };

        _subscriptionManager.GetRegistration("conditions").Returns(registration);

        var request = new ViewDefinitionExportRequest(
            viewDefinitionName: "conditions",
            format: "parquet");

        // Act
        ViewDefinitionExportResponse response = await _handler.Handle(request, CancellationToken.None);

        // Assert — SQL materialized data can serve any format via $run
        Assert.True(response.IsComplete);
        Assert.Contains("$run?_format=parquet", response.Outputs[0].Location);
    }
}
