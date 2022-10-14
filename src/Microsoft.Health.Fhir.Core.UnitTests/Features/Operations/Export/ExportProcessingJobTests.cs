// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    public class ExportProcessingJobTests
    {
        private IExportJobTask _mockJob = Substitute.For<IExportJobTask>();

        public ExportProcessingJobTests()
        {
            _mockJob.ExecuteAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>()).Returns(x =>
            {
                string[] types = x.ArgAt<IReadOnlyList<Tuple<string, string>>>(1)[3].Item2.Split(',');
                return _mockJob.UpdateExportJob(x.ArgAt<ExportJobRecord>(0), x.ArgAt<WeakETag>(1), x.ArgAt<CancellationToken>(2));
            });
        }

        [Fact]
        public async Task GivenAnExportJob_WhenItSucceeds_ThenOutputsAreInTheResult()
        {
            string progressResult = string.Empty;

            Progress<string> progress = new Progress<string>((result) =>
            {
                progressResult = result;
            });

            var expectedResults = GenerateJobRecord(OperationStatus.Completed);

            var processingJob = new ExportProcessingJob(GenerateJobInfo(expectedResults), new Func<IExportJobTask>(() => _mockJob));
            var taskResult = await processingJob.ExecuteAsync(progress, CancellationToken.None);
            Assert.Equal(expectedResults, taskResult);
            Assert.Equal(expectedResults, progressResult);
        }

        [Fact]
        public async Task GivenAnExportJob_WhenItFails_ThenAnExceptionIsThrown()
        {
            Progress<string> progress = new Progress<string>((result) => { });

            var exceptionMessage = "Test job failed";
            var expectedResults = GenerateJobRecord(OperationStatus.Failed, exceptionMessage);

            var processingJob = new ExportProcessingJob(GenerateJobInfo(expectedResults), new Func<IExportJobTask>(() => _mockJob));
            var exception = await Assert.ThrowsAsync<JobExecutionException>(() => processingJob.ExecuteAsync(progress, CancellationToken.None));
            Assert.Equal(exceptionMessage, exception.Message);
        }

        [Fact]
        public async Task GivenAnExportJob_WhenItIsCancelled_ThenAnExceptionIsThrown()
        {
            Progress<string> progress = new Progress<string>((result) => { });

            var expectedResults = GenerateJobRecord(OperationStatus.Canceled);

            var processingJob = new ExportProcessingJob(GenerateJobInfo(expectedResults), new Func<IExportJobTask>(() => _mockJob));
            await Assert.ThrowsAsync<RetriableJobException>(() => processingJob.ExecuteAsync(progress, CancellationToken.None));
        }

        [Theory]
        [InlineData(OperationStatus.Queued)]
        [InlineData(OperationStatus.Running)]
        public async Task GivenAnExportJob_WhenItFinishesInANonTerminalState_ThenAnExceptionIsThrown(OperationStatus status)
        {
            Progress<string> progress = new Progress<string>((result) => { });

            var expectedResults = GenerateJobRecord(status);

            var processingJob = new ExportProcessingJob(GenerateJobInfo(expectedResults), new Func<IExportJobTask>(() => _mockJob));
            await Assert.ThrowsAsync<RetriableJobException>(() => processingJob.ExecuteAsync(progress, CancellationToken.None));
        }

        private string GenerateJobRecord(OperationStatus status, string failureReason = null)
        {
            var record = new ExportJobRecord(
                new Uri("https://localhost/ExportJob/"),
                ExportJobType.All,
                ExportFormatTags.ResourceName,
                null,
                null,
                "hash",
                0);
            record.Status = status;
            record.FailureDetails = new JobFailureDetails(failureReason, HttpStatusCode.InternalServerError);
            return JsonConvert.SerializeObject(record);
        }

        private JobInfo GenerateJobInfo(string record)
        {
            var info = new JobInfo();
            info.Id = RandomNumberGenerator.GetInt32(int.MaxValue);
            info.Definition = record;
            return info;
        }
    }
}
