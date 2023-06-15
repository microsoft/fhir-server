// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Api.Features.Operations.BulkDelete;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.BulkDelete
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BulkDelete)]
    public class BulkDeleteOrchestratorJobTests
    {
        private IQueueClient _queueClient;
        private ISearchService _searchService;
        private IProgress<string> _progress;
        private BulkDeleteOrchestratorJob _orchestratorJob;

        public BulkDeleteOrchestratorJobTests()
        {
            _queueClient = Substitute.For<IQueueClient>();
            _searchService = Substitute.For<ISearchService>();
            _orchestratorJob = new BulkDeleteOrchestratorJob(_queueClient, _searchService);

            _progress = new Progress<string>((result) => { });
        }

        [Fact]
        public async Task GivenBulkDeleteJob_WhenNoResourceTypeIsGiven_ThenProcessingJobsForAllTypesAreCreated()
        {
            _queueClient.ClearReceivedCalls();
            _searchService.ClearReceivedCalls();

            _searchService.GetUsedResourceTypes(Arg.Any<CancellationToken>()).Returns(new List<string>()
            {
                "Patient",
                "Observation",
            });

            var definition = new BulkDeleteDefinition(JobType.BulkDeleteOrchestrator, DeleteOperation.HardDelete, null, null, "test", "test");
            var jobInfo = new JobInfo()
            {
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
            };

            await _orchestratorJob.ExecuteAsync(jobInfo, _progress, CancellationToken.None);
            await _queueClient.ReceivedWithAnyArgs(1).EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), false, false, Arg.Any<CancellationToken>());
            await _searchService.ReceivedWithAnyArgs(1).GetUsedResourceTypes(Arg.Any<CancellationToken>());

            // Checks that two processing jobs were queued
            var calls = _queueClient.ReceivedCalls();
            Assert.Equal(2, ((string[])calls.First().GetArguments()[1]).Length);
        }

        [Fact]
        public async Task GivenBulkDeleteJob_WhenResourceTypeIsGiven_ThenOneProcessingJobIsCreated()
        {
            _queueClient.ClearReceivedCalls();
            _searchService.ClearReceivedCalls();

            var definition = new BulkDeleteDefinition(JobType.BulkDeleteOrchestrator, DeleteOperation.HardDelete, "Patient", null, "test", "test");
            var jobInfo = new JobInfo()
            {
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
            };

            await _orchestratorJob.ExecuteAsync(jobInfo, _progress, CancellationToken.None);
            await _queueClient.ReceivedWithAnyArgs(1).EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), false, false, Arg.Any<CancellationToken>());
            await _searchService.DidNotReceiveWithAnyArgs().GetUsedResourceTypes(Arg.Any<CancellationToken>());

            // Checks that one processing job was queued
            var calls = _queueClient.ReceivedCalls();
            Assert.Single((string[])calls.First().GetArguments()[1]);
        }
    }
}
