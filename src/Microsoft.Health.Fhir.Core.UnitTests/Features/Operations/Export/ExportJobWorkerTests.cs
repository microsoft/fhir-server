// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Export;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    public class ExportJobWorkerTests
    {
        private const ushort DefaultMaximumNumberOfConcurrentJobAllowed = 1;
        private static readonly TimeSpan DefaultJobHeartbeatTimeoutThreshold = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan DefaultJobPollingFrequency = TimeSpan.FromMilliseconds(100);

        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
        private readonly ExportJobConfiguration _exportJobConfiguration = new ExportJobConfiguration();
        private readonly Func<IExportJobTask> _exportJobTaskFactory = Substitute.For<Func<IExportJobTask>>();
        private readonly IExportJobTask _task = Substitute.For<IExportJobTask>();

        private readonly ExportJobWorker _exportJobWorker;

        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly CancellationToken _cancellationToken;

        public ExportJobWorkerTests()
        {
            _exportJobConfiguration.MaximumNumberOfConcurrentJobsAllowed = DefaultMaximumNumberOfConcurrentJobAllowed;
            _exportJobConfiguration.JobHeartbeatTimeoutThreshold = DefaultJobHeartbeatTimeoutThreshold;
            _exportJobConfiguration.JobPollingFrequency = DefaultJobPollingFrequency;

            _exportJobTaskFactory().Returns(_task);
            var scopedOperationDataStore = Substitute.For<IScoped<IFhirOperationDataStore>>();
            scopedOperationDataStore.Value.Returns(_fhirOperationDataStore);

            _exportJobWorker = new ExportJobWorker(
                () => scopedOperationDataStore,
                Options.Create(_exportJobConfiguration),
                _exportJobTaskFactory,
                NullLogger<ExportJobWorker>.Instance);

            _cancellationToken = _cancellationTokenSource.Token;
        }

        [Fact]
        public async Task GivenThereIsNoRunningJob_WhenExecuted_ThenATaskShouldBeCreated()
        {
            ExportJobOutcome job = CreateExportJobOutcome();

            SetupOperationDataStore(job);

            _cancellationTokenSource.CancelAfter(DefaultJobPollingFrequency);

            await _exportJobWorker.ExecuteAsync(_cancellationToken);

            _exportJobTaskFactory().Received(1);
        }

        [Fact]
        public async Task GivenTheNumberOfRunningJobExceedsThreshold_WhenExecuted_ThenATaskShouldNotBeCreated()
        {
            ExportJobOutcome job = CreateExportJobOutcome();

            SetupOperationDataStore(job);

            _task.ExecuteAsync(job.JobRecord, job.ETag, _cancellationToken).Returns(Task.Run(async () => { await Task.Delay(1000); }));

            _cancellationTokenSource.CancelAfter(DefaultJobPollingFrequency * 2);

            await _exportJobWorker.ExecuteAsync(_cancellationToken);

            _exportJobTaskFactory.Received(1);
        }

        [Fact]
        public async Task GivenTheNumberOfRunningJobDoesNotExceedThreshold_WhenExecuted_ThenATaskShouldBeCreated()
        {
            const int MaximumNumberOfConcurrentJobsAllowed = 2;

            _exportJobConfiguration.MaximumNumberOfConcurrentJobsAllowed = MaximumNumberOfConcurrentJobsAllowed;

            ExportJobOutcome job1 = CreateExportJobOutcome();
            ExportJobOutcome job2 = CreateExportJobOutcome();

            SetupOperationDataStore(
                job1,
                maximumNumberOfConcurrentJobsAllowed: MaximumNumberOfConcurrentJobsAllowed);

            _task.ExecuteAsync(job1.JobRecord, job1.ETag, _cancellationToken).Returns(Task.Run(() =>
            {
                // Simulate the fact a new job now becomes available.
                SetupOperationDataStore(
                    job2,
                    maximumNumberOfConcurrentJobsAllowed: MaximumNumberOfConcurrentJobsAllowed);

                return Task.CompletedTask;
            }));

            bool isSecondJobCalled = false;

            _task.ExecuteAsync(job2.JobRecord, job2.ETag, _cancellationToken).Returns(Task.Run(() =>
            {
                // The task was called and therefore we can cancel the worker.
                isSecondJobCalled = true;

                _cancellationTokenSource.Cancel();

                return Task.CompletedTask;
            }));

            // In case the task was not called, cancel the worker after certain period of time.
            _cancellationTokenSource.CancelAfter(DefaultJobPollingFrequency * 3);

            await _exportJobWorker.ExecuteAsync(_cancellationToken);

            Assert.True(isSecondJobCalled);
        }

        private void SetupOperationDataStore(
            ExportJobOutcome job,
            ushort maximumNumberOfConcurrentJobsAllowed = DefaultMaximumNumberOfConcurrentJobAllowed,
            TimeSpan? jobHeartbeatTimeoutThreshold = null,
            TimeSpan? jobPollingFrequency = null)
        {
            if (jobHeartbeatTimeoutThreshold == null)
            {
                jobHeartbeatTimeoutThreshold = DefaultJobHeartbeatTimeoutThreshold;
            }

            if (jobPollingFrequency == null)
            {
                jobPollingFrequency = DefaultJobPollingFrequency;
            }

            _fhirOperationDataStore.AcquireExportJobsAsync(
                maximumNumberOfConcurrentJobsAllowed,
                jobHeartbeatTimeoutThreshold.Value,
                _cancellationToken)
                .Returns(new[] { job });
        }

        private ExportJobOutcome CreateExportJobOutcome()
        {
            var exportRequest = new CreateExportRequest(new Uri($"http://localhost/ExportJob/"), "destinationType", "destinationConnection");
            return new ExportJobOutcome(new ExportJobRecord(exportRequest.RequestUri, "Patient", "hash"), WeakETag.FromVersionId("0"));
        }
    }
}
