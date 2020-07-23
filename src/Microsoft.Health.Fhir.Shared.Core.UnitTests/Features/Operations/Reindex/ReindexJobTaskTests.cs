// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Internal;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Search;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Reindex
{
    public class ReindexJobTaskTests
    {
        private const string PatientFileName = "Patient.ndjson";
        private const string ObservationFileName = "Observation.ndjson";
        private static readonly WeakETag _weakETag = WeakETag.FromVersionId("0");

        private ReindexJobRecord _reindexJobRecord;

        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
        private readonly IFhirDataStore _fhirDataStore = Substitute.For<IFhirDataStore>();
        private readonly ReindexJobConfiguration _reindexJobConfiguration = new ReindexJobConfiguration();
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();

        private readonly ReindexJobTask _reindexJobTask;

        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly CancellationToken _cancellationToken;

        public ReindexJobTaskTests()
        {
            _cancellationToken = _cancellationTokenSource.Token;
            SetupReindexJobRecord();

            _fhirOperationDataStore.UpdateReindexJobAsync(_reindexJobRecord, _weakETag, _cancellationToken).ReturnsForAnyArgs(new ReindexJobWrapper(_reindexJobRecord, _weakETag));

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
                NullLogger<ReindexJobTask>.Instance);
        }

        [Fact]
        public async Task GivenSupportedParams_WhenExecuted_ThenCorrectSearchIsPerformed()
        {
            // Add one parameter that needs to be indexed
            var param = SearchParameterFixtureData.SearchDefinitionManager.AllSearchParameters.Where(p => p.Name == "status").FirstOrDefault();
            param.IsSearchable = false;

            // setup search result
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>()).
                Returns(CreateSearchResult());

            await _reindexJobTask.ExecuteAsync(_reindexJobRecord, _weakETag, _cancellationToken);

            // verify search for count
            await _searchService.Received().SearchForReindexAsync(Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>());

            // verify search for results
            await _searchService.Received().SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Where(t => t.Item1 == "_type" && t.Item2 == "Account").Any()),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>());

            Assert.Equal(OperationStatus.Completed, _reindexJobRecord.Status);
            Assert.Equal(5, _reindexJobRecord.Count);
            Assert.Equal("Account", _reindexJobRecord.ResourceList);
            Assert.Equal("status", _reindexJobRecord.SearchParamList);
            Assert.Collection<ReindexJobQueryStatus>(
                _reindexJobRecord.QueryList,
                item => Assert.True(item.ContinuationToken == null && item.Status == OperationStatus.Completed));
        }

        [Fact]
        public async Task GivenContinuationToken_WhenExecuted_ThenAdditionalQueryAdded()
        {
            // Add one parameter that needs to be indexed
            var param = SearchParameterFixtureData.SearchDefinitionManager.AllSearchParameters.Where(p => p.Name == "identifier").FirstOrDefault();
            param.IsSearchable = false;

            // setup search result
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>()).
                Returns(CreateSearchResult("token"));

            await _reindexJobTask.ExecuteAsync(_reindexJobRecord, _weakETag, _cancellationToken);

            // verify search for count
            await _searchService.Received().SearchForReindexAsync(Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>());

            // verify search for results
            await _searchService.Received().SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Where(t => t.Item1 == "_type" && t.Item2 == "Account").Any()),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>());

            Assert.Equal(OperationStatus.Running, _reindexJobRecord.Status);
            Assert.Equal(5, _reindexJobRecord.Count);
            Assert.Equal("Account", _reindexJobRecord.ResourceList);
            Assert.Equal("identifier", _reindexJobRecord.SearchParamList);
            Assert.Collection<ReindexJobQueryStatus>(
                _reindexJobRecord.QueryList,
                item => Assert.True(item.ContinuationToken == null && item.Status == OperationStatus.Completed),
                item2 => Assert.True(item2.ContinuationToken == "token" && item2.Status == OperationStatus.Queued));
        }

        [Fact]
        public async Task GivenRunningJob_WhenExecuted_ThenQueuedQueryCompleted()
        {
            // Add one parameter that needs to be indexed
            var param = SearchParameterFixtureData.SearchDefinitionManager.AllSearchParameters.Where(p => p.Name == "appointment").FirstOrDefault();
            param.IsSearchable = false;

            // setup search result
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>()).
                Returns(CreateSearchResult("token"));

            await _reindexJobTask.ExecuteAsync(_reindexJobRecord, _weakETag, _cancellationToken);

            // verify search for count
            await _searchService.Received().SearchForReindexAsync(Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>());

            // verify search for results
            await _searchService.Received().SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Where(t => t.Item1 == "_type" && t.Item2 == "Appointment,AppointmentResponse").Any()),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>());

            Assert.Equal(OperationStatus.Running, _reindexJobRecord.Status);
            Assert.Equal(5, _reindexJobRecord.Count);
            Assert.Equal("Appointment,AppointmentResponse", _reindexJobRecord.ResourceList);
            Assert.Equal("appointment", _reindexJobRecord.SearchParamList);
            Assert.Collection<ReindexJobQueryStatus>(
                _reindexJobRecord.QueryList,
                item => Assert.True(item.ContinuationToken == null && item.Status == OperationStatus.Completed),
                item2 => Assert.True(item2.ContinuationToken == "token" && item2.Status == OperationStatus.Queued));

            // setup search result
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>()).
                Returns(CreateSearchResult(null));

            await _reindexJobTask.ExecuteAsync(_reindexJobRecord, _weakETag, _cancellationToken);

            Assert.Equal(OperationStatus.Completed, _reindexJobRecord.Status);
            Assert.Equal(5, _reindexJobRecord.Count);
            Assert.Equal("Appointment,AppointmentResponse", _reindexJobRecord.ResourceList);
            Assert.Equal("appointment", _reindexJobRecord.SearchParamList);
            Assert.Collection<ReindexJobQueryStatus>(
                _reindexJobRecord.QueryList,
                item => Assert.True(item.ContinuationToken == null && item.Status == OperationStatus.Completed),
                item2 => Assert.True(item2.ContinuationToken == "token" && item2.Status == OperationStatus.Completed));
        }

        [Fact]
        public async Task GivenNoSupportedParams_WhenExecuted_ThenJobCanceled()
        {
            SetupReindexJobRecord();
            await _reindexJobTask.ExecuteAsync(_reindexJobRecord, _weakETag, _cancellationToken);

            Assert.Equal(OperationStatus.Canceled, _reindexJobRecord.Status);
            await _searchService.DidNotReceiveWithAnyArgs().SearchForReindexAsync(default, default, default, default);
        }

        private void SetupReindexJobRecord(ReindexJobRecord reindexJob = null)
        {
            _reindexJobRecord = reindexJob ?? new ReindexJobRecord("HashValue", 1, null);
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
