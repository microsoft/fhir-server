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
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
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
        private BulkDeleteOrchestratorJob _orchestratorJob;

        public BulkDeleteOrchestratorJobTests()
        {
            _queueClient = Substitute.For<IQueueClient>();
            _searchService = Substitute.For<ISearchService>();
            _orchestratorJob = new BulkDeleteOrchestratorJob(_queueClient, _searchService.CreateMockScopeProvider());
        }

        [Theory]
        [MemberData(nameof(JobFailureTestData.OrchestratorFailureCases), MemberType = typeof(JobFailureTestData))]
        public async Task GivenOrchestrationFailure_WhenExecuted_ThenClassificationIsPreserved(string scenario, bool expectedCustomerCaused, string stage)
        {
            // Arrange
            var failure = JobFailureTestData.CreateException(scenario);
            _searchService.GetUsedResourceTypes(Arg.Any<CancellationToken>())
                .Returns(_ => stage == "discovery" ? Task.FromException<IReadOnlyList<string>>(failure) : Task.FromResult<IReadOnlyList<string>>(new[] { "Patient" }));
            _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>())
                .Returns(_ => stage == "search" ? Task.FromException<SearchResult>(failure) : Task.FromResult(new SearchResult(2, new List<Tuple<string, string>>())));
            _queueClient.EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns<Task<IReadOnlyList<JobInfo>>>(_ => throw failure);
            var definition = new BulkDeleteDefinition(JobType.BulkDeleteOrchestrator, DeleteOperation.HardDelete, null, null, null, "test", "test", "test");
            var jobInfo = new JobInfo { GroupId = 1, Definition = JsonConvert.SerializeObject(definition) };

            // Act
            var exception = await Record.ExceptionAsync(() => _orchestratorJob.ExecuteAsync(jobInfo, CancellationToken.None));

            // Assert
            if (expectedCustomerCaused)
            {
                var jobException = Assert.IsType<JobExecutionException>(exception);
                Assert.True(jobException.IsCustomerCaused);
                Assert.Same(failure, jobException.InnerException);
                Assert.Contains(failure.Message, Assert.IsType<BulkDeleteResult>(jobException.Error).Issues);
            }
            else
            {
                Assert.Same(failure, exception);
            }
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
            _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>()).Returns((x) =>
            {
                var result = new SearchResult(2, new List<Tuple<string, string>>());
                return Task.FromResult(result);
            });

            var definition = new BulkDeleteDefinition(JobType.BulkDeleteOrchestrator, DeleteOperation.HardDelete, null, null, null, "test", "test", "test");
            var jobInfo = new JobInfo()
            {
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
            };

            await _orchestratorJob.ExecuteAsync(jobInfo, CancellationToken.None);
            await _queueClient.ReceivedWithAnyArgs(1).EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), false, Arg.Any<CancellationToken>());
            await _searchService.ReceivedWithAnyArgs(1).GetUsedResourceTypes(Arg.Any<CancellationToken>());

            // Checks that one processing job was queued
            var calls = _queueClient.ReceivedCalls();
            var definitions = (string[])calls.First().GetArguments()[1];
            Assert.Single(definitions);

            // Checks that the processing job lists both resource types
            var actualDefinition = JsonConvert.DeserializeObject<BulkDeleteDefinition>(definitions[0]);
            Assert.Equal(2, actualDefinition.Type.SplitByOrSeparator().Count());

            // Confirms the default last updated search parameter was added
            Assert.Single(actualDefinition.SearchParameters);
            Assert.Equal("_lastUpdated", actualDefinition.SearchParameters.First().Item1);
            Assert.StartsWith("lt", actualDefinition.SearchParameters.First().Item2);
        }

        [Fact]
        public async Task GivenBulkDeleteJob_WhenResourceTypeIsGiven_ThenOneProcessingJobIsCreated()
        {
            _queueClient.ClearReceivedCalls();
            _searchService.ClearReceivedCalls();

            _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>()).Returns((x) =>
            {
                var result = new SearchResult(2, new List<Tuple<string, string>>());
                return Task.FromResult(result);
            });

            var definition = new BulkDeleteDefinition(JobType.BulkDeleteOrchestrator, DeleteOperation.HardDelete, "Patient", null, null, "test", "test", "test");
            var jobInfo = new JobInfo()
            {
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
            };

            await _orchestratorJob.ExecuteAsync(jobInfo, CancellationToken.None);
            await _queueClient.ReceivedWithAnyArgs(1).EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), false, Arg.Any<CancellationToken>());
            await _searchService.DidNotReceiveWithAnyArgs().GetUsedResourceTypes(Arg.Any<CancellationToken>());

            // Checks that one processing job was queued
            var calls = _queueClient.ReceivedCalls();
            Assert.Single((string[])calls.First().GetArguments()[1]);
        }

        [Fact]
        public async Task GivenBulkDeleteJob_WhenNoResourcesMatchCriteria_ThenNoProcessingJobsAreCreated()
        {
            _queueClient.ClearReceivedCalls();
            _searchService.ClearReceivedCalls();

            _searchService.GetUsedResourceTypes(Arg.Any<CancellationToken>()).Returns(new List<string>()
            {
                "Patient",
                "Observation",
            });
            _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>()).Returns((x) =>
            {
                var result = new SearchResult(0, new List<Tuple<string, string>>());
                return Task.FromResult(result);
            });

            var definition = new BulkDeleteDefinition(JobType.BulkDeleteOrchestrator, DeleteOperation.HardDelete, null, null, null, "test", "test", "test");
            var jobInfo = new JobInfo()
            {
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
            };

            await _orchestratorJob.ExecuteAsync(jobInfo, CancellationToken.None);
            await _queueClient.DidNotReceiveWithAnyArgs().EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), false, Arg.Any<CancellationToken>());
            await _searchService.ReceivedWithAnyArgs(1).GetUsedResourceTypes(Arg.Any<CancellationToken>());
        }
    }
}
