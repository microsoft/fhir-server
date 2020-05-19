// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Internal;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.Extensions;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    public class ExportJobTaskTests
    {
        private const string PatientFileName = "Patient.ndjson";
        private static readonly WeakETag _weakETag = WeakETag.FromVersionId("0");

        private ExportJobRecord _exportJobRecord;
        private InMemoryExportDestinationClient _inMemoryDestinationClient = new InMemoryExportDestinationClient();

        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
        private readonly ExportJobConfiguration _exportJobConfiguration = new ExportJobConfiguration();
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly IResourceToByteArraySerializer _resourceToByteArraySerializer = Substitute.For<IResourceToByteArraySerializer>();

        private readonly ExportJobTask _exportJobTask;

        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly CancellationToken _cancellationToken;

        private ExportJobOutcome _lastExportJobOutcome;

        public ExportJobTaskTests()
        {
            _cancellationToken = _cancellationTokenSource.Token;
            SetupExportJobRecordAndOperationDataStore();

            _resourceToByteArraySerializer.Serialize(Arg.Any<ResourceWrapper>()).Returns(x => Encoding.UTF8.GetBytes(x.ArgAt<ResourceWrapper>(0).ResourceId));

            _exportJobTask = new ExportJobTask(
                () => _fhirOperationDataStore.CreateMockScope(),
                Options.Create(_exportJobConfiguration),
                () => _searchService.CreateMockScope(),
                _resourceToByteArraySerializer,
                _inMemoryDestinationClient,
                NullLogger<ExportJobTask>.Instance);
        }

        [Fact]
        public async Task GivenAJob_WhenExecuted_ThenCorrectSearchIsPerformed()
        {
            bool capturedSearch = false;

            _exportJobConfiguration.MaximumNumberOfResourcesPerQuery = 1;

            // First search should not have continuation token in the list of query parameters.
            _searchService.SearchAsync(
                _exportJobRecord.ResourceType,
                Arg.Is(CreateQueryParametersExpression()),
                _cancellationToken)
                .Returns(x =>
                {
                    capturedSearch = true;

                    return CreateSearchResult();
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            Assert.True(capturedSearch);
        }

        [Fact]
        public async Task GivenAJobWithSinceParameter_WhenExecuted_ThenCorrectSearchIsPerformed()
        {
            bool capturedSearch = false;

            _exportJobConfiguration.MaximumNumberOfResourcesPerQuery = 1;
            var exportJobRecordWithSince = new ExportJobRecord(
               new Uri("https://localhost/ExportJob/"),
               "Patient",
               "hash",
               since: Core.Models.PartialDateTime.MinValue);

            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithSince);

            // First search should not have continuation token in the list of query parameters.
            _searchService.SearchAsync(
                _exportJobRecord.ResourceType,
                Arg.Is(CreateQueryParametersExpression(_exportJobRecord.Since)),
                _cancellationToken)
                .Returns(x =>
                {
                    capturedSearch = true;

                    return CreateSearchResult();
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            Assert.True(capturedSearch);
        }

        [Fact]
        public async Task GivenThereAreTwoPagesOfSearchResults_WhenExecuted_ThenCorrectSearchIsPerformed()
        {
            const string continuationToken = "ct";

            _exportJobConfiguration.MaximumNumberOfResourcesPerQuery = 1;

            // First search returns a search result with continuation token.
            _searchService.SearchAsync(
                _exportJobRecord.ResourceType,
                Arg.Is(CreateQueryParametersExpression()),
                _cancellationToken)
                .Returns(CreateSearchResult(continuationToken: continuationToken));

            bool capturedSearch = false;

            // Second search returns a search result without continuation token.
            _searchService.SearchAsync(
                _exportJobRecord.ResourceType,
                Arg.Is(CreateQueryParametersExpressionWithContinuationToken(continuationToken)),
                _cancellationToken)
                .Returns(x =>
                {
                    capturedSearch = true;

                    return CreateSearchResult();
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            Assert.True(capturedSearch);
        }

        [Fact]
        public async Task GivenThereAreTwoPagesOfSearchResultsWithSinceParameter_WhenExecuted_ThenCorrectSearchIsPerformed()
        {
            const string continuationToken = "ct";

            _exportJobConfiguration.MaximumNumberOfResourcesPerQuery = 1;
            var exportJobRecordWithSince = new ExportJobRecord(
               new Uri("https://localhost/ExportJob/"),
               "Patient",
               "hash",
               since: PartialDateTime.MinValue);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithSince);

            // First search returns a search result with continuation token.
            _searchService.SearchAsync(
                _exportJobRecord.ResourceType,
                Arg.Is(CreateQueryParametersExpression(_exportJobRecord.Since)),
                _cancellationToken)
                .Returns(CreateSearchResult(continuationToken: continuationToken));

            bool capturedSearch = false;

            // Second search returns a search result without continuation token.
            _searchService.SearchAsync(
                _exportJobRecord.ResourceType,
                Arg.Is(CreateQueryParametersExpressionWithContinuationToken(continuationToken, _exportJobRecord.Since)),
                _cancellationToken)
                .Returns(x =>
                {
                    capturedSearch = true;

                    return CreateSearchResult();
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            Assert.True(capturedSearch);
        }

        [Fact]
        public async Task GivenThereAreMultiplePagesOfSearchResults_WhenExecuted_ThenCorrectSearchIsPerformed()
        {
            const string continuationToken = "ct";

            _exportJobConfiguration.MaximumNumberOfResourcesPerQuery = 1;

            // First search returns a search result with continuation token.
            _searchService.SearchAsync(
                _exportJobRecord.ResourceType,
                Arg.Is(CreateQueryParametersExpression()),
                _cancellationToken)
                .Returns(CreateSearchResult(continuationToken: continuationToken));

            bool firstCapturedSearch = false;
            string newContinuationToken = "newCt";

            // Second search returns a search result with continuation token.
            _searchService.SearchAsync(
                _exportJobRecord.ResourceType,
                Arg.Is(CreateQueryParametersExpressionWithContinuationToken(continuationToken)),
                _cancellationToken)
                .Returns(x =>
                {
                    firstCapturedSearch = true;

                    return CreateSearchResult(continuationToken: newContinuationToken);
                });

            bool secondCapturedSearch = false;

            // Third search returns a search result without continuation token.
            _searchService.SearchAsync(
                _exportJobRecord.ResourceType,
                Arg.Is(CreateQueryParametersExpressionWithContinuationToken(newContinuationToken)),
                _cancellationToken)
                .Returns(x =>
                {
                    secondCapturedSearch = true;

                    return CreateSearchResult();
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            Assert.True(firstCapturedSearch);
            Assert.True(secondCapturedSearch);
        }

        [Fact]
        public async Task GivenThereAreMultiplePagesOfSearchResultsWithSinceParameter_WhenExecuted_ThenCorrectSearchIsPerformed()
        {
            const string continuationToken = "ct";

            _exportJobConfiguration.MaximumNumberOfResourcesPerQuery = 1;
            var exportJobRecordWithSince = new ExportJobRecord(
               new Uri("https://localhost/ExportJob/"),
               "Patient",
               "hash",
               since: PartialDateTime.MinValue);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithSince);

            // First search returns a search result with continuation token.
            _searchService.SearchAsync(
                _exportJobRecord.ResourceType,
                Arg.Is(CreateQueryParametersExpression(_exportJobRecord.Since)),
                _cancellationToken)
                .Returns(CreateSearchResult(continuationToken: continuationToken));

            bool firstCapturedSearch = false;
            string newContinuationToken = "newCt";

            // Second search returns a search result with continuation token.
            _searchService.SearchAsync(
                _exportJobRecord.ResourceType,
                Arg.Is(CreateQueryParametersExpressionWithContinuationToken(continuationToken, _exportJobRecord.Since)),
                _cancellationToken)
                .Returns(x =>
                {
                    firstCapturedSearch = true;

                    return CreateSearchResult(continuationToken: newContinuationToken);
                });

            bool secondCapturedSearch = false;

            // Third search returns a search result without continuation token.
            _searchService.SearchAsync(
                _exportJobRecord.ResourceType,
                Arg.Is(CreateQueryParametersExpressionWithContinuationToken(newContinuationToken, _exportJobRecord.Since)),
                _cancellationToken)
                .Returns(x =>
                {
                    secondCapturedSearch = true;

                    return CreateSearchResult();
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            Assert.True(firstCapturedSearch);
            Assert.True(secondCapturedSearch);
        }

        private Expression<Predicate<IReadOnlyList<Tuple<string, string>>>> CreateQueryParametersExpression()
        {
            return arg => arg != null &&
                Tuple.Create("_count", "1").Equals(arg[0]) &&
                Tuple.Create("_lastUpdated", $"le{_exportJobRecord.QueuedTime.ToString("o")}").Equals(arg[1]);
        }

        private Expression<Predicate<IReadOnlyList<Tuple<string, string>>>> CreateQueryParametersExpression(PartialDateTime since)
        {
            return arg => arg != null &&
                Tuple.Create("_count", "1").Equals(arg[0]) &&
                Tuple.Create("_lastUpdated", $"le{_exportJobRecord.QueuedTime.ToString("o")}").Equals(arg[1]) &&
                Tuple.Create("_lastUpdated", $"ge{since}").Equals(arg[2]);
        }

        private Expression<Predicate<IReadOnlyList<Tuple<string, string>>>> CreateQueryParametersExpressionWithContinuationToken(string continuationToken)
        {
            return arg => arg != null &&
                Tuple.Create("_count", "1").Equals(arg[0]) &&
                Tuple.Create("_lastUpdated", $"le{_exportJobRecord.QueuedTime.ToString("o")}").Equals(arg[1]) &&
                Tuple.Create("ct", continuationToken).Equals(arg[2]);
        }

        private Expression<Predicate<IReadOnlyList<Tuple<string, string>>>> CreateQueryParametersExpressionWithContinuationToken(string continuationToken, PartialDateTime since)
        {
            return arg => arg != null &&
                Tuple.Create("_count", "1").Equals(arg[0]) &&
                Tuple.Create("_lastUpdated", $"le{_exportJobRecord.QueuedTime.ToString("o")}").Equals(arg[1]) &&
                Tuple.Create("_lastUpdated", $"ge{since}").Equals(arg[2]) &&
                Tuple.Create("ct", continuationToken).Equals(arg[3]);
        }

        [Fact]
        public async Task GivenSearchSucceeds_WhenExecuted_ThenJobStatusShouldBeUpdatedToCompleted()
        {
            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x => CreateSearchResult());

            DateTimeOffset endTimestamp = DateTimeOffset.UtcNow;

            using (Mock.Property(() => ClockResolver.UtcNowFunc, () => endTimestamp))
            {
                await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);
            }

            Assert.NotNull(_lastExportJobOutcome);
            Assert.Equal(OperationStatus.Completed, _lastExportJobOutcome.JobRecord.Status);
            Assert.Equal(endTimestamp, _lastExportJobOutcome.JobRecord.EndTime);
        }

        [Fact]
        public async Task GivenSearchFailed_WhenExecuted_ThenJobStatusShouldBeUpdatedToFailed()
        {
            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns<SearchResult>(x =>
                {
                    throw new Exception();
                });

            DateTimeOffset endTimestamp = DateTimeOffset.UtcNow;

            using (Mock.Property(() => ClockResolver.UtcNowFunc, () => endTimestamp))
            {
                await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);
            }

            Assert.NotNull(_lastExportJobOutcome);
            Assert.Equal(OperationStatus.Failed, _lastExportJobOutcome.JobRecord.Status);
            Assert.Equal(endTimestamp, _lastExportJobOutcome.JobRecord.EndTime);
            Assert.False(string.IsNullOrWhiteSpace(_lastExportJobOutcome.JobRecord.FailureDetails.FailureReason));
        }

        [Theory]
        [InlineData(0, null)] // Because it fails to perform the 1st search, the file will not be created.
        [InlineData(1, "")] // Because it fails to perform the 2nd search, the file is created but nothing is committed.
        [InlineData(2, "")] // Because it fails to perform the 3rd search, the file is created but nothing is committed.
        [InlineData(3, "012")] // Because it fails to perform the 4th search, the file is created and the first 3 pages are committed.
        [InlineData(4, "012")] // Because it fails to perform the 5th search, the file is created and the first 3 pages are committed.
        [InlineData(5, "012")] // Because it fails to perform the 6th search, the file is created and the first 3 pages are committed.
        [InlineData(6, "012345")] // Because it fails to perform the 7th search, the file is created and the first 6 pages are committed.
        public async Task GivenVariousNumberOfSuccessfulSearch_WhenExecuted_ThenItShouldCommitAtScheduledPage(int numberOfSuccessfulPages, string expectedIds)
        {
            _exportJobConfiguration.NumberOfPagesPerCommit = 3;

            int numberOfCalls = 0;

            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    int count = numberOfCalls++;

                    if (count == numberOfSuccessfulPages)
                    {
                        throw new Exception();
                    }

                    return CreateSearchResult(
                        new[]
                        {
                            new SearchResultEntry(
                                new ResourceWrapper(
                                    count.ToString(CultureInfo.InvariantCulture),
                                    "1",
                                    "Patient",
                                    new RawResource("data", Core.Models.FhirResourceFormat.Json),
                                    null,
                                    DateTimeOffset.MinValue,
                                    false,
                                    null,
                                    null,
                                    null)),
                        },
                        continuationToken: "ct");
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string actualIds = _inMemoryDestinationClient.GetExportedData(new Uri(PatientFileName, UriKind.Relative));

            Assert.Equal(expectedIds, actualIds);
        }

        [Fact]
        public async Task GivenNumberOfSearch_WhenExecuted_ThenItShouldCommitOneLastTime()
        {
            _exportJobConfiguration.NumberOfPagesPerCommit = 3;

            SearchResult searchResultWithContinuationToken = CreateSearchResult(continuationToken: "ct");

            int numberOfCalls = 0;

            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    int count = numberOfCalls++;

                    if (count == 5)
                    {
                        return CreateSearchResult();
                    }

                    return CreateSearchResult(
                        new[]
                        {
                            new SearchResultEntry(
                                new ResourceWrapper(
                                    count.ToString(CultureInfo.InvariantCulture),
                                    "1",
                                    "Patient",
                                    new RawResource("data", Core.Models.FhirResourceFormat.Json),
                                    null,
                                    DateTimeOffset.MinValue,
                                    false,
                                    null,
                                    null,
                                    null)),
                        },
                        continuationToken: "ct");
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string actualIds = _inMemoryDestinationClient.GetExportedData(new Uri(PatientFileName, UriKind.Relative));

            // All of the ids should be present since it should have committed one last time after all the results were exported.
            Assert.Equal("01234", actualIds);
        }

        [Fact]
        public async Task GivenConnectingToDestinationFails_WhenExecuted_ThenJobStatusShouldBeUpdatedToFailed()
        {
            // Setup export destination client.
            string connectionFailure = "failedToConnectToDestination";
            IExportDestinationClient mockExportDestinationClient = Substitute.For<IExportDestinationClient>();
            mockExportDestinationClient.ConnectAsync(Arg.Any<CancellationToken>(), Arg.Any<string>())
                .Returns<Task>(x => throw new DestinationConnectionException(connectionFailure, HttpStatusCode.BadRequest));

            var exportJobTask = new ExportJobTask(
                () => _fhirOperationDataStore.CreateMockScope(),
                Options.Create(_exportJobConfiguration),
                () => _searchService.CreateMockScope(),
                _resourceToByteArraySerializer,
                mockExportDestinationClient,
                NullLogger<ExportJobTask>.Instance);

            await exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            Assert.NotNull(_lastExportJobOutcome);
            Assert.Equal(OperationStatus.Failed, _lastExportJobOutcome.JobRecord.Status);
            Assert.Equal(connectionFailure, _lastExportJobOutcome.JobRecord.FailureDetails.FailureReason);
            Assert.Equal(HttpStatusCode.BadRequest, _lastExportJobOutcome.JobRecord.FailureDetails.FailureStatusCode);
        }

        [Fact]
        public async Task GivenAnExportJobToResume_WhenExecuted_ThenItShouldExportAllRecordsAsExpected()
        {
            // We are using the SearchService to throw an exception in order to simulate the export job task
            // "crashing" while in the middle of the process.
            _exportJobConfiguration.NumberOfPagesPerCommit = 2;

            int numberOfCalls = 0;
            int numberOfSuccessfulPages = 2;

            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    int count = numberOfCalls;

                    if (count == numberOfSuccessfulPages)
                    {
                        throw new Exception();
                    }

                    numberOfCalls++;
                    return CreateSearchResult(
                        new[]
                        {
                            new SearchResultEntry(
                                new ResourceWrapper(
                                    count.ToString(CultureInfo.InvariantCulture),
                                    "1",
                                    "Patient",
                                    new RawResource("data", Core.Models.FhirResourceFormat.Json),
                                    null,
                                    DateTimeOffset.MinValue,
                                    false,
                                    null,
                                    null,
                                    null)),
                        },
                        continuationToken: "ct");
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(PatientFileName, UriKind.Relative));

            Assert.Equal("01", exportedIds);
            Assert.NotNull(_exportJobRecord.Progress);

            // We create a new export job task here to simulate the worker picking up the "old" export job record
            // and resuming the export process. The export destination client contains data that has
            // been committed up until the "crash".
            _inMemoryDestinationClient = new InMemoryExportDestinationClient();

            var secondExportJobTask = new ExportJobTask(
                () => _fhirOperationDataStore.CreateMockScope(),
                Options.Create(_exportJobConfiguration),
                () => _searchService.CreateMockScope(),
                _resourceToByteArraySerializer,
                _inMemoryDestinationClient,
                NullLogger<ExportJobTask>.Instance);

            numberOfSuccessfulPages = 5;
            await secondExportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(PatientFileName, UriKind.Relative));
            Assert.Equal("23", exportedIds);
        }

        private SearchResult CreateSearchResult(IEnumerable<SearchResultEntry> resourceWrappers = null, string continuationToken = null)
        {
            if (resourceWrappers == null)
            {
                resourceWrappers = Array.Empty<SearchResultEntry>();
            }

            return new SearchResult(resourceWrappers, new Tuple<string, string>[0], Array.Empty<(string parameterName, string reason)>(), continuationToken);
        }

        private void SetupExportJobRecordAndOperationDataStore(ExportJobRecord exportJobRecord = null)
        {
            _exportJobRecord = exportJobRecord ?? new ExportJobRecord(
                new Uri("https://localhost/ExportJob/"),
                "Patient",
                "hash");

            _fhirOperationDataStore.UpdateExportJobAsync(_exportJobRecord, _weakETag, _cancellationToken).Returns(x =>
            {
                _lastExportJobOutcome = new ExportJobOutcome(_exportJobRecord, _weakETag);

                return _lastExportJobOutcome;
            });
        }
    }
}
