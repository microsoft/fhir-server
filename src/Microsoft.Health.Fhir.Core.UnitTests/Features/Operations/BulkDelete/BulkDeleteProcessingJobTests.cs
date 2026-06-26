// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

using FhirJobConflictException = global::Microsoft.Health.Fhir.Core.Features.Operations.JobConflictException;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.BulkDelete
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BulkDelete)]
    public class BulkDeleteProcessingJobTests
    {
        private IDeletionService _deleter;
        private BulkDeleteProcessingJob _processingJob;
        private ISearchService _searchService;
        private IQueueClient _queueClient;

        public BulkDeleteProcessingJobTests()
        {
            _searchService = Substitute.For<ISearchService>();
            _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), resourceVersionTypes: Arg.Any<ResourceVersionType>())
                .Returns(Task.FromResult(new SearchResult(5, new List<Tuple<string, string>>())));
            _queueClient = Substitute.For<IQueueClient>();
            _deleter = Substitute.For<IDeletionService>();
            _processingJob = new BulkDeleteProcessingJob(_deleter.CreateMockScopeFactory(), Substitute.For<RequestContextAccessor<IFhirRequestContext>>(), Substitute.For<IMediator>(), _searchService.CreateMockScopeFactory(), _queueClient);
        }

        [Fact]
        public async Task GivenProcessingJob_WhenJobIsRun_ThenResourcesAreDeleted()
        {
            _deleter.ClearReceivedCalls();

            var definition = new BulkDeleteDefinition(JobType.BulkDeleteProcessing, DeleteOperation.HardDelete, "Patient", new List<Tuple<string, string>>(), new List<string>(), "https:\\\\test.com", "https:\\\\test.com", "test");
            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(definition),
            };

            var substituteResults = new Dictionary<string, long>();
            substituteResults.Add("Patient", 3);

            _deleter.DeleteMultipleAsync(Arg.Any<ConditionalDeleteResourceRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IList<string>>())
                .Returns(args => substituteResults);

            var result = JsonConvert.DeserializeObject<BulkDeleteResult>(await _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
            Assert.Single(result.ResourcesDeleted);
            Assert.Equal(3, result.ResourcesDeleted["Patient"]);

            await _deleter.ReceivedWithAnyArgs(1).DeleteMultipleAsync(Arg.Any<ConditionalDeleteResourceRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IList<string>>());
        }

        [Fact]
        public async Task GivenProcessingJob_WhenJobIsRunWithMultipleResourceTypes_ThenFollowupJobIsCreated()
        {
            _deleter.ClearReceivedCalls();

            var definition = new BulkDeleteDefinition(JobType.BulkDeleteProcessing, DeleteOperation.HardDelete, "Patient,Observation,Device", new List<Tuple<string, string>>(), new List<string>(), "https:\\\\test.com", "https:\\\\test.com", "test");
            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(definition),
            };

            var substituteResults = new Dictionary<string, long>();
            substituteResults.Add("Patient", 3);

            _deleter.DeleteMultipleAsync(Arg.Any<ConditionalDeleteResourceRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IList<string>>())
                .Returns(args => substituteResults);

            var result = JsonConvert.DeserializeObject<BulkDeleteResult>(await _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
            Assert.Single(result.ResourcesDeleted);
            Assert.Equal(3, result.ResourcesDeleted["Patient"]);

            await _deleter.ReceivedWithAnyArgs(1).DeleteMultipleAsync(Arg.Any<ConditionalDeleteResourceRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IList<string>>());

            // Checks that one processing job was queued
            var calls = _queueClient.ReceivedCalls();
            var definitions = (string[])calls.First().GetArguments()[1];
            Assert.Single(definitions);

            // Checks that the processing job removed the resource type that was processed and lists the remaining two resource types
            var actualDefinition = JsonConvert.DeserializeObject<BulkDeleteDefinition>(definitions[0]);
            Assert.Equal(2, actualDefinition.Type.SplitByOrSeparator().Count());
        }

        [Fact]
        public async Task GivenProcessingJobForSearchParameter_WhenReindexStartsBeforeExecution_ThenConflictIsThrown()
        {
            var definition = new BulkDeleteDefinition(JobType.BulkDeleteProcessing, DeleteOperation.HardDelete, "SearchParameter", new List<Tuple<string, string>>(), new List<string>(), "https:\\\\test.com", "https:\\\\test.com", "test");
            var jobInfo = new JobInfo() { Id = 1, Definition = JsonConvert.SerializeObject(definition) };

            // Simulate the database layer throwing JobConflictException when trying to delete SearchParameter during active reindex
            _deleter.DeleteMultipleAsync(Arg.Any<ConditionalDeleteResourceRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IList<string>>()).Returns<Task<IDictionary<string, long>>>(x => throw new FhirJobConflictException("A reindex job is currently running."));

            await Assert.ThrowsAsync<FhirJobConflictException>(async () => await _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
        }

        [Fact]
        public async Task GivenBulkDeleteWithSearchParameterFirst_WhenReindexIsActive_ThenConflictIsThrownImmediately()
        {
            var definition = new BulkDeleteDefinition(JobType.BulkDeleteProcessing, DeleteOperation.HardDelete, "SearchParameter,Patient", new List<Tuple<string, string>>(), new List<string>(), "https:\\\\test.com", "https:\\\\test.com", "test");
            var jobInfo = new JobInfo() { Id = 1, Definition = JsonConvert.SerializeObject(definition) };

            // SearchParameter processed first - database layer throws conflict due to active reindex
            _deleter.DeleteMultipleAsync(Arg.Any<ConditionalDeleteResourceRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IList<string>>()).Returns<Task<IDictionary<string, long>>>(x => throw new FhirJobConflictException("A reindex job is currently running."));

            // The conflict should propagate through the workflow, preventing any deletions
            await Assert.ThrowsAsync<FhirJobConflictException>(async () => await _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
        }

        [Fact]
        public async Task GivenBulkDeleteWithSearchParameterSecond_WhenReindexStartsBetweenDeletes_ThenFollowUpJobFails()
        {
            var definition = new BulkDeleteDefinition(JobType.BulkDeleteProcessing, DeleteOperation.HardDelete, "Patient,SearchParameter", new List<Tuple<string, string>>(), new List<string>(), "https:\\\\test.com", "https:\\\\test.com", "test");
            var jobInfo = new JobInfo() { Id = 1, Definition = JsonConvert.SerializeObject(definition) };

            // Patient processed first and succeeds (reindex hasn't started yet)
            var patientResults = new Dictionary<string, long> { { "Patient", 5 } };
            _deleter.DeleteMultipleAsync(Arg.Any<ConditionalDeleteResourceRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IList<string>>()).Returns(Task.FromResult<IDictionary<string, long>>(patientResults));

            // Execute the first job - should succeed and enqueue follow-up job for SearchParameter
            var result = await _processingJob.ExecuteAsync(jobInfo, CancellationToken.None);
            var bulkDeleteResult = JsonConvert.DeserializeObject<BulkDeleteResult>(result);
            Assert.Equal(5, bulkDeleteResult.ResourcesDeleted["Patient"]);

            // Verify a follow-up job was queued for SearchParameter
            var calls = _queueClient.ReceivedCalls();
            var definitions = (string[])calls.First().GetArguments()[1];
            Assert.Single(definitions);
            var followUpDefinition = JsonConvert.DeserializeObject<BulkDeleteDefinition>(definitions[0]);
            Assert.Equal("SearchParameter", followUpDefinition.Type);

            // Now simulate the follow-up job running, but reindex has started between the two jobs
            _deleter.ClearReceivedCalls();
            _deleter.DeleteMultipleAsync(Arg.Any<ConditionalDeleteResourceRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IList<string>>()).Returns<Task<IDictionary<string, long>>>(x => throw new FhirJobConflictException("A reindex job is currently running."));

            var followUpJobInfo = new JobInfo() { Id = 2, Definition = definitions[0] };
            await Assert.ThrowsAsync<FhirJobConflictException>(async () => await _processingJob.ExecuteAsync(followUpJobInfo, CancellationToken.None));
        }
    }
}
