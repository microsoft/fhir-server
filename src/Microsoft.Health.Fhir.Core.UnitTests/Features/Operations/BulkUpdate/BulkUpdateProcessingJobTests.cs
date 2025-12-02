// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Rest;
using Medino;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Messages;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.BulkUpdate
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BulkUpdate)]
    public class BulkUpdateProcessingJobTests
    {
        private readonly IBulkUpdateService _updater;
        private readonly BulkUpdateProcessingJob _processingJob;
        private readonly ISupportedProfilesStore _supportedProfilesStore;
        private readonly IQueueClient _queueClient;
        private readonly IMediator _mediator;

        public BulkUpdateProcessingJobTests()
        {
            _queueClient = Substitute.For<IQueueClient>();
            _updater = Substitute.For<IBulkUpdateService>();
            _supportedProfilesStore = Substitute.For<ISupportedProfilesStore>();
            _mediator = Substitute.For<IMediator>();
            _processingJob = new BulkUpdateProcessingJob(_queueClient, _updater.CreateMockScopeFactory(), Substitute.For<RequestContextAccessor<IFhirRequestContext>>(), _supportedProfilesStore, _mediator, Substitute.For<ILogger<BulkUpdateProcessingJob>>());
        }

        [Fact]
        public async Task GivenProcessingJob_WhenJobIsRun_ThenResourcesAreUpdated()
        {
            _updater.ClearReceivedCalls();
            var definition = new BulkUpdateDefinition(JobType.BulkUpdateProcessing, null, null, "test", "test", "test", null, isParallel: true, readNextPage: true);
            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(definition),
            };

            var substituteResults = new BulkUpdateResult();
            substituteResults.ResourcesUpdated["Patient"] = 3;
            substituteResults.ResourcesUpdated["Observation"] = 1;
            substituteResults.ResourcesIgnored["CarePlan"] = 2;

            _updater.UpdateMultipleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), 0, Arg.Is<bool>(b => !b), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<BundleResourceContext>(), Arg.Any<CancellationToken>())
                .Returns(args => substituteResults);

            var result = JsonConvert.DeserializeObject<BulkUpdateResult>(await _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
            Assert.Equal(3, result.ResourcesUpdated["Patient"]);
            Assert.Equal(1, result.ResourcesUpdated["Observation"]);
            Assert.Single(result.ResourcesIgnored);
            Assert.Equal(2, result.ResourcesIgnored["CarePlan"]);

            await _updater.ReceivedWithAnyArgs(1).UpdateMultipleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), 0, Arg.Is<bool>(b => !b), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<BundleResourceContext>(), Arg.Any<CancellationToken>());
            await _mediator.Received(1).PublishAsync(Arg.Is<BulkUpdateMetricsNotification>(n => n.JobId == jobInfo.Id && n.ResourcesUpdated == 4), Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(10, 1)]
        [InlineData(100, 1)]
        [InlineData(1000, 1)]
        [InlineData(5000, 5)]
        [InlineData(10000, 10)]
        public async Task GivenProcessingJobWithContinuationToken_WhenJobIsRun_ThenIncludedResourcesAreUpdated(uint maximumNumberOfResourcesPerQuery, uint readUpto)
        {
            _updater.ClearReceivedCalls();

            var searchParams = new List<Tuple<string, string>>()
            {
                new Tuple<string, string>(KnownQueryParameterNames.ContinuationToken, KnownQueryParameterNames.ContinuationToken),
            };
            var definition = new BulkUpdateDefinition(JobType.BulkUpdateProcessing, null, searchParams, "test", "test", "test", null, isParallel: true, readNextPage: false, maximumNumberOfResourcesPerQuery: maximumNumberOfResourcesPerQuery);
            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(definition),
            };

            var substituteResults = new BulkUpdateResult();
            substituteResults.ResourcesUpdated["Patient"] = 3;

            _updater.UpdateMultipleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), readUpto, Arg.Is<bool>(b => !b), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<BundleResourceContext>(), Arg.Any<CancellationToken>())
                .Returns(args => substituteResults);

            var result = JsonConvert.DeserializeObject<BulkUpdateResult>(await _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
            Assert.Single(result.ResourcesUpdated);
            Assert.Equal(3, result.ResourcesUpdated["Patient"]);

            await _updater.ReceivedWithAnyArgs(1).UpdateMultipleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), readUpto, Arg.Is<bool>(b => !b), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<BundleResourceContext>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenProcessingJobWithSurrogateIds_WhenJobIsRun_ThenIdsAreAddedToSearchParametersAndResourcesAreUpdated()
        {
            _updater.ClearReceivedCalls();

            var searchParams = new List<Tuple<string, string>>()
            {
                new Tuple<string, string>(KnownQueryParameterNames.GlobalStartSurrogateId, KnownQueryParameterNames.GlobalStartSurrogateId),
                new Tuple<string, string>(KnownQueryParameterNames.GlobalEndSurrogateId, KnownQueryParameterNames.GlobalEndSurrogateId),
                new Tuple<string, string>(KnownQueryParameterNames.StartSurrogateId, KnownQueryParameterNames.StartSurrogateId),
                new Tuple<string, string>(KnownQueryParameterNames.EndSurrogateId, KnownQueryParameterNames.EndSurrogateId),
                new Tuple<string, string>(KnownQueryParameterNames.Type, KnownQueryParameterNames.Type),
            };
            var definition = new BulkUpdateDefinition(JobType.BulkUpdateProcessing, "type", searchParams, "test", "test", "test", null, isParallel: true, readNextPage: false, startSurrogateId: "startSurrogateId", endSurrogateId: "endSurrogateId", globalStartSurrogateId: "globalStartSurrogateId", globalEndSurrogateId: "globalEndSurrogateId");
            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(definition),
            };

            var substituteResults = new BulkUpdateResult();
            substituteResults.ResourcesUpdated["Patient"] = 3;

            _updater.UpdateMultipleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), 0, Arg.Any<bool>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<BundleResourceContext>(), Arg.Any<CancellationToken>())
                .Returns(args => substituteResults);

            var result = JsonConvert.DeserializeObject<BulkUpdateResult>(await _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
            Assert.Single(result.ResourcesUpdated);
            Assert.Equal(3, result.ResourcesUpdated["Patient"]);

            await _updater.ReceivedWithAnyArgs(1).UpdateMultipleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), 0, Arg.Any<bool>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<BundleResourceContext>(), Arg.Any<CancellationToken>());
            await _mediator.Received(1).PublishAsync(Arg.Is<BulkUpdateMetricsNotification>(n => n.JobId == jobInfo.Id && n.ResourcesUpdated == 3), Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(10000)]
        [InlineData(5000)]
        public async Task GivenProcessingJobWithSurrogateIds_WhenJobIsRun_QueryParametersAreBuiltForRangeSubjob(uint maximumNumberOfResourcesPerQuery)
        {
            // Arrange: Create a definition with global surrogate properties.
            var definition = new BulkUpdateDefinition(JobType.BulkUpdateProcessing, "type", null, "test", "test", "test", null, isParallel: true, readNextPage: false, startSurrogateId: "startSurrogateId", endSurrogateId: "endSurrogateId", globalStartSurrogateId: "globalStartSurrogateId", globalEndSurrogateId: "globalEndSurrogateId", maximumNumberOfResourcesPerQuery: maximumNumberOfResourcesPerQuery);
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(definition),
            };

            // Capture the queryParametersList passed to UpdateMultipleAsync.
            List<Tuple<string, string>> capturedQueryParameters = null;
            _updater
                .UpdateMultipleAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<bool>(),
                    Arg.Any<uint>(),
                    Arg.Any<bool>(),
                    Arg.Do<IReadOnlyList<Tuple<string, string>>>(q => capturedQueryParameters = q.ToList()),
                    Arg.Any<BundleResourceContext>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new BulkUpdateResult()));

            // Act: Execute the job.
            // Since we are only interested in the query parameters, we let the job run and ignore the result.
            await _processingJob.ExecuteAsync(jobInfo, CancellationToken.None);

            // Assert: We expect queryParametersList to contain 6 tuples.
            Assert.NotNull(capturedQueryParameters);

            // Expected keys in order for range subjob.
            var expected = new List<(string Key, string Value)>
            {
                (KnownQueryParameterNames.Type, definition.Type),
                (KnownQueryParameterNames.GlobalEndSurrogateId, definition.GlobalEndSurrogateId),
                (KnownQueryParameterNames.EndSurrogateId, definition.EndSurrogateId),
                (KnownQueryParameterNames.GlobalStartSurrogateId, definition.GlobalStartSurrogateId),
                (KnownQueryParameterNames.StartSurrogateId, definition.StartSurrogateId),
                (KnownQueryParameterNames.Count, definition.MaximumNumberOfResourcesPerQuery.ToString()),
            };

            foreach (var exp in expected)
            {
                Assert.Contains(capturedQueryParameters, q => q.Item1 == exp.Key && q.Item2 == exp.Value);
            }
        }

        [Theory]
        [InlineData(2500, "1000")]
        [InlineData(1000, "1000")]
        [InlineData(1500, "1000")] // above processing batch size; expect ProcessingBatchSize applied.
        [InlineData(800, "800")] // below processing batch size.
        public async Task When_GlobalEndSurrogateId_IsNull_QueryParametersCountIsBasedOnProcessingBatchSize(uint maximumNumberOfResourcesPerQuery, string expectedCount)
        {
            // Arrange: Create definition with GlobalEndSurrogateId not set.
            // Arrange: Create a definition with global surrogate properties.
            var definition = new BulkUpdateDefinition(JobType.BulkUpdateProcessing, "type", null, "test", "test", "test", null, isParallel: true, readNextPage: false, maximumNumberOfResourcesPerQuery: maximumNumberOfResourcesPerQuery);
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(definition),
            };

            List<Tuple<string, string>> capturedQueryParameters = null;
            _updater
                .UpdateMultipleAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<bool>(),
                    Arg.Any<uint>(),
                    Arg.Any<bool>(),
                    Arg.Do<IReadOnlyList<Tuple<string, string>>>(q => capturedQueryParameters = q.ToList()),
                    Arg.Any<BundleResourceContext>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new BulkUpdateResult()));

            await _processingJob.ExecuteAsync(jobInfo, CancellationToken.None);

            // Assert: Since GlobalEndSurrogateId is null, only one queryParameter for count is added.
            Assert.NotNull(capturedQueryParameters);
            var countParam = capturedQueryParameters.FirstOrDefault(q => q.Item1 == KnownQueryParameterNames.Count);
            Assert.NotNull(countParam);
            Assert.Equal(expectedCount, countParam.Item2);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenProcessingJobLastOneFromGroupAndProfileResourceTypeIsUpdatedInPreviouslyCompleteOrFailedJob_WhenJobIsRun_ThenProfilesAreRefreshed(bool isFailed)
        {
            _updater.ClearReceivedCalls();

            var definition = new BulkUpdateDefinition(JobType.BulkUpdateProcessing, null, null, "test", "test", "test", null, isParallel: true, readNextPage: false, startSurrogateId: "startSurrogateId", endSurrogateId: "endSurrogateId", globalStartSurrogateId: "globalStartSurrogateId", globalEndSurrogateId: "globalEndSurrogateId");
            var completedResult = new BulkUpdateResult();
            completedResult.ResourcesUpdated["StructureDefinition"] = 5;
            if (isFailed)
            {
                completedResult.ResourcesPatchFailed["StructureDefinition"] = 6;
            }

            var jobs = new List<JobInfo>
            {
                new JobInfo
                {
                    Id = 2,
                    Status = isFailed ? JobStatus.Failed : JobStatus.Completed,
                    GroupId = 123,
                    Definition = "{...}", // Optionally, a serialized BulkUpdateDefinition
                    Result = JsonConvert.SerializeObject(completedResult),
                },
                new JobInfo
                {
                    Id = 1,
                    GroupId = 123,
                    Definition = JsonConvert.SerializeObject(definition),
                },
            };
            _queueClient.GetJobByGroupIdAsync(Arg.Any<byte>(), 123, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(jobs);
            _supportedProfilesStore.GetProfilesTypes().Returns(new HashSet<string> { "ValueSet", "StructureDefinition", "CodeSystem" });

            var substituteResults = new BulkUpdateResult();
            substituteResults.ResourcesUpdated["Patient"] = 3;
            _updater.UpdateMultipleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), 0, Arg.Any<bool>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<BundleResourceContext>(), Arg.Any<CancellationToken>())
                .Returns(args => substituteResults);

            var result = JsonConvert.DeserializeObject<BulkUpdateResult>(await _processingJob.ExecuteAsync(jobs[1], CancellationToken.None));
            Assert.Single(result.ResourcesUpdated);
            Assert.Equal(3, result.ResourcesUpdated["Patient"]);

            await _updater.ReceivedWithAnyArgs(1).UpdateMultipleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), 0, Arg.Any<bool>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<BundleResourceContext>(), Arg.Any<CancellationToken>());
            await _mediator.Received(1).PublishAsync(Arg.Is<BulkUpdateMetricsNotification>(n => n.JobId == jobs[1].Id && n.ResourcesUpdated == 3), Arg.Any<CancellationToken>());
            _supportedProfilesStore.Received(1).Refresh();
        }

        [Fact]
        public async Task GivenProcessingJobLastOneFromGroupAndProfileResourceTypeIsUpdatedInCurrentlyRunningJob_WhenJobIsRun_ThenProfilesAreRefreshed()
        {
            _updater.ClearReceivedCalls();

            var definition = new BulkUpdateDefinition(JobType.BulkUpdateProcessing, null, null, "test", "test", "test", null, isParallel: true, readNextPage: false, startSurrogateId: "startSurrogateId", endSurrogateId: "endSurrogateId", globalStartSurrogateId: "globalStartSurrogateId", globalEndSurrogateId: "globalEndSurrogateId");
            var completedResult = new BulkUpdateResult();
            completedResult.ResourcesUpdated["Patient"] = 5;
            var jobs = new List<JobInfo>
            {
                new JobInfo
                {
                    Id = 2,
                    Status = JobStatus.Completed,
                    GroupId = 123,
                    Definition = "{...}", // Optionally, a serialized BulkUpdateDefinition
                    Result = JsonConvert.SerializeObject(completedResult),
                },
                new JobInfo
                {
                    Id = 1,
                    GroupId = 123,
                    Definition = JsonConvert.SerializeObject(definition),
                },
            };
            _queueClient.GetJobByGroupIdAsync(Arg.Any<byte>(), 123, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(jobs);
            _supportedProfilesStore.GetProfilesTypes().Returns(new HashSet<string> { "ValueSet", "StructureDefinition", "CodeSystem" });

            var substituteResults = new BulkUpdateResult();
            substituteResults.ResourcesUpdated["StructureDefinition"] = 3;
            _updater.UpdateMultipleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), 0, Arg.Any<bool>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<BundleResourceContext>(), Arg.Any<CancellationToken>())
                .Returns(args => substituteResults);

            var result = JsonConvert.DeserializeObject<BulkUpdateResult>(await _processingJob.ExecuteAsync(jobs[1], CancellationToken.None));
            Assert.Single(result.ResourcesUpdated);
            Assert.Equal(3, result.ResourcesUpdated["StructureDefinition"]);

            await _updater.ReceivedWithAnyArgs(1).UpdateMultipleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), 0, Arg.Any<bool>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<BundleResourceContext>(), Arg.Any<CancellationToken>());
            await _mediator.Received(1).PublishAsync(Arg.Is<BulkUpdateMetricsNotification>(n => n.JobId == jobs[1].Id && n.ResourcesUpdated == 3), Arg.Any<CancellationToken>());
            _supportedProfilesStore.Received(1).Refresh();
        }

        [Fact]
        public async Task GivenProcessingJobLastOneFromGroupAndProfileResourceTypeIsNotUpdated_WhenJobIsRun_ThenProfilesAreNotRefreshed()
        {
            _updater.ClearReceivedCalls();

            var definition = new BulkUpdateDefinition(JobType.BulkUpdateProcessing, null, null, "test", "test", "test", null, isParallel: true, readNextPage: false, startSurrogateId: "startSurrogateId", endSurrogateId: "endSurrogateId", globalStartSurrogateId: "globalStartSurrogateId", globalEndSurrogateId: "globalEndSurrogateId");
            var completedResult = new BulkUpdateResult();
            completedResult.ResourcesUpdated["Observation"] = 5;
            completedResult.ResourcesIgnored["StructureDefinition"] = 4; // Ignored resource not the updated once
            var jobs = new List<JobInfo>
            {
                new JobInfo
                {
                    Id = 2,
                    Status = JobStatus.Completed,
                    GroupId = 123,
                    Definition = "{...}", // Optionally, a serialized BulkUpdateDefinition
                    Result = JsonConvert.SerializeObject(completedResult),
                },
                new JobInfo
                {
                    Id = 1,
                    GroupId = 123,
                    Definition = JsonConvert.SerializeObject(definition),
                },
            };
            _queueClient.GetJobByGroupIdAsync(Arg.Any<byte>(), 123, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(jobs);
            _supportedProfilesStore.GetProfilesTypes().Returns(new HashSet<string> { "ValueSet", "StructureDefinition", "CodeSystem" });

            var substituteResults = new BulkUpdateResult();
            substituteResults.ResourcesUpdated["Patient"] = 3;
            _updater.UpdateMultipleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), 0, Arg.Any<bool>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<BundleResourceContext>(), Arg.Any<CancellationToken>())
                .Returns(args => substituteResults);

            var result = JsonConvert.DeserializeObject<BulkUpdateResult>(await _processingJob.ExecuteAsync(jobs[1], CancellationToken.None));
            Assert.Single(result.ResourcesUpdated);
            Assert.Equal(3, result.ResourcesUpdated["Patient"]);

            await _updater.ReceivedWithAnyArgs(1).UpdateMultipleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), 0, Arg.Any<bool>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<BundleResourceContext>(), Arg.Any<CancellationToken>());
            await _mediator.Received(1).PublishAsync(Arg.Is<BulkUpdateMetricsNotification>(n => n.JobId == jobs[1].Id && n.ResourcesUpdated == 3), Arg.Any<CancellationToken>());
            _supportedProfilesStore.DidNotReceiveWithAnyArgs().Refresh();
        }

        [Fact]
        public async Task GivenProcessingJobNotTheLastOneFromGroupAndProfileResourceTypeIsUpdated_WhenJobIsRun_ThenProfilesAreNotRefreshed()
        {
            _updater.ClearReceivedCalls();

            var definition = new BulkUpdateDefinition(JobType.BulkUpdateProcessing, null, null, "test", "test", "test", null, isParallel: true, readNextPage: false, startSurrogateId: "startSurrogateId", endSurrogateId: "endSurrogateId", globalStartSurrogateId: "globalStartSurrogateId", globalEndSurrogateId: "globalEndSurrogateId");
            var jobs = new List<JobInfo>
            {
                new JobInfo
                {
                    Id = 2,
                    Status = JobStatus.Created,
                    GroupId = 123,
                    Definition = "{...}", // Optionally, a serialized BulkUpdateDefinition
                },
                new JobInfo
                {
                    Id = 1,
                    GroupId = 123,
                    Definition = JsonConvert.SerializeObject(definition),
                },
            };
            _queueClient.GetJobByGroupIdAsync(Arg.Any<byte>(), 123, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(jobs);
            _supportedProfilesStore.GetProfilesTypes().Returns(new HashSet<string> { "ValueSet", "StructureDefinition", "CodeSystem" });

            var substituteResults = new BulkUpdateResult();
            substituteResults.ResourcesUpdated["StructureDefinition"] = 3;

            _updater.UpdateMultipleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), 0, Arg.Any<bool>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<BundleResourceContext>(), Arg.Any<CancellationToken>())
                .Returns(args => substituteResults);

            var result = JsonConvert.DeserializeObject<BulkUpdateResult>(await _processingJob.ExecuteAsync(jobs[1], CancellationToken.None));
            Assert.Single(result.ResourcesUpdated);
            Assert.Equal(3, result.ResourcesUpdated["StructureDefinition"]);

            await _updater.ReceivedWithAnyArgs(1).UpdateMultipleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), 0, Arg.Any<bool>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<BundleResourceContext>(), Arg.Any<CancellationToken>());
            await _mediator.Received(1).PublishAsync(Arg.Is<BulkUpdateMetricsNotification>(n => n.JobId == jobs[1].Id && n.ResourcesUpdated == 3), Arg.Any<CancellationToken>());
            _supportedProfilesStore.DidNotReceiveWithAnyArgs().Refresh();
        }

        [Fact]
        public async Task GivenProcessingJobWhenIncompleteOperationExceptionIsThrown_WhenJobIsRun_ThenJobExecutionExceptionIsThrownAndPartialResultsAreHandled()
        {
            var definition = new BulkUpdateDefinition(JobType.BulkUpdateProcessing, null, null, "test", "test", "test", null, isParallel: true, readNextPage: true);
            var jobInfo = new JobInfo
            {
                Id = 1,
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
                CreateDate = DateTime.UtcNow,
            };

            var partialResult = new BulkUpdateResult();
            partialResult.ResourcesUpdated["Patient"] = 2;
            partialResult.ResourcesPatchFailed["Patient"] = 1;
            var exceptionMessage = "Partial failure occurred";
            var incompleteException = new IncompleteOperationException<BulkUpdateResult>(new Exception(exceptionMessage), partialResult);

            _updater.UpdateMultipleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), 0, Arg.Any<bool>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<BundleResourceContext>(), Arg.Any<CancellationToken>()).Returns<Task<BulkUpdateResult>>(x => { throw incompleteException; });

            var ex = await Assert.ThrowsAsync<JobExecutionException>(() => _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
            Assert.Contains(exceptionMessage, ex.Message);
            Assert.NotNull(ex.Error);
            var errorResult = Assert.IsType<BulkUpdateResult>(ex.Error);
            Assert.Equal(2, errorResult.ResourcesUpdated["Patient"]);
            Assert.Contains(exceptionMessage, errorResult.Issues);
            Assert.Equal(1, errorResult.ResourcesPatchFailed["Patient"]);

            await _mediator.Received(1).PublishAsync(Arg.Is<BulkUpdateMetricsNotification>(n => n.JobId == jobInfo.Id && n.ResourcesUpdated == 2), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenProcessingJobWhenResourcesPatchFailedIsNotEmpty_WhenJobIsRun_ThrowsJobExecutionSoftFailureException()
        {
            var definition = new BulkUpdateDefinition(JobType.BulkUpdateProcessing, null, null, "test", "test", "test", null, isParallel: true, readNextPage: true);
            var jobInfo = new JobInfo
            {
                Id = 1,
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
                CreateDate = DateTime.UtcNow,
            };

            var result = new BulkUpdateResult();
            result.ResourcesUpdated["Patient"] = 2;
            result.ResourcesIgnored["StructureDefinition"] = 2;
            result.ResourcesPatchFailed["Patient"] = 1;

            _updater.UpdateMultipleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), 0, Arg.Any<bool>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<BundleResourceContext>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(result));

            // Act & Assert
            var ex = await Assert.ThrowsAsync<JobExecutionSoftFailureException>(() => _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
            Assert.Contains("Exception encounted while updating resources", ex.Message);
            Assert.NotNull(ex.Error);
            var errorResult = Assert.IsType<BulkUpdateResult>(ex.Error);
            Assert.Equal(2, errorResult.ResourcesUpdated["Patient"]);
            Assert.Equal(2, errorResult.ResourcesIgnored["StructureDefinition"]);
            Assert.Equal(1, errorResult.ResourcesPatchFailed["Patient"]);

            await _mediator.Received(1).PublishAsync(Arg.Is<BulkUpdateMetricsNotification>(n => n.JobId == jobInfo.Id && n.ResourcesUpdated == 2), Arg.Any<CancellationToken>());
            _supportedProfilesStore.DidNotReceiveWithAnyArgs().Refresh();
        }

        [Fact]
        public async Task GivenProcessingJobWhenNoResourcesUpdatedAndNoErrors_WhenJobIsRun_ThenReturnsEmptyResult()
        {
            // Arrange
            var definition = new BulkUpdateDefinition(JobType.BulkUpdateProcessing, null, null, "test", "test", "test", null, isParallel: true, readNextPage: true);
            var jobInfo = new JobInfo
            {
                Id = 1,
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
                CreateDate = DateTime.UtcNow,
            };

            var emptyResult = new BulkUpdateResult();
            _updater.UpdateMultipleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), 0, Arg.Any<bool>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<BundleResourceContext>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(emptyResult));

            // Act
            var result = JsonConvert.DeserializeObject<BulkUpdateResult>(await _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));

            // Assert
            Assert.Empty(result.ResourcesUpdated);
            Assert.Empty(result.ResourcesIgnored);
            Assert.Empty(result.ResourcesPatchFailed);
            Assert.Empty(result.Issues);

            await _mediator.DidNotReceiveWithAnyArgs().PublishAsync(Arg.Any<BulkUpdateMetricsNotification>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenProcessingJobWhenUnexpectedExceptionIsThrownFromUpdateMultipleAsync_WhenJobIsRun_ThenExceptionIsPropagated()
        {
            // Arrange
            var definition = new BulkUpdateDefinition(JobType.BulkUpdateProcessing, null, null, "test", "test", "test", null, isParallel: true, readNextPage: true);
            var jobInfo = new JobInfo
            {
                Id = 1,
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
                CreateDate = DateTime.UtcNow,
            };

            var unexpectedException = new InvalidOperationException("Unexpected error");
            _updater.UpdateMultipleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), 0, Arg.Any<bool>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<BundleResourceContext>(), Arg.Any<CancellationToken>()).Returns<Task<BulkUpdateResult>>(x => throw unexpectedException);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
            Assert.Equal("Unexpected error", ex.Message);
            await _mediator.DidNotReceiveWithAnyArgs().PublishAsync(Arg.Any<BulkUpdateMetricsNotification>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenProcessingJob_WhenJobInfoIsNull_ThenArgumentNullExceptionIsThrown()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await _processingJob.ExecuteAsync(null, CancellationToken.None));
        }

        [Fact]
        public async Task GivenProcessingJob_WhenJobDefinitionIsMalformed_ThenJsonExceptionIsThrown()
        {
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = "not a valid json",
            };

            await Assert.ThrowsAsync<JsonReaderException>(async () => await _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
        }

        [Fact]
        public async Task GivenProcessingJob_WhenQueueClientThrowsException_ThenExceptionIsPropagated()
        {
            var definition = new BulkUpdateDefinition(JobType.BulkUpdateProcessing, null, null, "test", "test", "test", null, isParallel: true, readNextPage: true);
            var jobInfo = new JobInfo
            {
                Id = 1,
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
                CreateDate = DateTime.UtcNow,
            };

            _queueClient.GetJobByGroupIdAsync(Arg.Any<byte>(), Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Throws(new InvalidOperationException("Queue error"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
        }

        [Fact]
        public async Task GivenProcessingJob_WhenSupportedProfilesStoreThrowsException_ThenExceptionIsPropagated()
        {
            var definition = new BulkUpdateDefinition(JobType.BulkUpdateProcessing, null, null, "test", "test", "test", null, isParallel: true, readNextPage: true);
            var jobInfo = new JobInfo
            {
                Id = 1,
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
                CreateDate = DateTime.UtcNow,
            };

            var jobs = new List<JobInfo>
            {
                new JobInfo
                {
                    Id = 2,
                    Status = JobStatus.Completed,
                    GroupId = 1,
                    Definition = JsonConvert.SerializeObject(definition),
                    Result = JsonConvert.SerializeObject(new BulkUpdateResult()),
                },
                jobInfo,
            };
            _queueClient.GetJobByGroupIdAsync(Arg.Any<byte>(), 1, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(jobs);
            _supportedProfilesStore.GetProfilesTypes().Throws(new InvalidOperationException("Profiles error"));

            var substituteResults = new BulkUpdateResult();
            substituteResults.ResourcesUpdated["Patient"] = 3;
            _updater.UpdateMultipleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), 0, Arg.Any<bool>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<BundleResourceContext>(), Arg.Any<CancellationToken>())
                .Returns(args => substituteResults);

            await Assert.ThrowsAsync<InvalidOperationException>(() => _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
        }

        [Fact]
        public async Task GivenProcessingJob_WhenMediatorThrowsException_ThenExceptionIsPropagated()
        {
            var definition = new BulkUpdateDefinition(JobType.BulkUpdateProcessing, null, null, "test", "test", "test", null, isParallel: true, readNextPage: true);
            var jobInfo = new JobInfo
            {
                Id = 1,
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
                CreateDate = DateTime.UtcNow,
            };

            var substituteResults = new BulkUpdateResult();
            substituteResults.ResourcesUpdated["Patient"] = 3;
            _updater.UpdateMultipleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), 0, Arg.Any<bool>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<BundleResourceContext>(), Arg.Any<CancellationToken>())
                .Returns(args => substituteResults);

            _mediator.PublishAsync(Arg.Any<BulkUpdateMetricsNotification>(), Arg.Any<CancellationToken>())
                .Throws(new InvalidOperationException("Mediator error"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
        }
    }
}
