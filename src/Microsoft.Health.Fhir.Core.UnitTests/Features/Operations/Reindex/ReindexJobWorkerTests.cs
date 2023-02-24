// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Reindex
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
    public class ReindexJobWorkerTests
    {
        private const ushort DefaultMaximumNumberOfConcurrentJobAllowed = 1;
        private static readonly TimeSpan DefaultJobHeartbeatTimeoutThreshold = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan DefaultJobPollingFrequency = TimeSpan.FromMilliseconds(100);

        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
        private readonly ReindexJobConfiguration _reindexJobConfiguration = new ReindexJobConfiguration();
        private readonly Func<IReindexJobTask> _reindexJobTaskFactory = Substitute.For<Func<IReindexJobTask>>();
        private readonly IReindexJobTask _task = Substitute.For<IReindexJobTask>();

        private readonly ReindexJobWorker _reindexJobWorker;

        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly CancellationToken _cancellationToken;

        public ReindexJobWorkerTests()
        {
            _reindexJobConfiguration.MaximumNumberOfConcurrentJobsAllowed = DefaultMaximumNumberOfConcurrentJobAllowed;
            _reindexJobConfiguration.JobHeartbeatTimeoutThreshold = DefaultJobHeartbeatTimeoutThreshold;
            _reindexJobConfiguration.JobPollingFrequency = DefaultJobPollingFrequency;

            _reindexJobTaskFactory().Returns(_task);
            var scopedOperationDataStore = Substitute.For<IScoped<IFhirOperationDataStore>>();
            scopedOperationDataStore.Value.Returns(_fhirOperationDataStore);

            var searchParameterOperations = Substitute.For<ISearchParameterOperations>();

            _reindexJobWorker = new ReindexJobWorker(
                () => scopedOperationDataStore,
                Options.Create(_reindexJobConfiguration),
                _reindexJobTaskFactory,
                searchParameterOperations,
                NullLogger<ReindexJobWorker>.Instance);

            _reindexJobWorker.Handle(new Messages.Search.SearchParametersInitializedNotification(), CancellationToken.None);

            _cancellationToken = _cancellationTokenSource.Token;
        }

        [Fact]
        public async Task GivenThereIsNoRunningJob_WhenExecuted_ThenATaskShouldBeCreated()
        {
            ReindexJobWrapper job = CreateReindexJobWrapper();

            SetupOperationDataStore(job);

            _cancellationTokenSource.CancelAfter(DefaultJobPollingFrequency);

            await _reindexJobWorker.ExecuteAsync(_cancellationToken);

            _reindexJobTaskFactory().Received(1);
        }

        [Fact]
        public async Task GivenTheNumberOfRunningJobEqualsThreshold_WhenExecuted_ThenATaskShouldNotBeCreated()
        {
            ReindexJobWrapper job = CreateReindexJobWrapper();

            SetupOperationDataStore(job);

            _task.ExecuteAsync(job.JobRecord, job.ETag, _cancellationToken).Returns(Task.Run(async () => { await Task.Delay(1000); }));

            _cancellationTokenSource.CancelAfter(DefaultJobPollingFrequency * 2);

            await _reindexJobWorker.ExecuteAsync(_cancellationToken);

            _reindexJobTaskFactory.Received(1);
        }

        private void SetupOperationDataStore(
            ReindexJobWrapper job,
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

            _fhirOperationDataStore.AcquireReindexJobsAsync(
                maximumNumberOfConcurrentJobsAllowed,
                jobHeartbeatTimeoutThreshold.Value,
                _cancellationToken)
                .Returns(new[] { job });
        }

        private ReindexJobWrapper CreateReindexJobWrapper()
        {
            Dictionary<string, string> searchParameterHashMap = new Dictionary<string, string>();
            searchParameterHashMap.Add("patient", "hash1");
            return new ReindexJobWrapper(new ReindexJobRecord(searchParameterHashMap, new List<string>(), new List<string>(), new List<string>(), 5), WeakETag.FromVersionId("0"));
        }
    }
}
