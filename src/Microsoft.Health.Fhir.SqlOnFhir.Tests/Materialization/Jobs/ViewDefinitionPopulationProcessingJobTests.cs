// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlOnFhir.Channels;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization.Jobs;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlOnFhir.Tests.Materialization.Jobs;

/// <summary>
/// Unit tests for <see cref="ViewDefinitionPopulationProcessingJob"/>.
/// </summary>
public class ViewDefinitionPopulationProcessingJobTests
{
    private readonly ISearchService _searchService;
    private readonly IResourceDeserializer _resourceDeserializer;
    private readonly IViewDefinitionMaterializer _materializer;
    private readonly IQueueClient _queueClient;
    private readonly IViewDefinitionSubscriptionManager _subscriptionManager;
    private readonly ViewDefinitionPopulationProcessingJob _job;

    private const string ViewDefinitionJson = """
        {
            "name": "patient_demographics",
            "resource": "Patient",
            "select": [
                { "column": [{ "name": "id", "path": "id" }] }
            ]
        }
        """;

    public ViewDefinitionPopulationProcessingJobTests()
    {
        _searchService = Substitute.For<ISearchService>();
        _resourceDeserializer = Substitute.For<IResourceDeserializer>();
        _materializer = Substitute.For<IViewDefinitionMaterializer>();
        _queueClient = Substitute.For<IQueueClient>();
        _subscriptionManager = Substitute.For<IViewDefinitionSubscriptionManager>();

        var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
        scopedSearchService.Value.Returns(_searchService);
        Func<IScoped<ISearchService>> searchServiceFactory = () => scopedSearchService;

        var config = Options.Create(new SqlOnFhirMaterializationConfiguration { DefaultTarget = MaterializationTarget.SqlServer });
        var factory = new MaterializerFactory(
            _materializer,
            config,
            NullLogger<MaterializerFactory>.Instance);

        _job = new ViewDefinitionPopulationProcessingJob(
            searchServiceFactory,
            _resourceDeserializer,
            factory,
            _subscriptionManager,
            _queueClient,
            Substitute.For<MediatR.IMediator>(),
            NullLogger<ViewDefinitionPopulationProcessingJob>.Instance);
    }

    [Fact]
    public async Task GivenResourcesWithNoContinuation_WhenExecuted_ThenAllResourcesMaterializedAndNoFollowUpJob()
    {
        // Arrange
        var definition = new ViewDefinitionPopulationProcessingJobDefinition
        {
            ViewDefinitionJson = ViewDefinitionJson,
            ViewDefinitionName = "patient_demographics",
            ResourceType = "Patient",
            BatchSize = 100,
            ContinuationToken = null,
        };

        var jobInfo = CreateJobInfo(definition);

        var mockWrapper = CreateMockResourceWrapper("Patient", "p1");
        var mockElement = Substitute.For<ResourceElement>(Substitute.For<Hl7.Fhir.ElementModel.ITypedElement>());

        _searchService.SearchAsync(
                "Patient",
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<ResourceVersionType>(),
                Arg.Any<bool>(),
                Arg.Any<bool>())
            .Returns(CreateSearchResult(new[] { mockWrapper }, continuationToken: null));

        _resourceDeserializer.Deserialize(mockWrapper).Returns(mockElement);
        _materializer.UpsertResourceBatchAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<(ResourceElement, string)>>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        // Act
        string result = await _job.ExecuteAsync(jobInfo, CancellationToken.None);

        // Assert
        await _materializer.Received(1).UpsertResourceBatchAsync(
            ViewDefinitionJson,
            "patient_demographics",
            Arg.Is<IReadOnlyList<(ResourceElement Resource, string ResourceKey)>>(b =>
                b.Count == 1 && b[0].ResourceKey == "Patient/p1"),
            Arg.Any<CancellationToken>());

        // No follow-up job should be enqueued
        await _queueClient.DidNotReceive().EnqueueAsync(
            Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());

        var resultObj = JsonConvert.DeserializeObject<ViewDefinitionPopulationProcessingJobResult>(result);
        Assert.NotNull(resultObj);
        Assert.Equal(1, resultObj!.ResourcesProcessed);
        Assert.Equal(1, resultObj.RowsInserted);
        Assert.Equal(0, resultObj.FailedResources);
        Assert.Null(resultObj.NextContinuationToken);
    }

    [Fact]
    public async Task GivenResourcesWithContinuation_WhenMaxBatchesReached_ThenFollowUpJobEnqueued()
    {
        // Arrange
        var definition = new ViewDefinitionPopulationProcessingJobDefinition
        {
            ViewDefinitionJson = ViewDefinitionJson,
            ViewDefinitionName = "patient_demographics",
            ResourceType = "Patient",
            BatchSize = 1,
            ContinuationToken = null,
        };

        var jobInfo = CreateJobInfo(definition);

        // Always return results with continuation token (simulating a large dataset)
        _searchService.SearchAsync(
                "Patient",
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<ResourceVersionType>(),
                Arg.Any<bool>(),
                Arg.Any<bool>())
            .Returns(callInfo =>
            {
                var wrapper = CreateMockResourceWrapper("Patient", "px");
                return CreateSearchResult(new[] { wrapper }, continuationToken: "next-token");
            });

        var mockElement = Substitute.For<ResourceElement>(Substitute.For<Hl7.Fhir.ElementModel.ITypedElement>());
        _resourceDeserializer.Deserialize(Arg.Any<ResourceWrapper>()).Returns(mockElement);
        _materializer.UpsertResourceBatchAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<(ResourceElement, string)>>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        _queueClient.EnqueueAsync(
                Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<JobInfo> { new JobInfo { Id = 3 } });

        // Act
        string result = await _job.ExecuteAsync(jobInfo, CancellationToken.None);

        // Assert - should enqueue a follow-up job
        await _queueClient.Received(1).EnqueueAsync(
            (byte)QueueType.ViewDefinitionPopulation,
            Arg.Any<string[]>(),
            jobInfo.GroupId,
            false,
            Arg.Any<CancellationToken>());

        var resultObj = JsonConvert.DeserializeObject<ViewDefinitionPopulationProcessingJobResult>(result);
        Assert.NotNull(resultObj);
        Assert.True(resultObj!.ResourcesProcessed > 0);
        Assert.NotNull(resultObj.NextContinuationToken);
    }

