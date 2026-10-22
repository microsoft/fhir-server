// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Medino;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
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

            _deleter.DeleteMultipleAsync(Arg.Any<ConditionalDeleteResourceRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IList<string>>(), Arg.Any<Action<string, long>>())
                .Returns(args => substituteResults);

            var result = JsonConvert.DeserializeObject<BulkDeleteResult>(await _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
            Assert.Single(result.ResourcesDeleted);
            Assert.Equal(3, result.ResourcesDeleted["Patient"]);

            await _deleter.ReceivedWithAnyArgs(1).DeleteMultipleAsync(Arg.Any<ConditionalDeleteResourceRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IList<string>>(), Arg.Any<Action<string, long>>());
        }

        [Fact]
        public async Task GivenProcessingJob_WhenProfileResourcesAreSkipped_ThenResourcesAreIgnored()
        {
            var definition = new BulkDeleteDefinition(JobType.BulkDeleteProcessing, DeleteOperation.HardDelete, "StructureDefinition", new List<Tuple<string, string>>(), new List<string>(), "https:\\test.com", "https:\\test.com", "test");
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(definition),
            };

            _deleter.DeleteMultipleAsync(Arg.Any<ConditionalDeleteResourceRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IList<string>>(), Arg.Any<Action<string, long>>())
                .Returns(callInfo =>
                {
                    callInfo.ArgAt<Action<string, long>>(3)("StructureDefinition", 2);
                    return new Dictionary<string, long>();
                });

            var result = JsonConvert.DeserializeObject<BulkDeleteResult>(await _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));

            Assert.Empty(result.ResourcesDeleted);
            Assert.Equal(2, result.ResourcesIgnored["StructureDefinition"]);
            Assert.Contains("Skipped 2 StructureDefinition resource(s)", Assert.Single(result.Issues));
            Assert.Contains("EditProfileDefinitions", Assert.Single(result.Issues));
        }

        [Fact]
        public async Task GivenProfileModificationAllowed_WhenProcessingJobRuns_ThenProfileResourcesAreDeleted()
        {
            var definition = new BulkDeleteDefinition(
                JobType.BulkDeleteProcessing,
                DeleteOperation.HardDelete,
                "StructureDefinition",
                new List<Tuple<string, string>>(),
                new List<string>(),
                "https:\\test.com",
                "https:\\test.com",
                "test",
                allowProfileResourceModification: true);
            var jobInfo = new JobInfo { Id = 1, Definition = JsonConvert.SerializeObject(definition) };
            _deleter.DeleteMultipleAsync(
                    Arg.Any<ConditionalDeleteResourceRequest>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<IList<string>>(),
                    null)
                .Returns(new Dictionary<string, long> { ["StructureDefinition"] = 2 });

            var result = JsonConvert.DeserializeObject<BulkDeleteResult>(await _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));

            Assert.Equal(2, result.ResourcesDeleted["StructureDefinition"]);
            Assert.Empty(result.ResourcesIgnored);
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

            _deleter.DeleteMultipleAsync(Arg.Any<ConditionalDeleteResourceRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IList<string>>(), Arg.Any<Action<string, long>>())
                .Returns(args => substituteResults);

            var result = JsonConvert.DeserializeObject<BulkDeleteResult>(await _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
            Assert.Single(result.ResourcesDeleted);
            Assert.Equal(3, result.ResourcesDeleted["Patient"]);

            await _deleter.ReceivedWithAnyArgs(1).DeleteMultipleAsync(Arg.Any<ConditionalDeleteResourceRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IList<string>>(), Arg.Any<Action<string, long>>());

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
            _deleter.DeleteMultipleAsync(Arg.Any<ConditionalDeleteResourceRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IList<string>>(), Arg.Any<Action<string, long>>()).Returns<Task<IDictionary<string, long>>>(x => throw new FhirJobConflictException("A reindex job is currently running."));

            var exception = await Assert.ThrowsAsync<JobExecutionException>(() => _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
            Assert.True(exception.IsCustomerCaused);
            Assert.IsType<FhirJobConflictException>(exception.InnerException);
        }

        [Fact]
        public async Task GivenBulkDeleteWithSearchParameterFirst_WhenReindexIsActive_ThenConflictIsThrownImmediately()
        {
            var definition = new BulkDeleteDefinition(JobType.BulkDeleteProcessing, DeleteOperation.HardDelete, "SearchParameter,Patient", new List<Tuple<string, string>>(), new List<string>(), "https:\\\\test.com", "https:\\\\test.com", "test");
            var jobInfo = new JobInfo() { Id = 1, Definition = JsonConvert.SerializeObject(definition) };

            // SearchParameter processed first - database layer throws conflict due to active reindex
            _deleter.DeleteMultipleAsync(Arg.Any<ConditionalDeleteResourceRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IList<string>>(), Arg.Any<Action<string, long>>()).Returns<Task<IDictionary<string, long>>>(x => throw new FhirJobConflictException("A reindex job is currently running."));

            // The conflict should propagate through the workflow, preventing any deletions
            var exception = await Assert.ThrowsAsync<JobExecutionException>(() => _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
            Assert.True(exception.IsCustomerCaused);
            Assert.IsType<FhirJobConflictException>(exception.InnerException);
        }

        [Fact]
        public async Task GivenBulkDeleteWithSearchParameterSecond_WhenReindexStartsBetweenDeletes_ThenFollowUpJobFails()
        {
            var definition = new BulkDeleteDefinition(JobType.BulkDeleteProcessing, DeleteOperation.HardDelete, "Patient,SearchParameter", new List<Tuple<string, string>>(), new List<string>(), "https:\\\\test.com", "https:\\\\test.com", "test");
            var jobInfo = new JobInfo() { Id = 1, Definition = JsonConvert.SerializeObject(definition) };

            // Patient processed first and succeeds (reindex hasn't started yet)
            var patientResults = new Dictionary<string, long> { { "Patient", 5 } };
            _deleter.DeleteMultipleAsync(Arg.Any<ConditionalDeleteResourceRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IList<string>>(), Arg.Any<Action<string, long>>()).Returns(Task.FromResult<IDictionary<string, long>>(patientResults));

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
            _deleter.DeleteMultipleAsync(Arg.Any<ConditionalDeleteResourceRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IList<string>>(), Arg.Any<Action<string, long>>()).Returns<Task<IDictionary<string, long>>>(x => throw new FhirJobConflictException("A reindex job is currently running."));

            var followUpJobInfo = new JobInfo() { Id = 2, Definition = definitions[0] };
            var exception = await Assert.ThrowsAsync<JobExecutionException>(() => _processingJob.ExecuteAsync(followUpJobInfo, CancellationToken.None));
            Assert.True(exception.IsCustomerCaused);
            Assert.IsType<FhirJobConflictException>(exception.InnerException);
        }

        [Theory]
        [InlineData(true, false, true)]
        [InlineData(false, true, false)]
        [InlineData(true, true, false)]
        public async Task GivenIncompleteDeletion_WhenProcessingJobRuns_ThenOnlyReindexConflictsAreCustomerCaused(bool reindexConflict, bool systemFailure, bool expectedCustomerCaused)
        {
            // Arrange
            var definition = new BulkDeleteDefinition(JobType.BulkDeleteProcessing, DeleteOperation.HardDelete, "SearchParameter", new List<Tuple<string, string>>(), new List<string>(), "https://test.com", "https://test.com", "test");
            var jobInfo = new JobInfo { Id = 1, Definition = JsonConvert.SerializeObject(definition) };
            var partialResults = new Dictionary<string, long> { ["SearchParameter"] = 2 };
            var exceptions = new List<Exception>();
            if (reindexConflict)
            {
                exceptions.Add(new IncompleteOperationException<Dictionary<string, long>>(
                    new FhirJobConflictException("A reindex job is currently running."),
                    partialResults));
            }

            if (systemFailure)
            {
                exceptions.Add(new InvalidOperationException("Storage failure."));
            }

            _deleter.DeleteMultipleAsync(Arg.Any<ConditionalDeleteResourceRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IList<string>>(), Arg.Any<Action<string, long>>())
                .Returns<Task<IDictionary<string, long>>>(_ => throw new IncompleteOperationException<IDictionary<string, long>>(new AggregateException(exceptions), partialResults));

            // Act
            var exception = await Assert.ThrowsAsync<JobExecutionException>(() => _processingJob.ExecuteAsync(jobInfo, CancellationToken.None));

            // Assert
            Assert.Equal(expectedCustomerCaused, exception.IsCustomerCaused);
            var result = Assert.IsType<BulkDeleteResult>(exception.Error);
            Assert.Equal(2, result.ResourcesDeleted["SearchParameter"]);
            Assert.NotEmpty(result.Issues);
            if (reindexConflict)
            {
                Assert.IsType<FhirJobConflictException>(exception.InnerException);
                Assert.Contains("JobConflictException: A reindex job is currently running.", result.Issues);
            }

            if (systemFailure)
            {
                Assert.Contains("InvalidOperationException: Storage failure.", result.Issues);
            }
        }
    }
}
