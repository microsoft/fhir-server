// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.CosmosDb.Features.Operations.Export;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Operations.Export
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    public class ExportOrchestratorJobTests
    {
        private ISearchService _mockSearchService = Substitute.For<ISearchService>();
        private IQueueClient _mockQueueClient = Substitute.For<IQueueClient>();
        private const int _numberOfTestResourceTypes = 3;
        private List<JobInfo> _enqueuedJobs = new();
        private long _orchestratorJobId = 10000;

        [Theory]
        [InlineData(ExportJobType.Patient)]
        [InlineData(ExportJobType.Group)]
        public async Task GivenANonSystemLevelExportJob_WhenRun_ThenOneProcessingJobShouldBeCreated(ExportJobType exportJobType)
        {
            int numExpectedJobs = 1;
            int numExpectedEnqueueCalls = 1;

            var initialJobList = CreateOrchestratorJobList(_orchestratorJobId, isParallel: true, exportJobType: exportJobType).ToList();
            SetupMockQueue(_orchestratorJobId, initialJobList);
            var orchestratorJobInfo = initialJobList.First();

            var exportOrchestratorJob = new CosmosExportOrchestratorJob(_mockQueueClient, _mockSearchService);
            var result = await exportOrchestratorJob.ExecuteAsync(orchestratorJobInfo, new Progress<string>((result) => { }), CancellationToken.None);
            var jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);

            CheckJobsQueued(numExpectedEnqueueCalls, numExpectedJobs);
        }

        [Fact]
        public async Task GivenAnExportJobWithIsParallelSetToFalse_WhenRun_ThenOneProcessingJobShouldBeCreated()
        {
            int numExpectedJobs = 1;
            int numExpectedEnqueueCalls = 1;

            var initialJobList = CreateOrchestratorJobList(_orchestratorJobId, isParallel: false);
            SetupMockQueue(_orchestratorJobId, initialJobList.ToList());
            var orchestratorJobInfo = initialJobList.First();

            var exportOrchestratorJob = new CosmosExportOrchestratorJob(_mockQueueClient, _mockSearchService);
            var result = await exportOrchestratorJob.ExecuteAsync(orchestratorJobInfo, new Progress<string>((result) => { }), CancellationToken.None);
            var jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);
            Assert.Equal(OperationStatus.Completed, jobResult.Status);

            CheckJobsQueued(numExpectedEnqueueCalls, numExpectedJobs);
        }

        [Fact]
        public async Task GivenAnExportJobWithParallelParameters_WhenRun_ThenMultipleProcessingJobShouldBeCreated()
        {
            int numExpectedJobsPerResourceType = 3;
            int numExpectedJobs = numExpectedJobsPerResourceType * _numberOfTestResourceTypes;
            int numExpectedEnqueueCalls = numExpectedJobsPerResourceType * _numberOfTestResourceTypes;

            var initialJobList = CreateOrchestratorJobList(_orchestratorJobId, isParallel: true);
            SetupMockQueue(_orchestratorJobId, initialJobList.ToList());
            var orchestratorJobInfo = initialJobList.First();

            var exportOrchestratorJob = new CosmosExportOrchestratorJob(_mockQueueClient, _mockSearchService);
            var result = await exportOrchestratorJob.ExecuteAsync(orchestratorJobInfo, new Progress<string>((result) => { }), CancellationToken.None);
            var jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);
            Assert.Equal(OperationStatus.Completed, jobResult.Status);

            CheckJobsQueued(numExpectedEnqueueCalls, numExpectedJobs);
        }

        [Fact]
        public async Task GivenAnExportJobWithTypeRestrictions_WhenRun_ThenProcessingJobShouldBeCreatedPerResourceType()
        {
            int numExpectedJobsPerResourceType = 3;
            int numExpectedResourceTypes = 2;
            int numExpectedJobs = numExpectedJobsPerResourceType * numExpectedResourceTypes;
            int numExpectedEnqueueCalls = numExpectedJobsPerResourceType * numExpectedResourceTypes;

            var initialJobList = CreateOrchestratorJobList(_orchestratorJobId, isParallel: true, typeFilter: "Patient,Observation");
            SetupMockQueue(_orchestratorJobId, initialJobList.ToList());
            var orchestratorJobInfo = initialJobList.First();

            var exportOrchestratorJob = new CosmosExportOrchestratorJob(_mockQueueClient, _mockSearchService);
            var result = await exportOrchestratorJob.ExecuteAsync(orchestratorJobInfo, new Progress<string>((result) => { }), CancellationToken.None);
            var jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);
            Assert.Equal(OperationStatus.Completed, jobResult.Status);

            CheckJobsQueued(numExpectedEnqueueCalls, numExpectedJobs);
        }

        [Fact]
        public async Task GivenAnExportJob_WhenRunMultipleTimes_ThenMultipleJobsNotCreatedPerRun()
        {
            int numExpectedJobsPerResourceType = 3;
            int numExpectedJobs = numExpectedJobsPerResourceType * _numberOfTestResourceTypes;
            int numExpectedEnqueueCalls = numExpectedJobsPerResourceType * _numberOfTestResourceTypes;

            var initialJobList = CreateOrchestratorJobList(_orchestratorJobId, isParallel: true);
            SetupMockQueue(_orchestratorJobId, initialJobList);
            var orchestratorJobInfo = initialJobList.First();

            var exportOrchestratorJob = new CosmosExportOrchestratorJob(_mockQueueClient, _mockSearchService);
            var result = await exportOrchestratorJob.ExecuteAsync(orchestratorJobInfo, new Progress<string>((result) => { }), CancellationToken.None);
            var jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);
            Assert.Equal(OperationStatus.Completed, jobResult.Status);

            // Run the same job again - it should skip adding these to the queue.
            await exportOrchestratorJob.ExecuteAsync(orchestratorJobInfo, new Progress<string>((result) => { }), CancellationToken.None);

            CheckJobsQueued(numExpectedEnqueueCalls, numExpectedJobs);
        }

        [Fact]
        public async Task GivenAnExportJobWithParallelParameters_WhenStoppedInMiddleAndRestarted_ThenCorrectNumberOfJobsCreated()
        {
            int numExpectedJobsPerResourceType = 3;
            int numJobsBeforeStop = 5;
            int numExpectedJobs = numExpectedJobsPerResourceType * _numberOfTestResourceTypes;
            int numExpectedEnqueueCalls = numExpectedJobsPerResourceType * _numberOfTestResourceTypes;

            var initialJobList = CreateOrchestratorJobList(_orchestratorJobId, isParallel: true);
            SetupMockQueue(_orchestratorJobId, initialJobList.ToList());
            var orchestratorJobInfo = initialJobList.First();

            // Queue the job initially to get partial job list.
            var exportOrchestratorJob = new CosmosExportOrchestratorJob(_mockQueueClient, _mockSearchService);
            var result = await exportOrchestratorJob.ExecuteAsync(orchestratorJobInfo, new Progress<string>((result) => { }), CancellationToken.None);
            var jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);
            var jobsInGroup = await _mockQueueClient.GetJobByGroupIdAsync(0, _orchestratorJobId, true, CancellationToken.None);

            // Pull first 5 jobs from the queue and inject them into a new mock queue to simluate a stopped job.
            initialJobList = CreateOrchestratorJobList(_orchestratorJobId, isParallel: true);
            initialJobList.AddRange(jobsInGroup.Where(x => x.Id != _orchestratorJobId).Take(numJobsBeforeStop));
            _mockQueueClient.ClearReceivedCalls();
            SetupMockQueue(_orchestratorJobId, initialJobList);

            // Run the job again - it should skip adding existing jobs but add non-existing jobs.
            result = await exportOrchestratorJob.ExecuteAsync(orchestratorJobInfo, new Progress<string>((result) => { }), CancellationToken.None);
            jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);
            Assert.Equal(OperationStatus.Completed, jobResult.Status);

            CheckJobsQueued(numExpectedEnqueueCalls - numJobsBeforeStop, numExpectedJobs);
        }

        private List<JobInfo> CreateOrchestratorJobList(
            long orchestratorJobId,
            bool isParallel = false,
            string typeFilter = null,
            ExportJobType exportJobType = ExportJobType.All)
        {
            var jobInfoArray = new List<JobInfo>();

            var orchestratorRecord = new ExportJobRecord(
                            requestUri: new Uri("https://localhost/ExportJob/"),
                            exportType: exportJobType,
                            exportFormat: ExportFormatTags.ResourceName,
                            resourceType: typeFilter,
                            filters: null,
                            hash: "hash",
                            rollingFileSizeInMB: 0,
                            groupId: $"{orchestratorJobId}",
                            isParallel: isParallel,
                            since: new PartialDateTime(new DateTimeOffset(2020, 1, 1, 1, 1, 1, TimeSpan.Zero)),
                            till: new PartialDateTime(new DateTimeOffset(2021, 1, 1, 1, 1, 1, TimeSpan.Zero)))
                            {
                                Id = $"{orchestratorJobId}",
                            };

            var orchestratorJob = new JobInfo()
            {
                Id = orchestratorJobId,
                GroupId = orchestratorJobId,
                Status = JobStatus.Running,
                Definition = JsonConvert.SerializeObject(orchestratorRecord),
            };

            jobInfoArray.Add(orchestratorJob);

            return jobInfoArray;
        }

        private List<JobInfo> CreateWorkerJobList(long orchestratorJobId, string[] definitions)
        {
            var jobInfoArray = new List<JobInfo>();

            if (definitions is not null)
            {
                for (int i = 0; i < definitions.Length; i++)
                {
                    var processingJob = new JobInfo()
                    {
                        Id = i,
                        GroupId = orchestratorJobId,
                        Status = JobStatus.Running,
                        Definition = definitions[i],
                        Result = definitions[i],
                    };

                    jobInfoArray.Add(processingJob);
                }
            }

            return jobInfoArray;
        }

        private void SetupMockQueue(long orchestratorJobId, List<JobInfo> defaultJobs = null)
        {
            if (defaultJobs is not null)
            {
                _enqueuedJobs = defaultJobs;
            }

            _mockSearchService.GetUsedResourceTypes(Arg.Any<CancellationToken>()).Returns(new List<string>() { "Patient", "Observation", "Encounter" });
            _mockSearchService.GetFeedRanges(Arg.Any<CancellationToken>()).Returns(new List<string>() { "Range1", "Range2", "Range3" });

            _mockQueueClient.EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), orchestratorJobId, false, false, Arg.Any<CancellationToken>()).Returns(x =>
            {
                string[] definitions = x.ArgAt<string[]>(1);
                Assert.Single(definitions); // CosmosDB export jobs always have a single definition.

                var jobInfoArray = CreateWorkerJobList(orchestratorJobId, definitions);
                _enqueuedJobs.AddRange(jobInfoArray);

                return jobInfoArray;
            });

            _mockQueueClient.GetJobByGroupIdAsync(Arg.Any<byte>(), orchestratorJobId, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(x =>
            {
                return _enqueuedJobs.Where(x => x.GroupId == orchestratorJobId).ToList();
            });
        }

        private void CheckJobsQueued(int expectedEnqueueCalls, int expectedQueuedJobs)
        {
            var calls = _mockQueueClient.ReceivedCalls().Where(call =>
            {
                return call.GetMethodInfo().Name.Equals("EnqueueAsync");
            });

            Assert.Equal(expectedEnqueueCalls, calls.Count());

            var numJobsMade = _enqueuedJobs.Where(x => x.Id != _orchestratorJobId).Count();

            Assert.Equal(expectedQueuedJobs, numJobsMade);
        }
    }
}
