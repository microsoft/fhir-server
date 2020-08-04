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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Search;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Reindex
{
    public class ReindexJobTaskTests
    {
        private const string PatientFileName = "Patient.ndjson";
        private const string ObservationFileName = "Observation.ndjson";
        private static readonly WeakETag _weakETag = WeakETag.FromVersionId("0");

        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
        private readonly IFhirDataStore _fhirDataStore = Substitute.For<IFhirDataStore>();
        private readonly ReindexJobConfiguration _reindexJobConfiguration = new ReindexJobConfiguration();
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly ISearchIndexer _searchIndexer = Substitute.For<ISearchIndexer>();

        private readonly ReindexJobTask _reindexJobTask;

        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly CancellationToken _cancellationToken;

        public ReindexJobTaskTests()
        {
            _cancellationToken = _cancellationTokenSource.Token;

            var job = new ReindexJobRecord(null, 1, null);

            _fhirOperationDataStore.UpdateReindexJobAsync(job, _weakETag, _cancellationToken).ReturnsForAnyArgs(new ReindexJobWrapper(job, _weakETag));

            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                true,
                Arg.Any<CancellationToken>()).
                Returns(new SearchResult(5, new List<Tuple<string, string>>()));

            _reindexJobTask = new ReindexJobTask(
                () => _fhirOperationDataStore.CreateMockScope(),
                Options.Create(_reindexJobConfiguration),
                () => _searchService.CreateMockScope(),
                SearchParameterFixtureData.SupportedSearchDefinitionManager,
                () => _fhirDataStore.CreateMockScope(),
                _searchIndexer,
                _mediator,
                NullLogger<ReindexJobTask>.Instance);
        }

        [Fact]
        public async Task GivenSupportedParams_WhenExecuted_ThenCorrectSearchIsPerformed()
        {
            // Add one parameter that needs to be indexed
            var param = SearchParameterFixtureData.SearchDefinitionManager.AllSearchParameters.Where(p => p.Name == "status").FirstOrDefault();
            param.IsSearchable = false;

            var job = new ReindexJobRecord(null, 1, null);

            // setup search result
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>()).
                Returns(CreateSearchResult());

            await _reindexJobTask.ExecuteAsync(job, _weakETag, _cancellationToken);

            // verify search for count
            await _searchService.Received().SearchForReindexAsync(Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>());

            // verify search for results
            await _searchService.Received().SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Where(t => t.Item1 == "_type" && t.Item2 == "Account").Any()),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>());

            Assert.Equal(OperationStatus.Completed, job.Status);
            Assert.Equal(5, job.Count);
            Assert.Equal("Account", job.ResourceList);
            Assert.Equal("status", job.SearchParamList);
            Assert.Collection<ReindexJobQueryStatus>(
                job.QueryList,
                item => Assert.True(item.ContinuationToken == null && item.Status == OperationStatus.Completed));

            param.IsSearchable = true;
        }

        [Fact]
        public async Task GivenContinuationToken_WhenExecuted_ThenAdditionalQueryAdded()
        {
            // Add one parameter that needs to be indexed
            var param = SearchParameterFixtureData.SearchDefinitionManager.AllSearchParameters.Where(p => p.Name == "identifier").FirstOrDefault();
            param.IsSearchable = false;

            var job = new ReindexJobRecord(null, 1, null);

            // setup search result
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>()).
                Returns(CreateSearchResult("token"));

            await _reindexJobTask.ExecuteAsync(job, _weakETag, _cancellationToken);

            // verify search for count
            await _searchService.Received().SearchForReindexAsync(Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>());

            // verify search for results
            await _searchService.Received().SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Where(t => t.Item1 == "_type" && t.Item2 == "Account").Any()),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>());

            Assert.Equal(OperationStatus.Running, job.Status);
            Assert.Equal(5, job.Count);
            Assert.Equal("Account", job.ResourceList);
            Assert.Equal("identifier", job.SearchParamList);
            Assert.Collection<ReindexJobQueryStatus>(
                job.QueryList,
                item => Assert.True(item.ContinuationToken == null && item.Status == OperationStatus.Completed),
                item2 => Assert.True(item2.ContinuationToken == "token" && item2.Status == OperationStatus.Queued));

            param.IsSearchable = true;
        }

        [Fact]
        public async Task GivenRunningJob_WhenExecuted_ThenQueuedQueryCompleted()
        {
            // Add one parameter that needs to be indexed
            var param = SearchParameterFixtureData.SearchDefinitionManager.AllSearchParameters.Where(p => p.Name == "appointment").FirstOrDefault();
            param.IsSearchable = false;

            var job = new ReindexJobRecord(null, 1, null);

            // setup search result
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>()).
                Returns(CreateSearchResult("token"));

            await _reindexJobTask.ExecuteAsync(job, _weakETag, _cancellationToken);

            // verify search for count
            await _searchService.Received().SearchForReindexAsync(Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>());

            // verify search for results
            await _searchService.Received().SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Where(t => t.Item1 == "_type" && t.Item2 == "Appointment,AppointmentResponse").Any()),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>());

            Assert.Equal(OperationStatus.Running, job.Status);
            Assert.Equal(5, job.Count);
            Assert.Equal("Appointment,AppointmentResponse", job.ResourceList);
            Assert.Equal("appointment", job.SearchParamList);
            Assert.Collection<ReindexJobQueryStatus>(
                job.QueryList,
                item => Assert.True(item.ContinuationToken == null && item.Status == OperationStatus.Completed),
                item2 => Assert.True(item2.ContinuationToken == "token" && item2.Status == OperationStatus.Queued));

            // setup search result
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>()).
                Returns(CreateSearchResult(null));

            await _reindexJobTask.ExecuteAsync(job, _weakETag, _cancellationToken);

            Assert.Equal(OperationStatus.Completed, job.Status);
            Assert.Equal(5, job.Count);
            Assert.Equal("Appointment,AppointmentResponse", job.ResourceList);
            Assert.Equal("appointment", job.SearchParamList);
            Assert.Collection<ReindexJobQueryStatus>(
                job.QueryList,
                item => Assert.True(item.ContinuationToken == null && item.Status == OperationStatus.Completed),
                item2 => Assert.True(item2.ContinuationToken == "token" && item2.Status == OperationStatus.Completed));

            param.IsSearchable = true;
        }

        [Fact]
        public async Task GivenNoSupportedParams_WhenExecuted_ThenJobCanceled()
        {
            var job = new ReindexJobRecord(null, 1, null);

            await _reindexJobTask.ExecuteAsync(job, _weakETag, _cancellationToken);

            Assert.Equal(OperationStatus.Canceled, job.Status);
            await _searchService.DidNotReceiveWithAnyArgs().SearchForReindexAsync(default, default, default, default);
        }

        private SearchResult CreateSearchResult(string continuationToken = null)
        {
            var resultList = new List<SearchResultEntry>();
            var wrapper = Substitute.For<ResourceWrapper>();
            var entry = new SearchResultEntry(wrapper);
            resultList.Add(entry);
            var result = new SearchResult(resultList, new List<Tuple<string, string>>(), new List<(string, string)>(), continuationToken);

            return result;
        }
    }
}
