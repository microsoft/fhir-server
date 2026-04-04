// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization.Jobs;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlOnFhir.Tests.Materialization.Jobs;

/// <summary>
/// Unit tests for <see cref="ViewDefinitionPopulationOrchestratorJob"/>.
/// </summary>
public class ViewDefinitionPopulationOrchestratorJobTests
{
    private readonly IViewDefinitionSchemaManager _schemaManager;
    private readonly IQueueClient _queueClient;
    private readonly ViewDefinitionPopulationOrchestratorJob _job;

    private const string ViewDefinitionJson = """
        {
            "name": "patient_demographics",
            "resource": "Patient",
            "select": [
                { "column": [{ "name": "id", "path": "id" }] }
            ]
        }
        """;

    public ViewDefinitionPopulationOrchestratorJobTests()
    {
        _schemaManager = Substitute.For<IViewDefinitionSchemaManager>();
        _queueClient = Substitute.For<IQueueClient>();

        _job = new ViewDefinitionPopulationOrchestratorJob(
            _schemaManager,
            _queueClient,
            NullLogger<ViewDefinitionPopulationOrchestratorJob>.Instance);
    }

    [Fact]
    public async Task GivenNewViewDefinition_WhenExecuted_ThenTableIsCreatedAndProcessingJobEnqueued()
    {
        // Arrange
        var definition = new ViewDefinitionPopulationOrchestratorJobDefinition
        {
            ViewDefinitionJson = ViewDefinitionJson,
            ViewDefinitionName = "patient_demographics",
            ResourceType = "Patient",
            BatchSize = 100,
        };

        var jobInfo = CreateJobInfo(definition);

        _schemaManager.TableExistsAsync("patient_demographics", Arg.Any<CancellationToken>())
            .Returns(false);

        _schemaManager.CreateTableAsync(ViewDefinitionJson, Arg.Any<CancellationToken>())
            .Returns("[sqlfhir].[patient_demographics]");

        _queueClient.EnqueueAsync(
                Arg.Any<byte>(),
                Arg.Any<string[]>(),
                Arg.Any<long?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<JobInfo> { new JobInfo { Id = 2 } });

        // Act
        string result = await _job.ExecuteAsync(jobInfo, CancellationToken.None);

        // Assert
        await _schemaManager.Received(1).CreateTableAsync(ViewDefinitionJson, Arg.Any<CancellationToken>());

        await _queueClient.Received(1).EnqueueAsync(
            (byte)QueueType.ViewDefinitionPopulation,
            Arg.Is<string[]>(defs => defs.Length == 1),
            jobInfo.GroupId,
            false,
            Arg.Any<CancellationToken>());

        Assert.Contains("patient_demographics", result);
        Assert.Contains("\"TableCreated\":true", result);
    }

    [Fact]
    public async Task GivenExistingTable_WhenExecuted_ThenTableNotRecreatedButProcessingJobEnqueued()
    {
        // Arrange
        var definition = new ViewDefinitionPopulationOrchestratorJobDefinition
        {
            ViewDefinitionJson = ViewDefinitionJson,
            ViewDefinitionName = "patient_demographics",
            ResourceType = "Patient",
            BatchSize = 50,
        };

        var jobInfo = CreateJobInfo(definition);

        _schemaManager.TableExistsAsync("patient_demographics", Arg.Any<CancellationToken>())
            .Returns(true);

        _queueClient.EnqueueAsync(
                Arg.Any<byte>(),
                Arg.Any<string[]>(),
                Arg.Any<long?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<JobInfo> { new JobInfo { Id = 2 } });

        // Act
        string result = await _job.ExecuteAsync(jobInfo, CancellationToken.None);

        // Assert - should NOT create table
        await _schemaManager.DidNotReceive().CreateTableAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Should still enqueue processing job
        await _queueClient.Received(1).EnqueueAsync(
            (byte)QueueType.ViewDefinitionPopulation,
            Arg.Any<string[]>(),
            Arg.Any<long?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());

        Assert.Contains("\"TableCreated\":false", result);
    }

    [Fact]
    public async Task GivenOrchestratorJob_WhenEnqueueingProcessing_ThenDefinitionContainsCorrectValues()
    {
        // Arrange
        var definition = new ViewDefinitionPopulationOrchestratorJobDefinition
        {
            ViewDefinitionJson = ViewDefinitionJson,
            ViewDefinitionName = "patient_demographics",
            ResourceType = "Patient",
            BatchSize = 200,
        };

        var jobInfo = CreateJobInfo(definition);
        _schemaManager.TableExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        string[]? capturedDefinitions = null;
        _queueClient.EnqueueAsync(
                Arg.Any<byte>(),
                Arg.Do<string[]>(d => capturedDefinitions = d),
                Arg.Any<long?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<JobInfo> { new JobInfo { Id = 2 } });

        // Act
        await _job.ExecuteAsync(jobInfo, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedDefinitions);
        Assert.Single(capturedDefinitions!);

        var processingDef = JsonConvert.DeserializeObject<ViewDefinitionPopulationProcessingJobDefinition>(capturedDefinitions![0]);
        Assert.NotNull(processingDef);
        Assert.Equal("patient_demographics", processingDef!.ViewDefinitionName);
        Assert.Equal("Patient", processingDef.ResourceType);
        Assert.Equal(200, processingDef.BatchSize);
        Assert.Null(processingDef.ContinuationToken);
    }

    private static JobInfo CreateJobInfo(ViewDefinitionPopulationOrchestratorJobDefinition definition)
    {
        return new JobInfo
        {
            Id = 1,
            GroupId = 100,
            QueueType = (byte)QueueType.ViewDefinitionPopulation,
            Definition = JsonConvert.SerializeObject(definition),
        };
    }
}
