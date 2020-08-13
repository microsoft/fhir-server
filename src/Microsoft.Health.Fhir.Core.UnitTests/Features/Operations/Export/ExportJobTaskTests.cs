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
using Hl7.Fhir.ElementModel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Internal;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    public class ExportJobTaskTests
    {
        private const string PatientFileName = "Patient.ndjson";
        private const string ObservationFileName = "Observation.ndjson";
        private static readonly WeakETag _weakETag = WeakETag.FromVersionId("0");

        private ExportJobRecord _exportJobRecord;
        private InMemoryExportDestinationClient _inMemoryDestinationClient = new InMemoryExportDestinationClient();

        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
        private readonly ExportJobConfiguration _exportJobConfiguration = new ExportJobConfiguration();
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly IResourceToByteArraySerializer _resourceToByteArraySerializer = Substitute.For<IResourceToByteArraySerializer>();
        private readonly IResourceDeserializer _resourceDeserializer = Substitute.For<IResourceDeserializer>();
        private readonly IGroupMemberExtractor _groupMemberExtractor = Substitute.For<IGroupMemberExtractor>();

        private readonly ExportJobTask _exportJobTask;

        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly CancellationToken _cancellationToken;

        private ExportJobOutcome _lastExportJobOutcome;

        public ExportJobTaskTests()
        {
            _cancellationToken = _cancellationTokenSource.Token;
            SetupExportJobRecordAndOperationDataStore();

            _resourceToByteArraySerializer.Serialize(Arg.Any<ResourceElement>()).Returns(x => Encoding.UTF8.GetBytes(x.ArgAt<ResourceElement>(0).Instance.Value.ToString()));
            _resourceDeserializer.Deserialize(Arg.Any<ResourceWrapper>()).Returns(x => new ResourceElement(ElementNode.FromElement(ElementNode.ForPrimitive(x.ArgAt<ResourceWrapper>(0).ResourceId))));

            _exportJobTask = new ExportJobTask(
                () => _fhirOperationDataStore.CreateMockScope(),
                Options.Create(_exportJobConfiguration),
                () => _searchService.CreateMockScope(),
                _groupMemberExtractor,
                _resourceToByteArraySerializer,
                _inMemoryDestinationClient,
                _resourceDeserializer,
                null,
                NullLogger<ExportJobTask>.Instance);
        }

        [Fact]
        public async Task GivenAJob_WhenExecuted_ThenCorrectSearchIsPerformed()
        {
            bool capturedSearch = false;

            var exportJobRecordWithOneResource = CreateExportJobRecord(
                exportJobType: ExportJobType.Patient,
                maximumNumberOfResourcesPerQuery: 1);

            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithOneResource);

            // First search should not have continuation token in the list of query parameters.
            _searchService.SearchAsync(
                null,
                Arg.Is(CreateQueryParametersExpression(KnownResourceTypes.Patient)),
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

            var exportJobRecordWithSince = CreateExportJobRecord(
                exportJobType: ExportJobType.Patient,
                since: PartialDateTime.MinValue,
                maximumNumberOfResourcesPerQuery: 1);

            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithSince);

            // First search should not have continuation token in the list of query parameters.
            _searchService.SearchAsync(
                null,
                Arg.Is(CreateQueryParametersExpression(_exportJobRecord.Since, KnownResourceTypes.Patient)),
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

            var exportJobRecordWithOneResource = CreateExportJobRecord(
                exportJobType: ExportJobType.Patient,
                maximumNumberOfResourcesPerQuery: 1);

            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithOneResource);

            // First search returns a search result with continuation token.
            _searchService.SearchAsync(
                null,
                Arg.Is(CreateQueryParametersExpression(KnownResourceTypes.Patient)),
                _cancellationToken)
                .Returns(CreateSearchResult(continuationToken: continuationToken));

            bool capturedSearch = false;

            // Second search returns a search result without continuation token.
            _searchService.SearchAsync(
                null,
                Arg.Is(CreateQueryParametersExpressionWithContinuationToken(continuationToken, KnownResourceTypes.Patient)),
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

            var exportJobRecordWithSince = CreateExportJobRecord(
                exportJobType: ExportJobType.Patient,
                since: PartialDateTime.MinValue,
                maximumNumberOfResourcesPerQuery: 1);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithSince);

            // First search returns a search result with continuation token.
            _searchService.SearchAsync(
                null,
                Arg.Is(CreateQueryParametersExpression(_exportJobRecord.Since, KnownResourceTypes.Patient)),
                _cancellationToken)
                .Returns(CreateSearchResult(continuationToken: continuationToken));

            bool capturedSearch = false;

            // Second search returns a search result without continuation token.
            _searchService.SearchAsync(
                null,
                Arg.Is(CreateQueryParametersExpressionWithContinuationToken(continuationToken, _exportJobRecord.Since, KnownResourceTypes.Patient)),
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

            var exportJobRecordWithOneResource = CreateExportJobRecord(
                exportJobType: ExportJobType.Patient,
                maximumNumberOfResourcesPerQuery: 1);

            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithOneResource);

            // First search returns a search result with continuation token.
            _searchService.SearchAsync(
                null,
                Arg.Is(CreateQueryParametersExpression(KnownResourceTypes.Patient)),
                _cancellationToken)
                .Returns(CreateSearchResult(continuationToken: continuationToken));

            bool firstCapturedSearch = false;
            string newContinuationToken = "newCt";

            // Second search returns a search result with continuation token.
            _searchService.SearchAsync(
                null,
                Arg.Is(CreateQueryParametersExpressionWithContinuationToken(continuationToken, KnownResourceTypes.Patient)),
                _cancellationToken)
                .Returns(x =>
                {
                    firstCapturedSearch = true;

                    return CreateSearchResult(continuationToken: newContinuationToken);
                });

            bool secondCapturedSearch = false;

            // Third search returns a search result without continuation token.
            _searchService.SearchAsync(
                null,
                Arg.Is(CreateQueryParametersExpressionWithContinuationToken(newContinuationToken, KnownResourceTypes.Patient)),
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

            var exportJobRecordWithSince = CreateExportJobRecord(
                exportJobType: ExportJobType.Patient,
                since: PartialDateTime.MinValue,
                maximumNumberOfResourcesPerQuery: 1);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithSince);

            // First search returns a search result with continuation token.
            _searchService.SearchAsync(
                null,
                Arg.Is(CreateQueryParametersExpression(_exportJobRecord.Since, KnownResourceTypes.Patient)),
                _cancellationToken)
                .Returns(CreateSearchResult(continuationToken: continuationToken));

            bool firstCapturedSearch = false;
            string newContinuationToken = "newCt";

            // Second search returns a search result with continuation token.
            _searchService.SearchAsync(
                null,
                Arg.Is(CreateQueryParametersExpressionWithContinuationToken(continuationToken, _exportJobRecord.Since, KnownResourceTypes.Patient)),
                _cancellationToken)
                .Returns(x =>
                {
                    firstCapturedSearch = true;

                    return CreateSearchResult(continuationToken: newContinuationToken);
                });

            bool secondCapturedSearch = false;

            // Third search returns a search result without continuation token.
            _searchService.SearchAsync(
                null,
                Arg.Is(CreateQueryParametersExpressionWithContinuationToken(newContinuationToken, _exportJobRecord.Since, KnownResourceTypes.Patient)),
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

        private Expression<Predicate<IReadOnlyList<Tuple<string, string>>>> CreateQueryParametersExpression(string resourceType)
        {
            return arg => arg != null &&
                Tuple.Create("_count", "1").Equals(arg[0]) &&
                Tuple.Create("_lastUpdated", $"le{_exportJobRecord.QueuedTime.ToString("o")}").Equals(arg[1]) &&
                Tuple.Create("_type", resourceType).Equals(arg[2]);
        }

        private Expression<Predicate<IReadOnlyList<Tuple<string, string>>>> CreateQueryParametersExpression(PartialDateTime since, string resourceType)
        {
            return arg => arg != null &&
                Tuple.Create("_count", "1").Equals(arg[0]) &&
                Tuple.Create("_lastUpdated", $"le{_exportJobRecord.QueuedTime.ToString("o")}").Equals(arg[1]) &&
                Tuple.Create("_lastUpdated", $"ge{since}").Equals(arg[2]) &&
                Tuple.Create("_type", resourceType).Equals(arg[3]);
        }

        private Expression<Predicate<IReadOnlyList<Tuple<string, string>>>> CreateQueryParametersExpressionWithContinuationToken(string continuationToken, string resourceType)
        {
            return arg => arg != null &&
                Tuple.Create("_count", "1").Equals(arg[0]) &&
                Tuple.Create("_lastUpdated", $"le{_exportJobRecord.QueuedTime.ToString("o")}").Equals(arg[1]) &&
                Tuple.Create("_type", resourceType).Equals(arg[2]) &&
                Tuple.Create("ct", continuationToken).Equals(arg[3]);
        }

        private Expression<Predicate<IReadOnlyList<Tuple<string, string>>>> CreateQueryParametersExpressionWithContinuationToken(string continuationToken, PartialDateTime since, string resourceType)
        {
            return arg => arg != null &&
                Tuple.Create("_count", "1").Equals(arg[0]) &&
                Tuple.Create("_lastUpdated", $"le{_exportJobRecord.QueuedTime.ToString("o")}").Equals(arg[1]) &&
                Tuple.Create("_lastUpdated", $"ge{since}").Equals(arg[2]) &&
                Tuple.Create("_type", resourceType).Equals(arg[3]) &&
                Tuple.Create("ct", continuationToken).Equals(arg[4]);
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
            var exportJobRecordWithCommitPages = CreateExportJobRecord(
                numberOfPagesPerCommit: 3);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithCommitPages);

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
                            CreateSearchResultEntry(count.ToString(CultureInfo.InvariantCulture), "Patient"),
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
            var exportJobRecordWithCommitPages = CreateExportJobRecord(
                 numberOfPagesPerCommit: 2);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithCommitPages);

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
                            CreateSearchResultEntry(count.ToString(CultureInfo.InvariantCulture), "Patient"),
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
            mockExportDestinationClient.ConnectAsync(Arg.Any<ExportJobConfiguration>(), Arg.Any<CancellationToken>(), Arg.Any<string>())
                .Returns<Task>(x => throw new DestinationConnectionException(connectionFailure, HttpStatusCode.BadRequest));

            var exportJobTask = new ExportJobTask(
                () => _fhirOperationDataStore.CreateMockScope(),
                Options.Create(_exportJobConfiguration),
                () => _searchService.CreateMockScope(),
                _groupMemberExtractor,
                _resourceToByteArraySerializer,
                mockExportDestinationClient,
                _resourceDeserializer,
                null,
                NullLogger<ExportJobTask>.Instance);

            await exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            Assert.NotNull(_lastExportJobOutcome);
            Assert.Equal(OperationStatus.Failed, _lastExportJobOutcome.JobRecord.Status);
            Assert.Equal(connectionFailure, _lastExportJobOutcome.JobRecord.FailureDetails.FailureReason);
            Assert.Equal(HttpStatusCode.BadRequest, _lastExportJobOutcome.JobRecord.FailureDetails.FailureStatusCode);
        }

        [Fact]
        public async Task GivenStorageAccountConnectionDidNotChange_WhenExecuted_ThenJobShouldBeCompleted()
        {
            ExportJobConfiguration exportJobConfiguration = new ExportJobConfiguration();
            exportJobConfiguration.StorageAccountConnection = "connection";
            exportJobConfiguration.StorageAccountUri = string.Empty;

            var exportJobRecordWithConnection = CreateExportJobRecord(
                exportJobType: ExportJobType.Patient,
                storageAccountConnectionHash: Microsoft.Health.Core.Extensions.StringExtensions.ComputeHash(exportJobConfiguration.StorageAccountConnection));
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithConnection);

            var exportJobTask = new ExportJobTask(
                () => _fhirOperationDataStore.CreateMockScope(),
                Options.Create(exportJobConfiguration),
                () => _searchService.CreateMockScope(),
                _groupMemberExtractor,
                _resourceToByteArraySerializer,
                _inMemoryDestinationClient,
                _resourceDeserializer,
                null,
                NullLogger<ExportJobTask>.Instance);

            _searchService.SearchAsync(
               Arg.Any<string>(),
               Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
               _cancellationToken)
               .Returns(x => CreateSearchResult());

            DateTimeOffset endTimestamp = DateTimeOffset.UtcNow;

            using (Mock.Property(() => ClockResolver.UtcNowFunc, () => endTimestamp))
            {
                await exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);
            }

            Assert.NotNull(_lastExportJobOutcome);
            Assert.Equal(OperationStatus.Completed, _lastExportJobOutcome.JobRecord.Status);
            Assert.Equal(endTimestamp, _lastExportJobOutcome.JobRecord.EndTime);
        }

        [Fact]
        public async Task GivenStorageAccountConnectionChanged_WhenExecuted_ThenJobStatusShouldBeUpdatedToFailed()
        {
            string connectionFailure = "Storage account connection string was updated during an export job.";

            var exportJobRecordWithChangedConnection = CreateExportJobRecord(
                exportJobType: ExportJobType.Patient,
                storageAccountConnectionHash: Microsoft.Health.Core.Extensions.StringExtensions.ComputeHash("different connection"));
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithChangedConnection);

            var exportJobTask = new ExportJobTask(
                () => _fhirOperationDataStore.CreateMockScope(),
                Options.Create(_exportJobConfiguration),
                () => _searchService.CreateMockScope(),
                _groupMemberExtractor,
                _resourceToByteArraySerializer,
                _inMemoryDestinationClient,
                _resourceDeserializer,
                null,
                NullLogger<ExportJobTask>.Instance);

            await exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            Assert.NotNull(_lastExportJobOutcome);
            Assert.Equal(OperationStatus.Failed, _lastExportJobOutcome.JobRecord.Status);
            Assert.Equal(connectionFailure, _lastExportJobOutcome.JobRecord.FailureDetails.FailureReason);
            Assert.Equal(HttpStatusCode.BadRequest, _lastExportJobOutcome.JobRecord.FailureDetails.FailureStatusCode);
        }

        [Fact]
        public async Task GivenStorageAccountUriChanged_WhenExecuted_ThenRecordsAreSentToOldStorageAccount()
        {
            var exportJobRecordWithChangedConnection = CreateExportJobRecord(
                exportJobType: ExportJobType.Patient,
                storageAccountConnectionHash: Microsoft.Health.Core.Extensions.StringExtensions.ComputeHash(_exportJobConfiguration.StorageAccountConnection),
                storageAccountUri: "origionalUri");
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithChangedConnection);

            ExportJobConfiguration configurationWithUri = new ExportJobConfiguration();
            configurationWithUri.StorageAccountUri = "newUri";

            IExportDestinationClient mockDestinationClient = Substitute.For<IExportDestinationClient>();
            ExportJobConfiguration capturedConfiguration = null;
            mockDestinationClient.ConnectAsync(
                Arg.Do<ExportJobConfiguration>(arg => capturedConfiguration = arg),
                Arg.Any<CancellationToken>(),
                Arg.Any<string>())
                .Returns(x =>
            {
                return Task.CompletedTask;
            });

            var exportJobTask = new ExportJobTask(
                () => _fhirOperationDataStore.CreateMockScope(),
                Options.Create(_exportJobConfiguration),
                () => _searchService.CreateMockScope(),
                _groupMemberExtractor,
                _resourceToByteArraySerializer,
                mockDestinationClient,
                _resourceDeserializer,
                null,
                NullLogger<ExportJobTask>.Instance);

            await exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            Assert.Equal(exportJobRecordWithChangedConnection.StorageAccountUri, capturedConfiguration.StorageAccountUri);
        }

        [Fact]
        public async Task GivenAnExportJobToResume_WhenExecuted_ThenItShouldExportAllRecordsAsExpected()
        {
            // We are using the SearchService to throw an exception in order to simulate the export job task
            // "crashing" while in the middle of the process.
            var exportJobRecordWithCommitPages = CreateExportJobRecord(
                numberOfPagesPerCommit: 2);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithCommitPages);

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
                            CreateSearchResultEntry(count.ToString(CultureInfo.InvariantCulture), "Patient"),
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
                _groupMemberExtractor,
                _resourceToByteArraySerializer,
                _inMemoryDestinationClient,
                _resourceDeserializer,
                null,
                NullLogger<ExportJobTask>.Instance);

            numberOfSuccessfulPages = 5;
            await secondExportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(PatientFileName, UriKind.Relative));
            Assert.Equal("23", exportedIds);
        }

        [Fact]
        public async Task GivenAPatientExportJob_WhenExecuted_ThenAllCompartmentResourcesShouldBeExported()
        {
            int numberOfCalls = 0;
            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    numberOfCalls++;
                    return CreateSearchResult(
                        new[]
                        {
                            CreateSearchResultEntry(numberOfCalls.ToString(CultureInfo.InvariantCulture), "Patient"),
                        },
                        continuationToken: numberOfCalls > 3 ? null : "ct");
                });

            int numberOfCompartmentCalls = 0;
            _searchService.SearchCompartmentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    numberOfCompartmentCalls++;
                    return CreateSearchResult(
                        new[]
                        {
                            CreateSearchResultEntry(numberOfCompartmentCalls.ToString(CultureInfo.InvariantCulture), "Observation"),
                        },
                        continuationToken: numberOfCompartmentCalls % 2 == 0 ? null : "ct");
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            Assert.Equal(4, numberOfCalls);
            Assert.Equal(8, numberOfCompartmentCalls);

            string exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(PatientFileName, UriKind.Relative));
            Assert.Equal("1234", exportedIds);
            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(ObservationFileName, UriKind.Relative));
            Assert.Equal("12345678", exportedIds);

            Assert.Equal(OperationStatus.Completed, _exportJobRecord.Status);
        }

        [Fact]
        public async Task GivenAPatientExportJobWithNoCompartmentResources_WhenExecuted_ThenJustAllPatientResourcesShouldBeExported()
        {
            int numberOfCalls = 0;
            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    numberOfCalls++;
                    return CreateSearchResult(
                        new[]
                        {
                            CreateSearchResultEntry(numberOfCalls.ToString(CultureInfo.InvariantCulture), "Patient"),
                        },
                        continuationToken: numberOfCalls > 3 ? null : "ct");
                });

            int numberOfCompartmentCalls = 0;
            _searchService.SearchCompartmentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    numberOfCompartmentCalls++;
                    return CreateSearchResult();
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            Assert.Equal(4, numberOfCalls);
            Assert.Equal(4, numberOfCompartmentCalls);

            string exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(PatientFileName, UriKind.Relative));
            Assert.Equal("1234", exportedIds);
            Assert.Equal(1, _inMemoryDestinationClient.ExportedDataFileCount);

            Assert.Equal(OperationStatus.Completed, _exportJobRecord.Status);
        }

        [Fact]
        public async Task GivenAPatientExportJobToResumeWithinACompartment_WhenExecuted_ThenItShouldExportAllResources()
        {
            // We are using the SearchService to throw an exception in order to simulate the export job task
            // "crashing" while in the middle of the process.
            var exportJobRecordWithCommitPages = CreateExportJobRecord(
                exportJobType: ExportJobType.Patient,
                numberOfPagesPerCommit: 2);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithCommitPages);

            int numberOfCalls = 0;

            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    numberOfCalls++;
                    return CreateSearchResult(
                        new[]
                        {
                            CreateSearchResultEntry("1", "Patient"),
                            CreateSearchResultEntry("2", "Patient"),
                        });
                });

            int numberOfCompartmentCalls = 0;
            int numberOfSuccessfulCompartmentPages = 5;

            _searchService.SearchCompartmentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    numberOfCompartmentCalls++;
                    if (numberOfCompartmentCalls % numberOfSuccessfulCompartmentPages == 0)
                    {
                        throw new Exception();
                    }

                    return CreateSearchResult(
                        new[]
                        {
                            CreateSearchResultEntry(numberOfCompartmentCalls.ToString(CultureInfo.InvariantCulture), "Observation"),
                        },
                        continuationToken: numberOfCompartmentCalls % 3 == 0 ? null : "ct");
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(PatientFileName, UriKind.Relative));
            Assert.Null(exportedIds);

            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(ObservationFileName, UriKind.Relative));
            Assert.Equal("123", exportedIds);

            Assert.NotNull(_exportJobRecord.Progress);
            Assert.NotNull(_exportJobRecord.Progress.SubSearch);

            // We create a new export job task here to simulate the worker picking up the "old" export job record
            // and resuming the export process. The export destination client contains data that has
            // been committed up until the "crash".
            _inMemoryDestinationClient = new InMemoryExportDestinationClient();
            var secondExportJobTask = new ExportJobTask(
                () => _fhirOperationDataStore.CreateMockScope(),
                Options.Create(_exportJobConfiguration),
                () => _searchService.CreateMockScope(),
                _groupMemberExtractor,
                _resourceToByteArraySerializer,
                _inMemoryDestinationClient,
                _resourceDeserializer,
                null,
                NullLogger<ExportJobTask>.Instance);

            await secondExportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(PatientFileName, UriKind.Relative));
            Assert.Equal("12", exportedIds);

            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(ObservationFileName, UriKind.Relative));

            // 4 was in the commit buffer when the crash happened, and 5 is the one that triggered the crash.
            // Since the 'id' is based on the number of times the mock method has been called these values never get exported.
            Assert.Equal("6", exportedIds);

            Assert.Equal(OperationStatus.Completed, _exportJobRecord.Status);
        }

        [Fact]
        public async Task GivenAPatientExportJobToResumeWithMultiplePagesOfPatientsWithinACompartment_WhenExecuted_ThenItShouldExportAllResources()
        {
            // We are using the SearchService to throw an exception in order to simulate the export job task
            // "crashing" while in the middle of the process.
            var exportJobRecordWithCommitPages = CreateExportJobRecord(
                exportJobType: ExportJobType.Patient,
                numberOfPagesPerCommit: 2);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithCommitPages);

            int numberOfCalls = 0;

            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    numberOfCalls++;
                    return CreateSearchResult(
                        new[]
                        {
                            CreateSearchResultEntry(numberOfCalls.ToString(CultureInfo.InvariantCulture), "Patient"),
                        },
                        continuationToken: numberOfCalls > 1 ? null : "ct");
                });

            int numberOfCompartmentCalls = 0;
            int numberOfSuccessfulCompartmentPages = 5;

            _searchService.SearchCompartmentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    numberOfCompartmentCalls++;
                    if (numberOfCompartmentCalls % numberOfSuccessfulCompartmentPages == 0)
                    {
                        throw new Exception();
                    }

                    return CreateSearchResult(
                        new[]
                        {
                            CreateSearchResultEntry(numberOfCompartmentCalls.ToString(CultureInfo.InvariantCulture), "Observation"),
                        },
                        continuationToken: numberOfCompartmentCalls % 3 == 0 ? null : "ct");
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(PatientFileName, UriKind.Relative));
            Assert.Equal("1", exportedIds);

            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(ObservationFileName, UriKind.Relative));
            Assert.Equal("123", exportedIds);

            Assert.NotNull(_exportJobRecord.Progress);
            Assert.NotNull(_exportJobRecord.Progress.SubSearch);

            // We create a new export job task here to simulate the worker picking up the "old" export job record
            // and resuming the export process. The export destination client contains data that has
            // been committed up until the "crash".
            _inMemoryDestinationClient = new InMemoryExportDestinationClient();

            var secondExportJobTask = new ExportJobTask(
                () => _fhirOperationDataStore.CreateMockScope(),
                Options.Create(_exportJobConfiguration),
                () => _searchService.CreateMockScope(),
                _groupMemberExtractor,
                _resourceToByteArraySerializer,
                _inMemoryDestinationClient,
                _resourceDeserializer,
                null,
                NullLogger<ExportJobTask>.Instance);

            // Reseting the number of calls so that the ressource id of the Patient is the same ('2') as it was when the crash happened.
            numberOfCalls = 1;
            await secondExportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(PatientFileName, UriKind.Relative));

            Assert.Equal("2", exportedIds);

            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(ObservationFileName, UriKind.Relative));

            // 4 was in the commit buffer when the crash happened, and 5 is the one that triggered the crash.
            // Since the 'id' is based on the number of times the mock method has been called these values never get exported.
            Assert.Equal("6", exportedIds);

            Assert.Equal(OperationStatus.Completed, _exportJobRecord.Status);
        }

        [Fact]
        public async Task GivenAnExportJobWithTheTypeParameter_WhenExecuted_ThenOnlyResourcesOfTheGivenTypesAreExported()
        {
            var exportJobRecordWithCommitPages = CreateExportJobRecord(
               resourceType: KnownResourceTypes.Observation + "," + KnownResourceTypes.Encounter,
               since: PartialDateTime.MinValue,
               numberOfPagesPerCommit: 2);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithCommitPages);

            _searchService.SearchAsync(
                null,
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    string[] types = x.ArgAt<IReadOnlyList<Tuple<string, string>>>(1)[3].Item2.Split(',');
                    SearchResultEntry[] entries = new SearchResultEntry[types.Length];

                    for (int index = 0; index < types.Length; index++)
                    {
                        entries[index] = CreateSearchResultEntry(index.ToString(CultureInfo.InvariantCulture), types[index]);
                    }

                    return CreateSearchResult(entries);
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(ObservationFileName, UriKind.Relative));
            Assert.Equal("0", exportedIds);
            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri("Encounter.ndjson", UriKind.Relative));
            Assert.Equal("1", exportedIds);
            Assert.Equal(2, _inMemoryDestinationClient.ExportedDataFileCount);

            Assert.Equal(OperationStatus.Completed, _exportJobRecord.Status);
        }

        [Fact]
        public async Task GivenAPatientExportJobWithTheTypeParameter_WhenExecuted_ThenOnlyCompartmentResourcesOfTheGivenTypesAreExported()
        {
            var exportJobRecordWithCommitPages = CreateExportJobRecord(
                exportJobType: ExportJobType.Patient,
                resourceType: KnownResourceTypes.Observation,
                since: PartialDateTime.MinValue,
                numberOfPagesPerCommit: 2);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithCommitPages);

            _searchService.SearchAsync(
                null,
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    return CreateSearchResult(
                        new[]
                        {
                            CreateSearchResultEntry("1", "Patient"),
                        });
                });

            _searchService.SearchCompartmentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    string[] types = x.ArgAt<IReadOnlyList<Tuple<string, string>>>(3)[3].Item2.Split(',');
                    SearchResultEntry[] entries = new SearchResultEntry[types.Length];

                    for (int index = 0; index < types.Length; index++)
                    {
                        entries[index] = CreateSearchResultEntry(index.ToString(CultureInfo.InvariantCulture), types[index]);
                    }

                    return CreateSearchResult(entries);
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(PatientFileName, UriKind.Relative));
            Assert.Equal("1", exportedIds);
            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(ObservationFileName, UriKind.Relative));
            Assert.Equal("0", exportedIds);
            Assert.Equal(2, _inMemoryDestinationClient.ExportedDataFileCount);

            Assert.Equal(OperationStatus.Completed, _exportJobRecord.Status);
        }

        [Fact]
        public async Task GivenAnExportJobToResumeWithTheTypeParameter_WhenExecuted_ThenOnlyResourcesOfTheGivenTypesAreExported()
        {
            var exportJobRecordWithCommitPages = CreateExportJobRecord(
                resourceType: KnownResourceTypes.Observation + "," + KnownResourceTypes.Encounter,
                numberOfPagesPerCommit: 1);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithCommitPages);

            int searchCallsMade = 0;
            _searchService.SearchAsync(
                null,
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    searchCallsMade++;
                    var queryParameterList = x.ArgAt<IReadOnlyList<Tuple<string, string>>>(1);

                    bool typeParameterIncluded = false;
                    bool continuationTokenParameterIncluded = false;
                    string[] types = null;

                    foreach (Tuple<string, string> parameter in queryParameterList)
                    {
                        if (parameter.Item1 == Core.Features.KnownQueryParameterNames.ContinuationToken)
                        {
                            continuationTokenParameterIncluded = true;
                        }
                        else if (parameter.Item1 == Core.Features.KnownQueryParameterNames.Type)
                        {
                            typeParameterIncluded = true;
                            types = parameter.Item2.Split(',');
                        }
                    }

                    Assert.True(typeParameterIncluded);
                    Assert.Equal(searchCallsMade > 1 ? true : false, continuationTokenParameterIncluded);

                    if (searchCallsMade == 2)
                    {
                        throw new Exception();
                    }

                    SearchResultEntry[] entries = new SearchResultEntry[types.Length];

                    for (int index = 0; index < types.Length; index++)
                    {
                        entries[index] = CreateSearchResultEntry(searchCallsMade.ToString(CultureInfo.InvariantCulture), types[index]);
                    }

                    return CreateSearchResult(entries, searchCallsMade < 4 ? "ct" : null);
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(ObservationFileName, UriKind.Relative));
            Assert.Equal("1", exportedIds);
            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri("Encounter.ndjson", UriKind.Relative));
            Assert.Equal("1", exportedIds);
            Assert.Equal(2, _inMemoryDestinationClient.ExportedDataFileCount);

            // We create a new export job task here to simulate the worker picking up the "old" export job record
            // and resuming the export process. The export destination client contains data that has
            // been committed up until the "crash".
            _inMemoryDestinationClient = new InMemoryExportDestinationClient();

            var secondExportJobTask = new ExportJobTask(
                () => _fhirOperationDataStore.CreateMockScope(),
                Options.Create(_exportJobConfiguration),
                () => _searchService.CreateMockScope(),
                _groupMemberExtractor,
                _resourceToByteArraySerializer,
                _inMemoryDestinationClient,
                _resourceDeserializer,
                null,
                NullLogger<ExportJobTask>.Instance);

            // Reseting the number of calls so that the ressource id of the Patient is the same ('2') as it was when the crash happened.
            await secondExportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(ObservationFileName, UriKind.Relative));
            Assert.Equal("34", exportedIds);
            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri("Encounter.ndjson", UriKind.Relative));
            Assert.Equal("34", exportedIds);
            Assert.Equal(2, _inMemoryDestinationClient.ExportedDataFileCount);

            Assert.Equal(OperationStatus.Completed, _exportJobRecord.Status);
        }

        [Fact]
        public async Task GivenAGroupExportJob_WhenExecuted_ThenAllPatientResourcesInTheGroupAreExported()
        {
            var exportJobRecordWithCommitPages = CreateExportJobRecord(
              exportJobType: ExportJobType.Group,
              groupId: "group",
              numberOfPagesPerCommit: 2);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithCommitPages);

            _groupMemberExtractor.GetGroupPatientIds(
                "group",
                Arg.Any<DateTimeOffset>(),
                _cancellationToken).Returns(
                    new HashSet<string>()
                    {
                        "1",
                        "2",
                    });

            _searchService.SearchAsync(
                null,
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    string[] ids = x.ArgAt<IReadOnlyList<Tuple<string, string>>>(1)[2].Item2.Split(',');
                    SearchResultEntry[] entries = new SearchResultEntry[ids.Length];

                    for (int index = 0; index < ids.Length; index++)
                    {
                        entries[index] = CreateSearchResultEntry(ids[index], KnownResourceTypes.Patient);
                    }

                    return CreateSearchResult(entries);
                });

            _searchService.SearchCompartmentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                 {
                     string parentId = x.ArgAt<string>(1);

                     return CreateSearchResult(new SearchResultEntry[]
                     {
                         CreateSearchResultEntry(parentId, KnownResourceTypes.Observation),
                     });
                 });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(PatientFileName, UriKind.Relative));
            Assert.Equal("12", exportedIds);
            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(ObservationFileName, UriKind.Relative));
            Assert.Equal("12", exportedIds);
            Assert.Equal(2, _inMemoryDestinationClient.ExportedDataFileCount);

            Assert.Equal(OperationStatus.Completed, _exportJobRecord.Status);
        }

        [Fact]
        public async Task GivenAGroupExportJobWithMultiplePagesOfPatients_WhenExecuted_ThenAllPatientResourcesInTheGroupAreExported()
        {
            var exportJobRecordWithCommitPages = CreateExportJobRecord(
              exportJobType: ExportJobType.Group,
              groupId: "group",
              maximumNumberOfResourcesPerQuery: 1,
              numberOfPagesPerCommit: 2);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithCommitPages);

            _groupMemberExtractor.GetGroupPatientIds(
                "group",
                Arg.Any<DateTimeOffset>(),
                _cancellationToken).Returns(
                    new HashSet<string>()
                    {
                        "1",
                        "2",
                        "3",
                    });

            int countOfSearches = 0;
            _searchService.SearchAsync(
                null,
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    string[] ids = x.ArgAt<IReadOnlyList<Tuple<string, string>>>(1)[2].Item2.Split(',');

                    countOfSearches++;

                    return CreateSearchResult(
                        new SearchResultEntry[]
                        {
                            CreateSearchResultEntry(ids[countOfSearches - 1], KnownResourceTypes.Patient),
                        },
                        countOfSearches < 3 ? "ct" : null);
                });

            _searchService.SearchCompartmentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    string parentId = x.ArgAt<string>(1);

                    return CreateSearchResult(new SearchResultEntry[]
                    {
                         CreateSearchResultEntry(parentId, KnownResourceTypes.Observation),
                    });
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(PatientFileName, UriKind.Relative));
            Assert.Equal("123", exportedIds);
            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(ObservationFileName, UriKind.Relative));
            Assert.Equal("123", exportedIds);
            Assert.Equal(2, _inMemoryDestinationClient.ExportedDataFileCount);

            Assert.Equal(OperationStatus.Completed, _exportJobRecord.Status);

            Assert.Equal(3, countOfSearches);
        }

        [Fact]
        public async Task GivenAGroupExportJobToResume_WhenExecuted_ThenAllPatientResourcesInTheGroupAreExported()
        {
            var exportJobRecordWithCommitPages = CreateExportJobRecord(
              exportJobType: ExportJobType.Group,
              groupId: "group",
              maximumNumberOfResourcesPerQuery: 1,
              numberOfPagesPerCommit: 1);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithCommitPages);

            _groupMemberExtractor.GetGroupPatientIds(
                "group",
                Arg.Any<DateTimeOffset>(),
                _cancellationToken).Returns(
                    new HashSet<string>()
                    {
                        "1",
                        "2",
                        "3",
                    });

            int countOfSearches = 0;
            _searchService.SearchAsync(
                null,
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    string[] ids;
                    int continuationTokenIndex;
                    countOfSearches++;

                    if (countOfSearches == 1)
                    {
                        ids = x.ArgAt<IReadOnlyList<Tuple<string, string>>>(1)[2].Item2.Split(',');
                        continuationTokenIndex = 0;
                    }
                    else if (countOfSearches == 2)
                    {
                        throw new Exception();
                    }
                    else
                    {
                        // The ids aren't in the query parameters because of the reset
                        ids = new string[] { "1", "2", "3" };
                        continuationTokenIndex = int.Parse(x.ArgAt<IReadOnlyList<Tuple<string, string>>>(1)[2].Item2.Substring(2));
                    }

                    return CreateSearchResult(
                        new SearchResultEntry[]
                        {
                            CreateSearchResultEntry(ids[continuationTokenIndex], KnownResourceTypes.Patient),
                        },
                        countOfSearches < 4 ? "ct" + (continuationTokenIndex + 1) : null);
                });

            _searchService.SearchCompartmentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    string parentId = x.ArgAt<string>(1);

                    return CreateSearchResult(new SearchResultEntry[]
                    {
                         CreateSearchResultEntry(parentId, KnownResourceTypes.Observation),
                    });
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(PatientFileName, UriKind.Relative));
            Assert.Equal("1", exportedIds);
            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(ObservationFileName, UriKind.Relative));
            Assert.Equal("1", exportedIds);
            Assert.Equal(2, _inMemoryDestinationClient.ExportedDataFileCount);

            // We create a new export job task here to simulate the worker picking up the "old" export job record
            // and resuming the export process. The export destination client contains data that has
            // been committed up until the "crash".
            _inMemoryDestinationClient = new InMemoryExportDestinationClient();

            var secondExportJobTask = new ExportJobTask(
                () => _fhirOperationDataStore.CreateMockScope(),
                Options.Create(_exportJobConfiguration),
                () => _searchService.CreateMockScope(),
                _groupMemberExtractor,
                _resourceToByteArraySerializer,
                _inMemoryDestinationClient,
                _resourceDeserializer,
                null,
                NullLogger<ExportJobTask>.Instance);

            // Reseting the number of calls so that the ressource id of the Patient is the same ('2') as it was when the crash happened.
            await secondExportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(PatientFileName, UriKind.Relative));
            Assert.Equal("23", exportedIds);
            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(ObservationFileName, UriKind.Relative));
            Assert.Equal("23", exportedIds);
            Assert.Equal(2, _inMemoryDestinationClient.ExportedDataFileCount);

            Assert.Equal(OperationStatus.Completed, _exportJobRecord.Status);
        }

        [Fact]
        public async Task GivenAGroupExportJobWithTheTypeParameter_WhenExecuted_ThenAllPatientResourcesInTheGroupAreExported()
        {
            var exportJobRecordWithCommitPages = CreateExportJobRecord(
              exportJobType: ExportJobType.Group,
              resourceType: KnownResourceTypes.Encounter + "," + KnownResourceTypes.Observation,
              groupId: "group",
              numberOfPagesPerCommit: 2);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithCommitPages);

            _groupMemberExtractor.GetGroupPatientIds(
                "group",
                Arg.Any<DateTimeOffset>(),
                _cancellationToken).Returns(
                    new HashSet<string>()
                    {
                        "1",
                        "2",
                        "3",
                    });

            _searchService.SearchAsync(
                null,
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    string[] ids = x.ArgAt<IReadOnlyList<Tuple<string, string>>>(1)[2].Item2.Split(',');
                    SearchResultEntry[] entries = new SearchResultEntry[ids.Length];

                    for (int index = 0; index < ids.Length; index++)
                    {
                        entries[index] = CreateSearchResultEntry(ids[index], KnownResourceTypes.Patient);
                    }

                    return CreateSearchResult(entries);
                });

            _searchService.SearchCompartmentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    string parentId = x.ArgAt<string>(1);
                    string[] resourceTypes = x.ArgAt<IReadOnlyList<Tuple<string, string>>>(3)[2].Item2.Split(',');

                    SearchResultEntry[] entries = new SearchResultEntry[resourceTypes.Length];

                    for (int index = 0; index < resourceTypes.Length; index++)
                    {
                        entries[index] = CreateSearchResultEntry(parentId, resourceTypes[index]);
                    }

                    return CreateSearchResult(entries);
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(PatientFileName, UriKind.Relative));
            Assert.Equal("123", exportedIds);
            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri(ObservationFileName, UriKind.Relative));
            Assert.Equal("123", exportedIds);
            exportedIds = _inMemoryDestinationClient.GetExportedData(new Uri("Encounter.ndjson", UriKind.Relative));
            Assert.Equal("123", exportedIds);
            Assert.Equal(3, _inMemoryDestinationClient.ExportedDataFileCount);

            Assert.Equal(OperationStatus.Completed, _exportJobRecord.Status);
        }

        [Fact]
        public async Task GivenAGroupExportJobWithANonExistantGroup_WhenExecuted_ThenTheJobIsMarkedAsFailed()
        {
            var exportJobRecordWithCommitPages = CreateExportJobRecord(
              exportJobType: ExportJobType.Group,
              resourceType: KnownResourceTypes.Encounter + "," + KnownResourceTypes.Observation,
              groupId: "group",
              numberOfPagesPerCommit: 2);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithCommitPages);

            _groupMemberExtractor.GetGroupPatientIds(
                "group",
                Arg.Any<DateTimeOffset>(),
                _cancellationToken).Returns<HashSet<string>>((x) =>
                {
                    throw new ResourceNotFoundException("test");
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            Assert.Equal(OperationStatus.Failed, _exportJobRecord.Status);
            Assert.Equal(HttpStatusCode.BadRequest, _exportJobRecord.FailureDetails.FailureStatusCode);
            Assert.Equal("test", _exportJobRecord.FailureDetails.FailureReason);
        }

        [Fact]
        public async Task GivenAnonymizedExportJob_WhenExecuted_ThenItShouldExportAllAnonymizedResources()
        {
            bool capturedSearch = false;

            ExportJobRecord exportJobRecordWithOneResource =
                CreateExportJobRecord(maximumNumberOfResourcesPerQuery: 1, numberOfPagesPerCommit: _exportJobConfiguration.NumberOfPagesPerCommit, anonymizationConfigurationLocation: "anonymization-config-file");

            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithOneResource);

            // First search should not have continuation token in the list of query parameters.
            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken)
                .Returns(x =>
                {
                    return CreateSearchResult(new[]
                        {
                            CreateSearchResultEntry("1", "Patient"),
                        });
                });

            IAnonymizer anonymizer = Substitute.For<IAnonymizer>();
            IAnonymizerFactory factory = Substitute.For<IAnonymizerFactory>();

            anonymizer.Anonymize(Arg.Any<ResourceElement>()).Returns(
                _ =>
                {
                    capturedSearch = true;
                    return new ResourceElement(ElementNode.FromElement(ElementNode.ForPrimitive("anonymized-resource")));
                });
            factory.CreateAnonymizerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IAnonymizer>(anonymizer));
            var inMemoryDestinationClient = new InMemoryExportDestinationClient();

            var anonymizedExportJobTask = new ExportJobTask(
                () => _fhirOperationDataStore.CreateMockScope(),
                Options.Create(_exportJobConfiguration),
                () => _searchService.CreateMockScope(),
                _groupMemberExtractor,
                _resourceToByteArraySerializer,
                inMemoryDestinationClient,
                _resourceDeserializer,
                factory.CreateMockScope(),
                NullLogger<ExportJobTask>.Instance);

            await anonymizedExportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string exportedValue = inMemoryDestinationClient.GetExportedData(new Uri(PatientFileName, UriKind.Relative));

            Assert.Equal("anonymized-resource", exportedValue);
            Assert.True(capturedSearch);
        }

        [Theory]
        [InlineData(typeof(AnonymizationConfigurationNotFoundException), "config not found", HttpStatusCode.BadRequest)]
        [InlineData(typeof(FailedToParseAnonymizationConfigurationException), "cannot parse the config", HttpStatusCode.BadRequest)]
        [InlineData(typeof(InvalidOperationException), "Unknown Error.", HttpStatusCode.InternalServerError)]
        public async Task GivenExceptionThrowFromAnonymizerFactory_WhenExecuted_ThenJobStatusShouldBeUpdatedToFailed(Type exceptionType, string expectedErrorMessage, HttpStatusCode expectedHttpStatusCode)
        {
            // Setup export destination client.
            ExportJobRecord exportJobRecordWithOneResource =
                 CreateExportJobRecord(maximumNumberOfResourcesPerQuery: 1, numberOfPagesPerCommit: _exportJobConfiguration.NumberOfPagesPerCommit, anonymizationConfigurationLocation: "anonymization-config-file");

            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithOneResource);
            IAnonymizerFactory factory = Substitute.For<IAnonymizerFactory>();
            factory.CreateAnonymizerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<Task<IAnonymizer>>(_ => throw (Exception)Activator.CreateInstance(exceptionType, expectedErrorMessage));

            var exportJobTask = new ExportJobTask(
                () => _fhirOperationDataStore.CreateMockScope(),
                Options.Create(_exportJobConfiguration),
                () => _searchService.CreateMockScope(),
                _groupMemberExtractor,
                _resourceToByteArraySerializer,
                _inMemoryDestinationClient,
                _resourceDeserializer,
                factory.CreateMockScope(),
                NullLogger<ExportJobTask>.Instance);

            await exportJobTask.ExecuteAsync(exportJobRecordWithOneResource, _weakETag, _cancellationToken);

            Assert.NotNull(_lastExportJobOutcome);
            Assert.Equal(OperationStatus.Failed, _lastExportJobOutcome.JobRecord.Status);
            Assert.Equal(expectedErrorMessage, _lastExportJobOutcome.JobRecord.FailureDetails.FailureReason);
            Assert.Equal(expectedHttpStatusCode, _lastExportJobOutcome.JobRecord.FailureDetails.FailureStatusCode);
        }

        [Fact]
        public async Task GivenAnUnKnownExceptionThrowFromAnonymizer_WhenExecuted_ThenJobStatusShouldBeUpdatedToFailed()
        {
            // this should not happen, thise test is to make sure if any unexpected exception happen, would not block export worker.
            string expectedError = "Unknown Error.";

            // Setup export destination client.
            ExportJobRecord exportJobRecordWithOneResource =
                 CreateExportJobRecord(maximumNumberOfResourcesPerQuery: 1, numberOfPagesPerCommit: _exportJobConfiguration.NumberOfPagesPerCommit, anonymizationConfigurationLocation: "anonymization-config-file");

            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithOneResource);
            IAnonymizer anonymizer = Substitute.For<IAnonymizer>();
            anonymizer.Anonymize(Arg.Any<ResourceElement>()).Returns<ResourceElement>(_ => throw new InvalidOperationException());
            IAnonymizerFactory factory = Substitute.For<IAnonymizerFactory>();
            factory.CreateAnonymizerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<Task<IAnonymizer>>(_ => Task.FromResult(anonymizer));

            var exportJobTask = new ExportJobTask(
                () => _fhirOperationDataStore.CreateMockScope(),
                Options.Create(_exportJobConfiguration),
                () => _searchService.CreateMockScope(),
                _groupMemberExtractor,
                _resourceToByteArraySerializer,
                _inMemoryDestinationClient,
                _resourceDeserializer,
                factory.CreateMockScope(),
                NullLogger<ExportJobTask>.Instance);

            await exportJobTask.ExecuteAsync(exportJobRecordWithOneResource, _weakETag, _cancellationToken);

            Assert.NotNull(_lastExportJobOutcome);
            Assert.Equal(OperationStatus.Failed, _lastExportJobOutcome.JobRecord.Status);
            Assert.Equal(expectedError, _lastExportJobOutcome.JobRecord.FailureDetails.FailureReason);
            Assert.Equal(HttpStatusCode.InternalServerError, _lastExportJobOutcome.JobRecord.FailureDetails.FailureStatusCode);
        }

        private ExportJobRecord CreateExportJobRecord(
            string requestEndpoint = "https://localhost/ExportJob/",
            ExportJobType exportJobType = ExportJobType.All,
            string resourceType = null,
            string hash = "hash",
            PartialDateTime since = null,
            string groupId = null,
            string storageAccountConnectionHash = "",
            string storageAccountUri = null,
            uint maximumNumberOfResourcesPerQuery = 0,
            uint numberOfPagesPerCommit = 0,
            string anonymizationConfigurationLocation = null,
            string anonymizationConfigurationFileEtag = null)
        {
            return new ExportJobRecord(
                new Uri(requestEndpoint),
                exportJobType,
                resourceType,
                hash,
                since: since,
                groupId: groupId,
                storageAccountConnectionHash: storageAccountConnectionHash,
                storageAccountUri: storageAccountUri == null ? _exportJobConfiguration.StorageAccountUri : storageAccountUri,
                maximumNumberOfResourcesPerQuery: maximumNumberOfResourcesPerQuery == 0 ? _exportJobConfiguration.MaximumNumberOfResourcesPerQuery : maximumNumberOfResourcesPerQuery,
                numberOfPagesPerCommit: numberOfPagesPerCommit == 0 ? _exportJobConfiguration.NumberOfPagesPerCommit : numberOfPagesPerCommit,
                anonymizationConfigurationLocation: anonymizationConfigurationLocation,
                anonymizationConfigurationFileETag: anonymizationConfigurationFileEtag);
        }

        private SearchResult CreateSearchResult(IEnumerable<SearchResultEntry> resourceWrappers = null, string continuationToken = null)
        {
            if (resourceWrappers == null)
            {
                resourceWrappers = Array.Empty<SearchResultEntry>();
            }

            return new SearchResult(resourceWrappers, new Tuple<string, string>[0], Array.Empty<(string parameterName, string reason)>(), continuationToken);
        }

        private SearchResultEntry CreateSearchResultEntry(string id, string type)
        {
            return new SearchResultEntry(
                            new ResourceWrapper(
                                id,
                                "1",
                                type,
                                new RawResource("data", Core.Models.FhirResourceFormat.Json),
                                null,
                                DateTimeOffset.MinValue,
                                false,
                                null,
                                null,
                                null));
        }

        private void SetupExportJobRecordAndOperationDataStore(ExportJobRecord exportJobRecord = null)
        {
            _exportJobRecord = exportJobRecord ?? new ExportJobRecord(
                new Uri("https://localhost/ExportJob/"),
                ExportJobType.Patient,
                null,
                "hash",
                storageAccountConnectionHash: string.Empty,
                storageAccountUri: _exportJobConfiguration.StorageAccountUri,
                maximumNumberOfResourcesPerQuery: _exportJobConfiguration.MaximumNumberOfResourcesPerQuery,
                numberOfPagesPerCommit: _exportJobConfiguration.NumberOfPagesPerCommit);

            _fhirOperationDataStore.UpdateExportJobAsync(_exportJobRecord, _weakETag, _cancellationToken).Returns(x =>
            {
                _lastExportJobOutcome = new ExportJobOutcome(_exportJobRecord, _weakETag);

                return _lastExportJobOutcome;
            });
        }
    }
}
