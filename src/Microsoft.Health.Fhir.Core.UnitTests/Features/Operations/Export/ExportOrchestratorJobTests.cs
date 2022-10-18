// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    public class ExportOrchestratorJobTests
    {
        private ISearchService _mockSearchService = Substitute.For<ISearchService>();
        private IQueueClient _mockQueueClient = Substitute.For<IQueueClient>();
        private ILoggerFactory _loggerFactory = new NullLoggerFactory();

        [Theory]
        [InlineData(ExportJobType.Patient)]
        [InlineData(ExportJobType.Group)]
        public async Task GivenANonSystemLevelExportJob_WhenRun_ThenOneProcessingJobShouldBeCreated(ExportJobType exportJobType)
        {
            int numExpectedJobs = 1;
            long orchestratorJobId = 10000;

            SetupMockQueue(numExpectedJobs, orchestratorJobId);

            var orchestratorJob = GetJobInfoArray(0, orchestratorJobId, false, orchestratorJobId, numExpectedJobs, exportJobType: exportJobType).First();
            var exportOrchestratorJob = new ExportOrchestratorJob(orchestratorJob, _mockQueueClient, _mockSearchService, _loggerFactory);
            var result = await exportOrchestratorJob.ExecuteAsync(new Progress<string>((result) => { }), CancellationToken.None);
            var jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);
            CountOutputFiles(jobResult, numExpectedJobs);
        }

        [Fact]
        public async Task GivenAnExportJobWithParallelSetToOne_WhenRun_ThenOneProcessingJobShouldBeCreated()
        {
            int numExpectedJobs = 1;
            long orchestratorJobId = 10000;

            SetupMockQueue(numExpectedJobs, orchestratorJobId);

            var orchestratorJob = GetJobInfoArray(0, orchestratorJobId, false, orchestratorJobId, numExpectedJobs).First();
            var exportOrchestratorJob = new ExportOrchestratorJob(orchestratorJob, _mockQueueClient, _mockSearchService, _loggerFactory);
            var result = await exportOrchestratorJob.ExecuteAsync(new Progress<string>((result) => { }), CancellationToken.None);
            var jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);
            CountOutputFiles(jobResult, numExpectedJobs);
        }

        [Fact]
        public async Task GivenAnExportJobWithNoTypeRestriction_WhenRun_ThenMultipleProcessingJobsShouldBeCreated()
        {
            int numExpectedJobs = 10;
            long orchestratorJobId = 10000;

            SetupMockQueue(numExpectedJobs, orchestratorJobId);

            var orchestratorJob = GetJobInfoArray(0, orchestratorJobId, false, orchestratorJobId, numExpectedJobs).First();
            var exportOrchestratorJob = new ExportOrchestratorJob(orchestratorJob, _mockQueueClient, _mockSearchService, _loggerFactory);
            var result = await exportOrchestratorJob.ExecuteAsync(new Progress<string>((result) => { }), CancellationToken.None);
            var jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);
            CountOutputFiles(jobResult, numExpectedJobs);
        }

        [Fact]
        public async Task GivenAnExportJobWithTypeRestrictions_WhenRun_ThenTenProcessingJobsShouldBeCreatedPerResourceType()
        {
            int numExpectedJobs = 10;
            long orchestratorJobId = 10000;

            SetupMockQueue(numExpectedJobs * 2, orchestratorJobId);

            var orchestratorJob = GetJobInfoArray(0, orchestratorJobId, false, orchestratorJobId, numExpectedJobs, typeFilter: "Patient,Observation").First();
            var exportOrchestratorJob = new ExportOrchestratorJob(orchestratorJob, _mockQueueClient, _mockSearchService, _loggerFactory);
            var result = await exportOrchestratorJob.ExecuteAsync(new Progress<string>((result) => { }), CancellationToken.None);
            var jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);
            CountOutputFiles(jobResult, numExpectedJobs * 2);
        }

        [Fact]
        public async Task GivenAnExportJobThatFails_WhenRun_ThenFailureReasonsAreInTheJobRecord()
        {
            int numExpectedJobs = 10;
            long orchestratorJobId = 10000;
            string expectedMessage = "Job failed.";

            SetupMockQueue(numExpectedJobs, orchestratorJobId, failure: true);

            var orchestratorJob = GetJobInfoArray(0, orchestratorJobId, false, orchestratorJobId, numExpectedJobs).First();
            var exportOrchestratorJob = new ExportOrchestratorJob(orchestratorJob, _mockQueueClient, _mockSearchService, _loggerFactory);
            var exception = await Assert.ThrowsAsync<JobExecutionException>(() => exportOrchestratorJob.ExecuteAsync(new Progress<string>((result) => { }), CancellationToken.None));
            Assert.Equal(expectedMessage, exception.Message);
            Assert.Equal(expectedMessage, ((ExportJobRecord)exception.Error).FailureDetails.FailureReason);
            Assert.Equal(System.Net.HttpStatusCode.InternalServerError, ((ExportJobRecord)exception.Error).FailureDetails.FailureStatusCode);
        }

        [Fact]
        public async Task GivenAnExportJobThatIsRestarted_WhenRun_ThenNewProcessingJobsAreNotMade()
        {
            int numExpectedJobs = 10;
            long orchestratorJobId = 10000;

            SetupMockQueue(numExpectedJobs, orchestratorJobId, firstRun: false);

            var orchestratorJob = GetJobInfoArray(0, orchestratorJobId, false, orchestratorJobId, numExpectedJobs).First();
            var exportOrchestratorJob = new ExportOrchestratorJob(orchestratorJob, _mockQueueClient, _mockSearchService, _loggerFactory);
            var result = await exportOrchestratorJob.ExecuteAsync(new Progress<string>((result) => { }), CancellationToken.None);
            var jobResult = JsonConvert.DeserializeObject<ExportJobRecord>(result);
            CountOutputFiles(jobResult, 10);
        }

        private IEnumerable<JobInfo> GetJobInfoArray(
            int numJobInfos,
            long groupId,
            bool areAllCompleted,
            long orchestratorJobId = -1,
            int parallelNum = 1,
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
                                parallel: parallelNum,
                                since: new PartialDateTime(Clock.UtcNow))
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
                                parallel: parallelNum,
                                typeId: (int)JobType.ExportProcessing)
                {
                    Id = $"{i}",
                    Status = areAllCompleted ? (failure ? OperationStatus.Failed : OperationStatus.Completed) : OperationStatus.Running,
                };

                processingRecord.Output.Add("Patient", patientList);

                if (failure)
                {
                    processingRecord.FailureDetails = new JobFailureDetails("Job failed.", System.Net.HttpStatusCode.InternalServerError);
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

        private void CountOutputFiles(ExportJobRecord record, int expectedCount)
        {
            int fileCount = 0;
            foreach (var output in record.Output)
            {
                foreach (var key in output.Value)
                {
                    fileCount++;
                }
            }

            Assert.Equal(expectedCount, fileCount);
            Assert.Equal(OperationStatus.Completed, record.Status);
        }

        private void SetupMockQueue(int numExpectedJobs, long orchestratorJobId, bool firstRun = true, bool failure = false)
        {
            _mockSearchService.GetDateTimeRange(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(x =>
            {
                int numRanges = x.ArgAt<int>(3);
                var ranges = new List<Tuple<DateTime, DateTime>>();
                for (int i = 0; i < numRanges; i++)
                {
                    ranges.Add(new Tuple<DateTime, DateTime>(new DateTime(10000), new DateTime(20000)));
                }

                return Task.FromResult<IReadOnlyList<Tuple<DateTime, DateTime>>>(ranges);
            });

            _mockQueueClient.EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), orchestratorJobId, false, false, Arg.Any<CancellationToken>()).Returns(x =>
            {
                string[] definitions = x.ArgAt<string[]>(1);
                Assert.Equal(numExpectedJobs, definitions.Length);
                return GetJobInfoArray(numExpectedJobs, orchestratorJobId, false, failure: failure);
            });

            _mockQueueClient.GetJobByGroupIdAsync(Arg.Any<byte>(), orchestratorJobId, false, Arg.Any<CancellationToken>()).Returns(x =>
            {
                if (firstRun)
                {
                    firstRun = false;
                    return GetJobInfoArray(0, orchestratorJobId, false, orchestratorJobId, failure: failure);
                }
                else
                {
                    return GetJobInfoArray(numExpectedJobs, orchestratorJobId, true, orchestratorJobId, failure: failure);
                }
            });
        }
    }
}