    [Fact]
    public async Task GivenEmptySearchResult_WhenExecuted_ThenZeroResourcesProcessed()
    {
        // Arrange
        var definition = new ViewDefinitionPopulationProcessingJobDefinition
        {
            ViewDefinitionJson = ViewDefinitionJson,
            ViewDefinitionName = "patient_demographics",
            ResourceType = "Patient",
            BatchSize = 100,
        };

        var jobInfo = CreateJobInfo(definition);

        _searchService.SearchAsync(
                "Patient",
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<ResourceVersionType>(),
                Arg.Any<bool>(),
                Arg.Any<bool>())
            .Returns(CreateSearchResult(Array.Empty<ResourceWrapper>(), continuationToken: null));

        // Act
        string result = await _job.ExecuteAsync(jobInfo, CancellationToken.None);

        // Assert
        await _materializer.DidNotReceive().UpsertResourceBatchAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<(ResourceElement, string)>>(),
            Arg.Any<CancellationToken>());

        var resultObj = JsonConvert.DeserializeObject<ViewDefinitionPopulationProcessingJobResult>(result);
        Assert.NotNull(resultObj);
        Assert.Equal(0, resultObj!.ResourcesProcessed);
        Assert.Equal(0, resultObj.RowsInserted);
    }

    [Fact]
    public async Task GivenMaterializerFailure_WhenExecuted_ThenFailureCountedAndProcessingContinues()
    {
        // Arrange
        var definition = new ViewDefinitionPopulationProcessingJobDefinition
        {
            ViewDefinitionJson = ViewDefinitionJson,
            ViewDefinitionName = "patient_demographics",
            ResourceType = "Patient",
            BatchSize = 100,
        };

        var jobInfo = CreateJobInfo(definition);

        var wrapper1 = CreateMockResourceWrapper("Patient", "p1");
        var wrapper2 = CreateMockResourceWrapper("Patient", "p2");
        var mockElement = Substitute.For<ResourceElement>(Substitute.For<Hl7.Fhir.ElementModel.ITypedElement>());

        _searchService.SearchAsync(
                "Patient",
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<ResourceVersionType>(),
                Arg.Any<bool>(),
                Arg.Any<bool>())
            .Returns(CreateSearchResult(new[] { wrapper1, wrapper2 }, continuationToken: null));

        _resourceDeserializer.Deserialize(Arg.Any<ResourceWrapper>()).Returns(mockElement);

        // Batch upsert fails, triggering fallback to per-resource upserts.
        _materializer.UpsertResourceBatchAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<(ResourceElement, string)>>(),
                Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("Batch failure"));

        // Fallback path: first resource succeeds, second throws.
        _materializer.UpsertResourceAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ResourceElement>(), "Patient/p1", Arg.Any<CancellationToken>())
            .Returns(1);

        _materializer.UpsertResourceAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ResourceElement>(), "Patient/p2", Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("Test failure"));

        // Act
        string result = await _job.ExecuteAsync(jobInfo, CancellationToken.None);

        // Assert - both resources attempted, one failed
        var resultObj = JsonConvert.DeserializeObject<ViewDefinitionPopulationProcessingJobResult>(result);
        Assert.NotNull(resultObj);
        Assert.Equal(1, resultObj!.ResourcesProcessed);
        Assert.Equal(1, resultObj.FailedResources);
    }

    private static JobInfo CreateJobInfo(ViewDefinitionPopulationProcessingJobDefinition definition)
    {
        return new JobInfo
        {
            Id = 2,
            GroupId = 100,
            QueueType = (byte)QueueType.ViewDefinitionPopulation,
            Definition = JsonConvert.SerializeObject(definition),
        };
    }

    private static ResourceWrapper CreateMockResourceWrapper(string resourceType, string resourceId)
    {
        return new ResourceWrapper(
            resourceId,
            "1",
            resourceType,
            new RawResource("{ }", Fhir.Core.Models.FhirResourceFormat.Json, true),
            null,
            DateTimeOffset.UtcNow,
            false,
            null,
            null,
            null);
    }

    private static SearchResult CreateSearchResult(ResourceWrapper[] wrappers, string? continuationToken)
    {
        var entries = wrappers.Select(w => new SearchResultEntry(w)).ToList();

        return new SearchResult(
            entries,
            continuationToken,
            Array.Empty<(SearchParameterInfo, SortOrder)>(),
            Array.Empty<Tuple<string, string>>());
    }
}
