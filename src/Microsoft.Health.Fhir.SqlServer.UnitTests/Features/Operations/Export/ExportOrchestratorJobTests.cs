// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Export;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Operations.Export
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    public class ExportOrchestratorJobTests
    {
        private ISearchService _mockSearchService = Substitute.For<ISearchService>();
        private IQueueClient _mockQueueClient = Substitute.For<IQueueClient>();
        private IOptions<ExportJobConfiguration> _exportJobConfiguration = Options.Create(new ExportJobConfiguration());

        [Theory]
        [InlineData(ExportJobType.Patient)]
        [InlineData(ExportJobType.Group)]
        public async Task GivenANonSystemLevelExportJob_WhenRun_ThenOneProcessingJobShouldBeCreated(ExportJobType exportJobType)
        {
            int numExpectedJobs = 1;
            long orchestratorJobId = 10000;

            SetupMockQueue(numExpectedJobs, orchestratorJobId);

            var orchestratorJob = GetJobInfoArray(0, orchestratorJobId, false, orchestratorJobId, isParallel: true, exportJobType: exportJobType)[0];
            var exportOrchestratorJob = new SqlExportOrchestratorJob(_mockQueueClient, _mockSearchService, _exportJobConfiguration);
            var result = await exportOrchestratorJob.ExecuteAsync(orchestratorJob, new Progress<string>((result) => { }), CancellationToken.None);
            var jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);

            CheckJobsQueued(1, numExpectedJobs);
        }

        [Fact]
        public async Task GivenAnExportJobWithIsParallelSetToFalse_WhenRun_ThenOneProcessingJobShouldBeCreated()
        {
            int numExpectedJobs = 1;
            long orchestratorJobId = 10000;

            SetupMockQueue(numExpectedJobs, orchestratorJobId);

            var orchestratorJob = GetJobInfoArray(0, orchestratorJobId, false, orchestratorJobId, isParallel: false).First();
            var exportOrchestratorJob = new SqlExportOrchestratorJob(_mockQueueClient, _mockSearchService, _exportJobConfiguration);
            var result = await exportOrchestratorJob.ExecuteAsync(orchestratorJob, new Progress<string>((result) => { }), CancellationToken.None);
            var jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);
            Assert.Equal(OperationStatus.Completed, jobResult.Status);

            CheckJobsQueued(1, numExpectedJobs);
        }

        [Fact]
        public async Task GivenAnExportJobWithNoTypeRestriction_WhenRun_ThenMultipleProcessingJobsShouldBeCreated()
        {
            int numExpectedJobsPerResourceType = 100;
            long orchestratorJobId = 10000;

            SetupMockQueue(numExpectedJobsPerResourceType, orchestratorJobId);

            var orchestratorJob = GetJobInfoArray(0, orchestratorJobId, false, orchestratorJobId, isParallel: true).First();
            var exportOrchestratorJob = new SqlExportOrchestratorJob(_mockQueueClient, _mockSearchService, _exportJobConfiguration);
            var result = await exportOrchestratorJob.ExecuteAsync(orchestratorJob, new Progress<string>((result) => { }), CancellationToken.None);
            var jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);
            Assert.Equal(OperationStatus.Completed, jobResult.Status);

            CheckJobsQueued(3, numExpectedJobsPerResourceType * 3);
        }

        [Fact]
        public async Task GivenAnExportJobWithTypeRestrictions_WhenRun_ThenProcessingJobsShouldBeCreatedPerResourceType()
        {
            int numExpectedJobs = 100;
            long orchestratorJobId = 10000;

            SetupMockQueue(numExpectedJobs, orchestratorJobId);

            JobInfo orchestratorJob = GetJobInfoArray(0, orchestratorJobId, false, orchestratorJobId, isParallel: true, typeFilter: "Patient,Observation").First();
            var exportOrchestratorJob = new SqlExportOrchestratorJob(_mockQueueClient, _mockSearchService, _exportJobConfiguration);
            string result = await exportOrchestratorJob.ExecuteAsync(orchestratorJob, new Progress<string>(_ => { }), CancellationToken.None);
            ExportJobRecord jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);
            Assert.Equal(OperationStatus.Completed, jobResult.Status);

            CheckJobsQueued(2, numExpectedJobs * 2);
        }

        private IReadOnlyList<JobInfo> GetJobInfoArray(
            int numJobInfos,
            long groupId,
            bool areAllCompleted,
            long orchestratorJobId = -1,
            bool isParallel = false,
            string typeFilter = null,
            ExportJobType exportJobType = ExportJobType.All,
            bool failure = false)
        {
            var jobInfoArray = new List<JobInfo>();

            if (orchestratorJobId != -1)
            {
                var orchestratorRecord = new ExportJobRecord(
                                new Uri("https://localhost/ExportJob/"),
                                exportJobType,
                                ExportFormatTags.ResourceName,
                                typeFilter,
                                null,
                                "hash",
                                0,
                                groupId: $"{groupId}",
                                isParallel: isParallel,
                                since: new PartialDateTime(new DateTimeOffset(2020, 1, 1, 1, 1, 1, TimeSpan.Zero)),
                                till: new PartialDateTime(new DateTimeOffset(2021, 1, 1, 1, 1, 1, TimeSpan.Zero)))
                {
                    Id = $"{orchestratorJobId}",
                };

                var orchestratorJob = new JobInfo()
                {
                    Id = orchestratorJobId,
                    GroupId = groupId,
                    Status = JobStatus.Running,
                    Definition = JsonConvert.SerializeObject(orchestratorRecord),
                };

                jobInfoArray.Add(orchestratorJob);
            }

            for (int i = 0; i < numJobInfos; i++)
            {
                var patientList = new List<ExportFileInfo>();
                patientList.Add(new ExportFileInfo("Patient", new Uri("https://test"), 0));

                var processingRecord = new ExportJobRecord(
                                new Uri("https://localhost/ExportJob/"),
                                ExportJobType.All,
                                ExportFormatTags.ResourceName,
                                null,
                                null,
                                "hash",
                                0,
                                groupId: $"{groupId}",
                                isParallel: isParallel,
                                typeId: (int)JobType.ExportProcessing)
                {
                    Id = $"{i}",
                    Status = areAllCompleted ? (failure ? OperationStatus.Failed : OperationStatus.Completed) : OperationStatus.Running,
                };

                processingRecord.Output.Add("Patient", patientList);

                if (failure)
                {
                    processingRecord.FailureDetails = new JobFailureDetails("Job failed.", System.Net.HttpStatusCode.UnavailableForLegalReasons);
                }

                var processingJob = new JobInfo()
                {
                    Id = i,
                    GroupId = groupId,
                    Status = areAllCompleted ? (failure ? JobStatus.Failed : JobStatus.Completed) : JobStatus.Running,
                    Definition = JsonConvert.SerializeObject(processingRecord),
                    Result = JsonConvert.SerializeObject(processingRecord),
                };

                jobInfoArray.Add(processingJob);
            }

            return jobInfoArray;
        }

        private void SetupMockQueue(int numExpectedJobsPerResourceType, long orchestratorJobId, bool failure = false)
        {
            _mockSearchService.GetSurrogateIdRanges(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(x =>
            {
                int numRanges = x.ArgAt<int>(4);
                var ranges = new List<(long StartId, long EndId)>();
                if (x.ArgAt<long>(1) <= x.ArgAt<long>(2)) // start <= end to break internal loop
                {
                    for (int i = 0; i < numRanges; i++)
                    {
                        ranges.Add((long.MaxValue - 1, long.MaxValue - 1));
                    }
                }

                return Task.FromResult<IReadOnlyList<(long StartId, long EndId)>>(ranges);
            });

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

            _mockQueueClient.EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), orchestratorJobId, false, false, Arg.Any<CancellationToken>()).Returns(x =>
            {
                string[] definitions = x.ArgAt<string[]>(1);
                Assert.Equal(numExpectedJobsPerResourceType, definitions.Length);
                return GetJobInfoArray(numExpectedJobsPerResourceType, orchestratorJobId, false, failure: failure);
            });

            _mockQueueClient.GetJobByGroupIdAsync(Arg.Any<byte>(), orchestratorJobId, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(x =>
            {
                return GetJobInfoArray(0, orchestratorJobId, true, orchestratorJobId, failure: failure);
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
