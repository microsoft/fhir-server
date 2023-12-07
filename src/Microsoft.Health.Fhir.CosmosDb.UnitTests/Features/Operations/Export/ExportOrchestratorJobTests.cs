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

        [Theory]
        [InlineData(ExportJobType.Patient)]
        [InlineData(ExportJobType.Group)]
        public async Task GivenANonSystemLevelExportJob_WhenRun_ThenOneProcessingJobShouldBeCreated(ExportJobType exportJobType)
        {
            // Non-system level exports use single job framework.
            int numExpectedJobs = 1;
            long orchestratorJobId = 10000;

            var initialJobList = CreateOrchestratorJobList(orchestratorJobId, isParallel: true, exportJobType: exportJobType).ToList();
            SetupMockQueue(numExpectedJobs, orchestratorJobId, initialJobList);
            var orchestratorJob = initialJobList.First();

            var exportOrchestratorJob = new CosmosExportOrchestratorJob(_mockQueueClient, _mockSearchService);
            var result = await exportOrchestratorJob.ExecuteAsync(orchestratorJob, new Progress<string>((result) => { }), CancellationToken.None);
            var jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);

            CheckJobsQueued(1, numExpectedJobs);
        }

        [Fact]
        public async Task GivenAnExportJobWithIsParallelSetToFalse_WhenRun_ThenOneProcessingJobShouldBeCreated()
        {
            // Non-parallel exports use single job framework.
            int numExpectedJobs = 1;
            long orchestratorJobId = 10000;

            var initialJobList = CreateOrchestratorJobList(orchestratorJobId, isParallel: false);
            SetupMockQueue(numExpectedJobs, orchestratorJobId, initialJobList.ToList());
            var orchestratorJob = initialJobList.First();

            var exportOrchestratorJob = new CosmosExportOrchestratorJob(_mockQueueClient, _mockSearchService);
            var result = await exportOrchestratorJob.ExecuteAsync(orchestratorJob, new Progress<string>((result) => { }), CancellationToken.None);
            var jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);
            Assert.Equal(OperationStatus.Completed, jobResult.Status);

            CheckJobsQueued(1, numExpectedJobs);
        }

        [Fact]
        public async Task GivenAnExportJobWithNoTypeRestriction_WhenRun_ThenOneProcessingJobShouldBeCreated()
        {
            // We have three feed ranges mocked to return below.
            int numExpectedJobsPerResourceType = 3;
            int numExpectedResourceTypes = 3;
            long orchestratorJobId = 10000;

            var initialJobList = CreateOrchestratorJobList(orchestratorJobId, isParallel: true);
            SetupMockQueue(numExpectedJobsPerResourceType, orchestratorJobId, initialJobList.ToList());
            var orchestratorJob = initialJobList.First();

            var exportOrchestratorJob = new CosmosExportOrchestratorJob(_mockQueueClient, _mockSearchService);
            var result = await exportOrchestratorJob.ExecuteAsync(orchestratorJob, new Progress<string>((result) => { }), CancellationToken.None);
            var jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);
            Assert.Equal(OperationStatus.Completed, jobResult.Status);

            CheckJobsQueued(numExpectedResourceTypes, numExpectedJobsPerResourceType * numExpectedResourceTypes);
        }

        [Fact]
        public async Task GivenAnExportJobWithTypeRestrictions_WhenRun_ThenProcessingJobShouldBeCreatedPerResourceType()
        {
            // We have three feed ranges mocked to return below.
            int numExpectedResourceTypes = 2;
            int numExpectedJobsPerResourceType = 3;
            long orchestratorJobId = 10000;

            var initialJobList = CreateOrchestratorJobList(orchestratorJobId, isParallel: true, typeFilter: "Patient,Observation");
            SetupMockQueue(numExpectedJobsPerResourceType, orchestratorJobId, initialJobList.ToList());
            var orchestratorJob = initialJobList.First();

            var exportOrchestratorJob = new CosmosExportOrchestratorJob(_mockQueueClient, _mockSearchService);
            var result = await exportOrchestratorJob.ExecuteAsync(orchestratorJob, new Progress<string>((result) => { }), CancellationToken.None);
            var jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);
            Assert.Equal(OperationStatus.Completed, jobResult.Status);

            CheckJobsQueued(numExpectedResourceTypes, numExpectedJobsPerResourceType * numExpectedResourceTypes);
        }

        [Fact]
        public async Task GivenAnExportJobWithMoreFeedRangesThanMax_WhenRun_ThenMultipleJobsCreatedPerResourceType()
        {
            // We have three feed ranges mocked to return below.
            int numExpectedJobsPerResourceType = 3;
            int numExpectedResourceTypes = 3;
            int maxNumberOfDefinitionsPerJob = 1;
            long orchestratorJobId = 10000;

            var initialJobList = CreateOrchestratorJobList(orchestratorJobId, isParallel: true);
            SetupMockQueue(maxNumberOfDefinitionsPerJob, orchestratorJobId, initialJobList.ToList());
            var orchestratorJob = initialJobList.First();

            var exportOrchestratorJob = new CosmosExportOrchestratorJob(_mockQueueClient, _mockSearchService);
            var result = await exportOrchestratorJob.ExecuteAsync(orchestratorJob, new Progress<string>((result) => { }), CancellationToken.None);
            var jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);
            Assert.Equal(OperationStatus.Completed, jobResult.Status);

            // Since there is only one definition per job type, we expect 9 jobs queued
            CheckJobsQueued(numExpectedJobsPerResourceType * numExpectedResourceTypes, numExpectedJobsPerResourceType * numExpectedResourceTypes);
        }

        [Fact]
        public async Task GivenAnExportJob_WhenRunMultipleTimes_ThenMultipleJobsNotCreatedPerRun()
        {
            // We have three feed ranges mocked to return below.
            int numExpectedJobsPerResourceType = 3;
            int numExpectedResourceTypes = 3;
            long orchestratorJobId = 10000;

            var initialJobList = CreateOrchestratorJobList(orchestratorJobId, isParallel: true);
            SetupMockQueue(numExpectedJobsPerResourceType, orchestratorJobId, initialJobList.ToList());
            var orchestratorJob = initialJobList.First();

            var exportOrchestratorJob = new CosmosExportOrchestratorJob(_mockQueueClient, _mockSearchService);
            var result = await exportOrchestratorJob.ExecuteAsync(orchestratorJob, new Progress<string>((result) => { }), CancellationToken.None);
            var jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);
            Assert.Equal(OperationStatus.Completed, jobResult.Status);

            // Run the same job again - it should skip adding these to the queue.
            await exportOrchestratorJob.ExecuteAsync(orchestratorJob, new Progress<string>((result) => { }), CancellationToken.None);

            CheckJobsQueued(numExpectedResourceTypes, numExpectedJobsPerResourceType * numExpectedResourceTypes);
        }

        private IReadOnlyList<JobInfo> CreateOrchestratorJobList(
            long orchestratorJobId,
            bool isParallel = false,
            string typeFilter = null,
            ExportJobType exportJobType = ExportJobType.All)
        {
            var jobInfoArray = new List<JobInfo>();

            var orchestratorRecord = new ExportJobRecord(
                            new Uri("https://localhost/ExportJob/"),
                            exportJobType,
                            ExportFormatTags.ResourceName,
                            typeFilter,
                            null,
                            "hash",
                            0,
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

        private IReadOnlyList<JobInfo> CreateWorkerJobList(long orchestratorJobId, string[] definitions)
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

        private void SetupMockQueue(int numExpectedJobsPerResourceType, long orchestratorJobId, List<JobInfo> defaultJobs = null)
        {
            List<JobInfo> enqueuedJobs = defaultJobs ?? new List<JobInfo>();

            _mockSearchService.GetUsedResourceTypes(Arg.Any<CancellationToken>()).Returns(x =>
            {
                var list = new List<string>
                {
                    "Patient",
                    "Observation",
                    "Encounter",
                };
                return list;
            });

            _mockSearchService.GetFeedRanges(Arg.Any<CancellationToken>()).Returns(x =>
            {
                var list = new List<string>
                {
                    "Range1",
                    "Range2",
                    "Range3",
                };
                return list;
            });

            _mockQueueClient.EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), orchestratorJobId, false, false, Arg.Any<CancellationToken>()).Returns(x =>
            {
                string[] definitions = x.ArgAt<string[]>(1);
                Assert.Equal(numExpectedJobsPerResourceType, definitions.Length);
                var jobInfoArray = CreateWorkerJobList(orchestratorJobId, definitions);
                enqueuedJobs.AddRange(jobInfoArray);

                return jobInfoArray;
            });

            _mockQueueClient.GetJobByGroupIdAsync(Arg.Any<byte>(), orchestratorJobId, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(x =>
            {
                return enqueuedJobs.Where(x => x.GroupId == orchestratorJobId).ToList();
            });
        }

        private void CheckJobsQueued(int expectedCalls, int expectedQueuedJobs)
        {
            var calls = _mockQueueClient.ReceivedCalls().Where(call =>
            {
                return call.GetMethodInfo().Name.Equals("EnqueueAsync");
            });

            Assert.Equal(expectedCalls, calls.Count());
            var numJobsMade = calls.Aggregate(0, (sum, call) =>
            {
                return sum + ((string[])call.GetOriginalArguments()[1]).Length;
            });

            Assert.Equal(expectedQueuedJobs, numJobsMade);
        }
    }
}
