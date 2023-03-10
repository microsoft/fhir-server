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
using Microsoft.Health.Fhir.Core.Messages.Storage;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    public class ExportJobWorkerTests
    {
        private const ushort DefaultMaximumNumberOfConcurrentJobAllowed = 1;
        private static readonly TimeSpan DefaultJobHeartbeatTimeoutThreshold = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan DefaultJobPollingFrequency = TimeSpan.FromMilliseconds(100);

        private readonly ILegacyExportOperationDataStore _fhirOperationDataStore = Substitute.For<ILegacyExportOperationDataStore>();
        private readonly ExportJobConfiguration _exportJobConfiguration = new ExportJobConfiguration();
        private readonly Func<IExportJobTask> _exportJobTaskFactory = Substitute.For<Func<IExportJobTask>>();
        private readonly IExportJobTask _task = Substitute.For<IExportJobTask>();

        private readonly LegacyExportJobWorker _legacyExportJobWorker;

        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly CancellationToken _cancellationToken;

        public ExportJobWorkerTests()
        {
            _exportJobConfiguration.MaximumNumberOfConcurrentJobsAllowedPerInstance = DefaultMaximumNumberOfConcurrentJobAllowed;
            _exportJobConfiguration.JobHeartbeatTimeoutThreshold = DefaultJobHeartbeatTimeoutThreshold;
            _exportJobConfiguration.JobPollingFrequency = DefaultJobPollingFrequency;

            _exportJobTaskFactory().Returns(_task);

            _legacyExportJobWorker = new LegacyExportJobWorker(
                () => _fhirOperationDataStore.CreateMockScope(),
                Options.Create(_exportJobConfiguration),
                _exportJobTaskFactory,
                NullLogger<LegacyExportJobWorker>.Instance);

            _legacyExportJobWorker.Handle(new StorageInitializedNotification(), CancellationToken.None);
            _cancellationToken = _cancellationTokenSource.Token;
        }

        [Fact]
        public async Task GivenThereIsNoRunningJob_WhenExecuted_ThenATaskShouldBeCreated()
        {
            ExportJobOutcome job = CreateExportJobOutcome();

            SetupOperationDataStore(job);

            _cancellationTokenSource.CancelAfter(DefaultJobPollingFrequency);

            await _legacyExportJobWorker.ExecuteAsync(_cancellationToken);

            _exportJobTaskFactory().Received(1);
        }

        [Fact]
        public async Task GivenTheNumberOfRunningJobExceedsThreshold_WhenExecuted_ThenATaskShouldNotBeCreated()
        {
            ExportJobOutcome job = CreateExportJobOutcome();

            SetupOperationDataStore(job);

            _task.ExecuteAsync(job.JobRecord, job.ETag, _cancellationToken).Returns(Task.Run(async () => { await Task.Delay(1000); }));

            _cancellationTokenSource.CancelAfter(DefaultJobPollingFrequency * 2);

            await _legacyExportJobWorker.ExecuteAsync(_cancellationToken);

            _exportJobTaskFactory.Received(1);
        }

        [Fact]
        public async Task GivenTheNumberOfRunningJobDoesNotExceedThreshold_WhenExecuted_ThenATaskShouldBeCreated()
        {
            const int MaximumNumberOfConcurrentJobsAllowed = 2;

            _exportJobConfiguration.MaximumNumberOfConcurrentJobsAllowedPerInstance = MaximumNumberOfConcurrentJobsAllowed;

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

            await _legacyExportJobWorker.ExecuteAsync(_cancellationToken);

            Assert.True(isSecondJobCalled);
        }

        [Fact]
        public async Task GivenAcquireExportJobThrowsException_WhenExecuted_ThenWeHaveADelayBeforeWeRetry()
        {
            _fhirOperationDataStore.AcquireExportJobsAsync(
                DefaultMaximumNumberOfConcurrentJobAllowed,
                DefaultJobHeartbeatTimeoutThreshold,
                _cancellationToken)
                .ThrowsForAnyArgs<Exception>();

            _cancellationTokenSource.CancelAfter(DefaultJobPollingFrequency * 1.25);

            await _legacyExportJobWorker.ExecuteAsync(_cancellationToken);

            // Assert that we received only one call to AcquireExportJobsAsync
            await _fhirOperationDataStore.ReceivedWithAnyArgs(1).AcquireExportJobsAsync(Arg.Any<ushort>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenOperationIsCancelled_WhenExecuted_ThenWeExitTheLoop()
        {
            ExportJobOutcome job = CreateExportJobOutcome();
            _fhirOperationDataStore.AcquireExportJobsAsync(
                Arg.Any<ushort>(),
                Arg.Any<TimeSpan>(),
                _cancellationToken)
                .Returns(x =>
                    {
                        _cancellationTokenSource.Cancel();
                        return new[] { job };
                    });

            await _legacyExportJobWorker.ExecuteAsync(_cancellationToken);

            // Assert that we received only one call to AcquireExportJobsAsync
            await _fhirOperationDataStore.ReceivedWithAnyArgs(1).AcquireExportJobsAsync(Arg.Any<ushort>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
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
            var exportRequest = new CreateExportRequest(new Uri($"http://localhost/ExportJob/"), ExportJobType.All);
            return new ExportJobOutcome(new ExportJobRecord(exportRequest.RequestUri, exportRequest.RequestType, ExportFormatTags.ResourceName, null, null, "hash", rollingFileSizeInMB: 64), WeakETag.FromVersionId("0"));
        }
    }
}
