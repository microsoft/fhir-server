// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Internal;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
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
    public class ExportJobTaskTests
    {
        private const string PatientFileName = "Patient-1.ndjson";
        private const string ObservationFileName = "Observation-1.ndjson";
        private const string EncounterFileName = "Encounter-1.ndjson";
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
        private RequestContextAccessor<IFhirRequestContext> _contextAccessor;

        public ExportJobTaskTests()
        {
            _cancellationToken = _cancellationTokenSource.Token;
            SetupExportJobRecordAndOperationDataStore();
            _exportJobTask = CreateExportJobTask();
            ModelInfoProvider.SetProvider(MockModelInfoProviderBuilder.Create(FhirSpecification.R4).AddKnownTypes(KnownResourceTypes.Group).Build());
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
                _cancellationToken,
                true)
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
                _cancellationToken,
                true)
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
                _cancellationToken,
                true)
                .Returns(CreateSearchResult(continuationToken: continuationToken));

            bool capturedSearch = false;

            // Second search returns a search result without continuation token.
            _searchService.SearchAsync(
                null,
                Arg.Is(CreateQueryParametersExpressionWithContinuationToken(ContinuationTokenConverter.Encode(continuationToken), KnownResourceTypes.Patient)),
                _cancellationToken,
                true)
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
                _cancellationToken,
                true)
                .Returns(CreateSearchResult(continuationToken: continuationToken));

            bool capturedSearch = false;

            // Second search returns a search result without continuation token.
            _searchService.SearchAsync(
                null,
                Arg.Is(CreateQueryParametersExpressionWithContinuationToken(ContinuationTokenConverter.Encode(continuationToken), _exportJobRecord.Since, KnownResourceTypes.Patient)),
                _cancellationToken,
                true)
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
                _cancellationToken,
                true)
                .Returns(CreateSearchResult(continuationToken: continuationToken));

            bool firstCapturedSearch = false;
            string newContinuationToken = "newCt";

            // Second search returns a search result with continuation token.
            _searchService.SearchAsync(
                null,
                Arg.Is(CreateQueryParametersExpressionWithContinuationToken(ContinuationTokenConverter.Encode(continuationToken), KnownResourceTypes.Patient)),
                _cancellationToken,
                true)
                .Returns(x =>
                {
                    firstCapturedSearch = true;

                    return CreateSearchResult(continuationToken: newContinuationToken);
                });

            bool secondCapturedSearch = false;

            // Third search returns a search result without continuation token.
            _searchService.SearchAsync(
                null,
                Arg.Is(CreateQueryParametersExpressionWithContinuationToken(ContinuationTokenConverter.Encode(newContinuationToken), KnownResourceTypes.Patient)),
                _cancellationToken,
                true)
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
                _cancellationToken,
                true)
                .Returns(CreateSearchResult(continuationToken: continuationToken));

            bool firstCapturedSearch = false;
            string newContinuationToken = "newCt";

            // Second search returns a search result with continuation token.
            _searchService.SearchAsync(
                null,
                Arg.Is(CreateQueryParametersExpressionWithContinuationToken(ContinuationTokenConverter.Encode(continuationToken), _exportJobRecord.Since, KnownResourceTypes.Patient)),
                _cancellationToken,
                true)
                .Returns(x =>
                {
                    firstCapturedSearch = true;

                    return CreateSearchResult(continuationToken: newContinuationToken);
                });

            bool secondCapturedSearch = false;

            // Third search returns a search result without continuation token.
            _searchService.SearchAsync(
                null,
                Arg.Is(CreateQueryParametersExpressionWithContinuationToken(ContinuationTokenConverter.Encode(newContinuationToken), _exportJobRecord.Since, KnownResourceTypes.Patient)),
                _cancellationToken,
                true)
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
                Tuple.Create("_lastUpdated", $"le{_exportJobRecord.Till}").Equals(arg[1]) &&
                Tuple.Create("_type", resourceType).Equals(arg[2]);
        }

        private Expression<Predicate<IReadOnlyList<Tuple<string, string>>>> CreateQueryParametersExpression(PartialDateTime since, string resourceType)
        {
            return arg => arg != null &&
                Tuple.Create("_count", "1").Equals(arg[0]) &&
                Tuple.Create("_lastUpdated", $"le{_exportJobRecord.Till}").Equals(arg[1]) &&
                Tuple.Create("_lastUpdated", $"ge{since}").Equals(arg[2]) &&
                Tuple.Create("_type", resourceType).Equals(arg[3]);
        }

        private Expression<Predicate<IReadOnlyList<Tuple<string, string>>>> CreateQueryParametersExpressionWithContinuationToken(string continuationToken, string resourceType)
        {
            return arg => arg != null &&
                Tuple.Create("_count", "1").Equals(arg[0]) &&
                Tuple.Create("_lastUpdated", $"le{_exportJobRecord.Till}").Equals(arg[1]) &&
                Tuple.Create("_type", resourceType).Equals(arg[2]) &&
                Tuple.Create("ct", continuationToken).Equals(arg[3]);
        }

        private Expression<Predicate<IReadOnlyList<Tuple<string, string>>>> CreateQueryParametersExpressionWithContinuationToken(string continuationToken, PartialDateTime since, string resourceType)
        {
            return arg => arg != null &&
                Tuple.Create("_count", "1").Equals(arg[0]) &&
                Tuple.Create("_lastUpdated", $"le{_exportJobRecord.Till}").Equals(arg[1]) &&
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
                _cancellationToken,
                true)
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
                _cancellationToken,
                true)
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

        [Fact]
        public async Task GivenSearchHadIssues_WhenExecuted_ThenIssuesAreRecorded()
        {
            var issue = new OperationOutcomeIssue("warning", "code", "message");

            var exportJobRecordWithOneResource = CreateExportJobRecord();

            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithOneResource);

            // First search should not have continuation token in the list of query parameters.
            _searchService.SearchAsync(
                null,
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken,
                true)
                .Returns(x =>
                {
                    _contextAccessor.RequestContext.BundleIssues.Add(issue);

                    return CreateSearchResult();
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            Assert.True(_exportJobRecord.Issues.Contains(issue));
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
                _cancellationToken,
                true)
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

            // All of the ids should be present since it should have committed one last time after all the results were exported.
            CheckAllSingleIdFiles(KnownResourceTypes.Patient, 3, 0, 1);
        }

        [Fact]
        public async Task GivenConnectingToDestinationFails_WhenExecuted_ThenJobStatusShouldBeUpdatedToFailed()
        {
            // Setup export destination client.
            string connectionFailure = "failedToConnectToDestination";
            IExportDestinationClient mockExportDestinationClient = Substitute.For<IExportDestinationClient>();
            mockExportDestinationClient.ConnectAsync(Arg.Any<ExportJobConfiguration>(), Arg.Any<CancellationToken>(), Arg.Any<string>())
                .Returns<Task>(x => throw new DestinationConnectionException(connectionFailure, HttpStatusCode.BadRequest));

            var exportJobTask = CreateExportJobTask(exportDestinationClient: mockExportDestinationClient);

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

            var exportJobTask = CreateExportJobTask(exportJobConfiguration);

            _searchService.SearchAsync(
               Arg.Any<string>(),
               Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
               _cancellationToken,
               true)
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

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

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

            var exportJobTask = CreateExportJobTask(exportDestinationClient: mockDestinationClient);

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

            int numberOfCalls = 1;
            int numberOfSuccessfulPages = 3;

            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken,
                true)
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

            CheckAllSingleIdFiles(KnownResourceTypes.Patient, 2);
            Assert.Equal(2, _inMemoryDestinationClient.ExportedDataFileCount);
            Assert.NotNull(_exportJobRecord.Progress);

            // We create a new export job task here to simulate the worker picking up the "old" export job record
            // and resuming the export process. The export destination client contains data that has
            // been committed up until the "crash".
            _inMemoryDestinationClient = new InMemoryExportDestinationClient();

            var secondExportJobTask = CreateExportJobTask();

            numberOfSuccessfulPages = 5;
            await secondExportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            CheckAllSingleIdFiles(KnownResourceTypes.Patient, 4, 3);
            Assert.Equal(2, _inMemoryDestinationClient.ExportedDataFileCount);
        }

        [Fact]
        public async Task GivenAPatientExportJob_WhenExecuted_ThenAllCompartmentResourcesShouldBeExported()
        {
            int numberOfCalls = 0;
            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken,
                true)
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
                _cancellationToken,
                true)
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

            CheckAllSingleIdFiles(KnownResourceTypes.Patient, 4);
            string exportedIds = _inMemoryDestinationClient.GetExportedData(ObservationFileName);
            Assert.Equal("12", exportedIds);
            exportedIds = _inMemoryDestinationClient.GetExportedData(KnownResourceTypes.Observation + "-2.ndjson");
            Assert.Equal("34", exportedIds);
            exportedIds = _inMemoryDestinationClient.GetExportedData(KnownResourceTypes.Observation + "-3.ndjson");
            Assert.Equal("56", exportedIds);
            exportedIds = _inMemoryDestinationClient.GetExportedData(KnownResourceTypes.Observation + "-4.ndjson");
            Assert.Equal("78", exportedIds);

            Assert.Equal(OperationStatus.Completed, _exportJobRecord.Status);
        }

        [Fact]
        public async Task GivenAPatientExportJobWithNoCompartmentResources_WhenExecuted_ThenJustAllPatientResourcesShouldBeExported()
        {
            int numberOfCalls = 0;
            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken,
                true)
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
                _cancellationToken,
                true)
                .Returns(x =>
                {
                    numberOfCompartmentCalls++;
                    return CreateSearchResult();
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            Assert.Equal(4, numberOfCalls);
            Assert.Equal(4, numberOfCompartmentCalls);

            CheckAllSingleIdFiles(KnownResourceTypes.Patient, 4);
            Assert.Equal(4, _inMemoryDestinationClient.ExportedDataFileCount);

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
                _cancellationToken,
                true)
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
                _cancellationToken,
                true)
                .Returns(x =>
                {
                    numberOfCompartmentCalls++;
                    if (numberOfCompartmentCalls - numberOfSuccessfulCompartmentPages == 0)
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

            string exportedIds = _inMemoryDestinationClient.GetExportedData(PatientFileName);
            Assert.Equal("1", exportedIds);
            exportedIds = _inMemoryDestinationClient.GetExportedData(ObservationFileName);
            Assert.Equal("123", exportedIds);

            Assert.NotNull(_exportJobRecord.Progress);
            Assert.NotNull(_exportJobRecord.Progress.SubSearch);

            // We create a new export job task here to simulate the worker picking up the "old" export job record
            // and resuming the export process. The export destination client contains data that has
            // been committed up until the "crash".
            _inMemoryDestinationClient = new InMemoryExportDestinationClient();

            var secondExportJobTask = CreateExportJobTask();

            // Reseting the number of calls so that the ressource id of the Patient is the same ('2') as it was when the crash happened.
            numberOfCalls = 1;
            await secondExportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            exportedIds = _inMemoryDestinationClient.GetExportedData(KnownResourceTypes.Patient + "-2.ndjson");
            Assert.Equal("2", exportedIds);

            exportedIds = _inMemoryDestinationClient.GetExportedData(KnownResourceTypes.Observation + "-3.ndjson");

            // 4 was in the commit buffer when the crash happened, and 5 is the one that triggered the crash.
            // Since the 'id' is based on the number of times the mock method has been called these values never get exported.
            // The file is called Observation-3 because the test keeps the JobRecord in memory, it doesn't reload it from the database.
            // This means it thinks an Observation-2 exists because it started to create one before the crash.
            // If it had reloaded from the database this wouldn't happen because the buffered, but uncommited, file's existance was never recorded to the database.
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
                _cancellationToken,
                true)
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

            string exportedIds = _inMemoryDestinationClient.GetExportedData(ObservationFileName);
            Assert.Equal("0", exportedIds);
            exportedIds = _inMemoryDestinationClient.GetExportedData(EncounterFileName);
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
                _cancellationToken,
                true)
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
                _cancellationToken,
                true)
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

            string exportedIds = _inMemoryDestinationClient.GetExportedData(ObservationFileName);
            Assert.Equal("0", exportedIds);
            Assert.Equal(1, _inMemoryDestinationClient.ExportedDataFileCount);

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
                _cancellationToken,
                true)
                .Returns(x =>
                {
                    searchCallsMade++;
                    var queryParameterList = x.ArgAt<IReadOnlyList<Tuple<string, string>>>(1);

                    bool typeParameterIncluded = false;
                    bool continuationTokenParameterIncluded = false;
                    var types = new string[0];

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

            string exportedIds = _inMemoryDestinationClient.GetExportedData(ObservationFileName);
            Assert.Equal("1", exportedIds);
            exportedIds = _inMemoryDestinationClient.GetExportedData(EncounterFileName);
            Assert.Equal("1", exportedIds);
            Assert.Equal(2, _inMemoryDestinationClient.ExportedDataFileCount);

            // We create a new export job task here to simulate the worker picking up the "old" export job record
            // and resuming the export process. The export destination client contains data that has
            // been committed up until the "crash".
            _inMemoryDestinationClient = new InMemoryExportDestinationClient();

            var secondExportJobTask = CreateExportJobTask();

            // Reseting the number of calls so that the ressource id of the Patient is the same ('2') as it was when the crash happened.
            await secondExportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            CheckAllSingleIdFiles(KnownResourceTypes.Observation, 4, 3, -1);
            CheckAllSingleIdFiles(KnownResourceTypes.Encounter, 4, 3, -1);
            Assert.Equal(4, _inMemoryDestinationClient.ExportedDataFileCount);

            Assert.Equal(OperationStatus.Completed, _exportJobRecord.Status);
        }

        [Fact]
        public async Task GivenAGroupExportJob_WhenGroupDoesNotExist_ThenLogExceptionAsInfo()
        {
            string groupId = "groupdoesnotexist";

            var exportJobRecordWithCommitPages = CreateExportJobRecord(
              exportJobType: ExportJobType.Group,
              groupId: groupId,
              numberOfPagesPerCommit: 2);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithCommitPages);

            _groupMemberExtractor.GetGroupPatientIds(
                groupId,
                Arg.Any<DateTimeOffset>(),
                _cancellationToken).Returns<Task<HashSet<string>>>(x =>
                {
                    throw new ResourceNotFoundException($"Group {groupId} was not found.", new ResourceKey(KnownResourceTypes.Group, groupId));
                });

            var logger = new TestLogger();
            ExportJobTask exportJobTask = CreateExportJobTask(logger: logger);

            await exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            Assert.Single(logger.ResourceKeyList); // Log info is called exactly once.
            Assert.Equal(KnownResourceTypes.Group, logger.ResourceKeyList[0].ResourceType);
            Assert.Equal(groupId, logger.ResourceKeyList[0].Id);
        }

        [Fact]
        public async Task GivenAGroupExportJob_WhenGroupExists_ThenDoNotLogExceptionAsInfo()
        {
            string groupId = "groupexists";

            var exportJobRecordWithCommitPages = CreateExportJobRecord(
              exportJobType: ExportJobType.Group,
              groupId: groupId,
              numberOfPagesPerCommit: 2);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithCommitPages);

            _groupMemberExtractor.GetGroupPatientIds(
                groupId,
                Arg.Any<DateTimeOffset>(),
                _cancellationToken).Returns(
                    new HashSet<string>()
                    {
                        "1",
                        "2",
                    });

            _searchService.SearchAsync(
                KnownResourceTypes.Patient,
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken,
                true)
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
                _cancellationToken,
                true)
                .Returns(x =>
                 {
                     string parentId = x.ArgAt<string>(1);

                     return CreateSearchResult(new SearchResultEntry[]
                     {
                         CreateSearchResultEntry(parentId, KnownResourceTypes.Observation),
                     });
                 });

            var logger = new TestLogger();
            ExportJobTask exportJobTask = CreateExportJobTask(logger: logger);

            await exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            Assert.Empty(logger.ResourceKeyList);
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
                KnownResourceTypes.Patient,
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken,
                true)
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
                _cancellationToken,
                true)
                .Returns(x =>
                 {
                     string parentId = x.ArgAt<string>(1);

                     return CreateSearchResult(new SearchResultEntry[]
                     {
                         CreateSearchResultEntry(parentId, KnownResourceTypes.Observation),
                     });
                 });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string exportedIds = _inMemoryDestinationClient.GetExportedData(PatientFileName);
            Assert.Equal("12", exportedIds);
            exportedIds = _inMemoryDestinationClient.GetExportedData(ObservationFileName);
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
                KnownResourceTypes.Patient,
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken,
                true)
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
                _cancellationToken,
                true)
                .Returns(x =>
                {
                    string parentId = x.ArgAt<string>(1);

                    return CreateSearchResult(new SearchResultEntry[]
                    {
                         CreateSearchResultEntry(parentId, KnownResourceTypes.Observation),
                    });
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            CheckAllSingleIdFiles(KnownResourceTypes.Patient, 3);
            CheckAllSingleIdFiles(KnownResourceTypes.Observation, 3);
            Assert.Equal(6, _inMemoryDestinationClient.ExportedDataFileCount);

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
                KnownResourceTypes.Patient,
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken,
                true)
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
                        continuationTokenIndex = int.Parse(ContinuationTokenConverter.Decode(x.ArgAt<IReadOnlyList<Tuple<string, string>>>(1)[2].Item2).Substring(2));
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
                _cancellationToken,
                true)
                .Returns(x =>
                {
                    string parentId = x.ArgAt<string>(1);

                    return CreateSearchResult(new SearchResultEntry[]
                    {
                         CreateSearchResultEntry(parentId, KnownResourceTypes.Observation),
                    });
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string exportedIds = _inMemoryDestinationClient.GetExportedData(PatientFileName);
            Assert.Equal("1", exportedIds);
            exportedIds = _inMemoryDestinationClient.GetExportedData(ObservationFileName);
            Assert.Equal("1", exportedIds);
            Assert.Equal(2, _inMemoryDestinationClient.ExportedDataFileCount);

            // We create a new export job task here to simulate the worker picking up the "old" export job record
            // and resuming the export process. The export destination client contains data that has
            // been committed up until the "crash".
            _inMemoryDestinationClient = new InMemoryExportDestinationClient();

            var secondExportJobTask = CreateExportJobTask();

            // Reseting the number of calls so that the ressource id of the Patient is the same ('2') as it was when the crash happened.
            await secondExportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            CheckAllSingleIdFiles(KnownResourceTypes.Patient, 3, 2);
            CheckAllSingleIdFiles(KnownResourceTypes.Observation, 3, 2);
            Assert.Equal(4, _inMemoryDestinationClient.ExportedDataFileCount);

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
                KnownResourceTypes.Patient,
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken,
                true)
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
                _cancellationToken,
                true)
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

            string exportedIds = _inMemoryDestinationClient.GetExportedData(ObservationFileName);
            Assert.Equal("123", exportedIds);
            exportedIds = _inMemoryDestinationClient.GetExportedData(EncounterFileName);
            Assert.Equal("123", exportedIds);
            Assert.Equal(2, _inMemoryDestinationClient.ExportedDataFileCount);

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
                _cancellationToken,
                true)
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
            factory.CreateAnonymizerAsync(Arg.Any<ExportJobRecord>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IAnonymizer>(anonymizer));
            var inMemoryDestinationClient = new InMemoryExportDestinationClient();

            var anonymizedExportJobTask = CreateExportJobTask(exportDestinationClient: inMemoryDestinationClient, anonymizerFactory: factory.CreateMockScope());

            await anonymizedExportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string exportedValue = inMemoryDestinationClient.GetExportedData(PatientFileName);

            Assert.Equal("anonymized-resource", exportedValue);
            Assert.True(capturedSearch);
        }

        [Theory]
        [InlineData(typeof(AnonymizationConfigurationNotFoundException), "config not found", HttpStatusCode.BadRequest)]
        [InlineData(typeof(FailedToParseAnonymizationConfigurationException), "cannot parse the config", HttpStatusCode.BadRequest)]
        [InlineData(typeof(InvalidOperationException), "Unknown Error.", HttpStatusCode.InternalServerError)]
        [InlineData(typeof(RequestEntityTooLargeException), "Timespan in export request contains too much data to export in a single request. Please try reducing the time range and try again.", HttpStatusCode.RequestEntityTooLarge)]
        public async Task GivenExceptionThrowFromAnonymizerFactory_WhenExecuted_ThenJobStatusShouldBeUpdatedToFailed(Type exceptionType, string expectedErrorMessage, HttpStatusCode expectedHttpStatusCode)
        {
            // Setup export destination client.
            ExportJobRecord exportJobRecordWithOneResource =
                 CreateExportJobRecord(maximumNumberOfResourcesPerQuery: 1, numberOfPagesPerCommit: _exportJobConfiguration.NumberOfPagesPerCommit, anonymizationConfigurationLocation: "anonymization-config-file");

            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithOneResource);
            IAnonymizerFactory factory = Substitute.For<IAnonymizerFactory>();
            factory.CreateAnonymizerAsync(Arg.Any<ExportJobRecord>(), Arg.Any<CancellationToken>()).Returns<Task<IAnonymizer>>(_ => throw (Exception)Activator.CreateInstance(exceptionType, expectedErrorMessage));

            var exportJobTask = CreateExportJobTask(anonymizerFactory: factory.CreateMockScope());

            await exportJobTask.ExecuteAsync(exportJobRecordWithOneResource, _weakETag, _cancellationToken);

            Assert.NotNull(_lastExportJobOutcome);
            Assert.Equal(OperationStatus.Failed, _lastExportJobOutcome.JobRecord.Status);
            Assert.Equal(expectedErrorMessage, _lastExportJobOutcome.JobRecord.FailureDetails.FailureReason);
            Assert.Equal(expectedHttpStatusCode, _lastExportJobOutcome.JobRecord.FailureDetails.FailureStatusCode);
        }

        [Fact]
        public async Task GivenAnUnKnownExceptionThrowFromAnonymizer_WhenExecuted_ThenJobStatusShouldBeUpdatedToFailed()
        {
            // First search should not have continuation token in the list of query parameters.
            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken,
                true)
                .Returns(x =>
                {
                    return CreateSearchResult(new[]
                        {
                            CreateSearchResultEntry("1", "Patient"),
                        });
                });

            // Setup export destination client.
            ExportJobRecord exportJobRecordWithOneResource =
                 CreateExportJobRecord(maximumNumberOfResourcesPerQuery: 1, numberOfPagesPerCommit: _exportJobConfiguration.NumberOfPagesPerCommit, anonymizationConfigurationLocation: "anonymization-config-file");

            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithOneResource);
            IAnonymizer anonymizer = Substitute.For<IAnonymizer>();
            anonymizer.Anonymize(Arg.Any<ResourceElement>()).Returns<ResourceElement>(_ => throw new InvalidOperationException());
            IAnonymizerFactory factory = Substitute.For<IAnonymizerFactory>();
            factory.CreateAnonymizerAsync(Arg.Any<ExportJobRecord>(), Arg.Any<CancellationToken>()).Returns<Task<IAnonymizer>>(_ => Task.FromResult(anonymizer));

            var exportJobTask = CreateExportJobTask(anonymizerFactory: factory.CreateMockScope());

            await exportJobTask.ExecuteAsync(exportJobRecordWithOneResource, _weakETag, _cancellationToken);

            Assert.NotNull(_lastExportJobOutcome);
            Assert.Equal(OperationStatus.Failed, _lastExportJobOutcome.JobRecord.Status);
            Assert.Equal(HttpStatusCode.BadRequest, _lastExportJobOutcome.JobRecord.FailureDetails.FailureStatusCode);
        }

        [Fact]
        public async Task GivenAnExportJobWithACustomContainer_WhenExecuted_ThenAllResourcesAreExportedToThatContainer()
        {
            string containerName = "test_container";
            var exportJobRecordWithContainer = CreateExportJobRecord(
                 containerName: containerName);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithContainer);

            SearchResult searchResultWithContinuationToken = CreateSearchResult(continuationToken: "ct");

            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken,
                true)
                .Returns(x =>
                {
                    return CreateSearchResult(
                        new[]
                        {
                            CreateSearchResultEntry("1", "Patient"),
                        });
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string actualIds = _inMemoryDestinationClient.GetExportedData(PatientFileName);

            Assert.Equal("1", actualIds);
            Assert.Equal(containerName, _inMemoryDestinationClient.ConnectedContainer);
        }

        [Fact]
        public async Task GivenAnExportJobWithAFormat_WhenExecuted_ThenAllResourcesAreExportedToTheProperLocation()
        {
            var exportJobRecordWithFormat = CreateExportJobRecord(
                 format: ExportFormatTags.ResourceName + "/" + ExportFormatTags.Timestamp + "_" + ExportFormatTags.Id);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithFormat);

            SearchResult searchResultWithContinuationToken = CreateSearchResult(continuationToken: "ct");

            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken,
                true)
                .Returns(x =>
                {
                    return CreateSearchResult(
                        new[]
                        {
                            CreateSearchResultEntry("1", KnownResourceTypes.Patient),
                            CreateSearchResultEntry("2", KnownResourceTypes.Observation),
                        });
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string dateTime = _exportJobRecord.QueuedTime.UtcDateTime.ToString("s")
                .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace(":", string.Empty, StringComparison.OrdinalIgnoreCase);
            string uriSuffix = "/" + dateTime + "_" + _exportJobRecord.Id + "-1.ndjson";

            string patientIds = _inMemoryDestinationClient.GetExportedData(KnownResourceTypes.Patient + uriSuffix);
            string observationIds = _inMemoryDestinationClient.GetExportedData(KnownResourceTypes.Observation + uriSuffix);

            Assert.Equal("1", patientIds);
            Assert.Equal("2", observationIds);
            Assert.Equal(2, _inMemoryDestinationClient.ExportedDataFileCount);
            Assert.Equal(OperationStatus.Completed, _exportJobRecord.Status);
        }

        [Fact]
        public async Task GivenAnExportJobWithFilters_WhenExecuted_ThenAllResourcesAreExportedToTheProperLocation()
        {
            var filters = new List<ExportJobFilter>()
                 {
                     new ExportJobFilter(
                         KnownResourceTypes.Observation,
                         new List<Tuple<string, string>>()
                         {
                             new Tuple<string, string>("status", "final"),
                             new Tuple<string, string>("subject", "Patient/1"),
                         }),
                     new ExportJobFilter(
                         KnownResourceTypes.Patient,
                         new List<Tuple<string, string>>()
                         {
                             new Tuple<string, string>("address", "Seattle"),
                         }),
                 };
            var exportJobRecordWithFormat = CreateExportJobRecord(
                resourceType: $"{KnownResourceTypes.Patient},{KnownResourceTypes.Observation},{KnownResourceTypes.Encounter}",
                filters: filters);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithFormat);

            var checkedObservation = false;
            var checkedPatient = false;
            var checkedOther = false;

            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken,
                true)
                .Returns(x =>
                {
                    var type = x[0] as string;
                    var queryParams = x[1] as IReadOnlyList<Tuple<string, string>>;

                    if (type == KnownResourceTypes.Observation)
                    {
                        Assert.Contains(queryParams, (param) => param == filters[0].Parameters[0]);
                        Assert.Contains(queryParams, (param) => param == filters[0].Parameters[1]);
                        checkedObservation = true;
                    }
                    else if (type == KnownResourceTypes.Patient)
                    {
                        Assert.Contains(queryParams, (param) => param == filters[1].Parameters[0]);
                        checkedPatient = true;
                    }
                    else
                    {
                        type = KnownResourceTypes.Encounter;
                        checkedOther = true;
                    }

                    return CreateSearchResult(
                        new[]
                        {
                            CreateSearchResultEntry("1", type),
                        });
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string patientIds = _inMemoryDestinationClient.GetExportedData(PatientFileName);
            string observationIds = _inMemoryDestinationClient.GetExportedData(ObservationFileName);
            string encounterIds = _inMemoryDestinationClient.GetExportedData(EncounterFileName);

            Assert.True(checkedObservation);
            Assert.True(checkedPatient);
            Assert.True(checkedOther);

            Assert.Equal("1", patientIds);
            Assert.Equal("1", observationIds);
            Assert.Equal("1", encounterIds);
            Assert.Equal(3, _inMemoryDestinationClient.ExportedDataFileCount);
            Assert.Equal(OperationStatus.Completed, _exportJobRecord.Status);
        }

        [Fact]
        public async Task GivenAPatientExportJobWithFilters_WhenExecuted_ThenAllResourcesAreExportedToTheProperLocation()
        {
            var filters = new List<ExportJobFilter>()
                 {
                     new ExportJobFilter(
                         KnownResourceTypes.Observation,
                         new List<Tuple<string, string>>()
                         {
                             new Tuple<string, string>("status", "final"),
                             new Tuple<string, string>("subject", "Patient/2"),
                         }),
                     new ExportJobFilter(
                         KnownResourceTypes.Patient,
                         new List<Tuple<string, string>>()
                         {
                             new Tuple<string, string>("address", "Seattle"),
                         }),
                 };
            var exportJobRecordWithFormat = CreateExportJobRecord(
                exportJobType: ExportJobType.Patient,
                resourceType: $"{KnownResourceTypes.Patient},{KnownResourceTypes.Observation},{KnownResourceTypes.Encounter}",
                filters: filters);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithFormat);

            var checkedObservation = false;
            var checkedPatient = false;
            var checkedCompartmentPatient = false;
            var checkedOther = false;

            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken,
                true)
                .Returns(x =>
                {
                    var type = x[0] as string;
                    var queryParams = x[1] as IReadOnlyList<Tuple<string, string>>;

                    if (type == KnownResourceTypes.Patient)
                    {
                        Assert.Contains(queryParams, (param) => param == filters[1].Parameters[0]);
                        checkedPatient = true;
                    }
                    else
                    {
                        // Failure condition, only Patients should be searched.
                        Assert.True(false);
                    }

                    return CreateSearchResult(
                        new[]
                        {
                            CreateSearchResultEntry("2", type),
                        });
                });

            _searchService.SearchCompartmentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken,
                true)
                .Returns(x =>
                {
                    var type = x[2] as string;
                    var queryParams = x[3] as IReadOnlyList<Tuple<string, string>>;

                    if (type == KnownResourceTypes.Observation)
                    {
                        Assert.Contains(queryParams, (param) => param == filters[0].Parameters[0]);
                        Assert.Contains(queryParams, (param) => param == filters[0].Parameters[1]);
                        checkedObservation = true;
                    }
                    else if (type == KnownResourceTypes.Patient)
                    {
                        Assert.Contains(queryParams, (param) => param == filters[1].Parameters[0]);
                        checkedCompartmentPatient = true;
                    }
                    else
                    {
                        type = KnownResourceTypes.Encounter;
                        checkedOther = true;
                    }

                    return CreateSearchResult(
                        new[]
                        {
                            CreateSearchResultEntry("1", type),
                        });
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string exportedIds = _inMemoryDestinationClient.GetExportedData(PatientFileName);
            Assert.Equal("12", exportedIds);

            string observationIds = _inMemoryDestinationClient.GetExportedData(ObservationFileName);
            string encounterIds = _inMemoryDestinationClient.GetExportedData(EncounterFileName);

            Assert.True(checkedObservation);
            Assert.True(checkedPatient);
            Assert.True(checkedCompartmentPatient);
            Assert.True(checkedOther);

            Assert.Equal("1", observationIds);
            Assert.Equal("1", encounterIds);
            Assert.Equal(3, _inMemoryDestinationClient.ExportedDataFileCount);
            Assert.Equal(OperationStatus.Completed, _exportJobRecord.Status);
        }

        [Fact]
        public async Task GivenAPatientExportJobToResumeWithFilters_WhenExecuted_ThenAllResourcesAreExportedToTheProperLocation()
        {
            var filters = new List<ExportJobFilter>()
                 {
                     new ExportJobFilter(
                         KnownResourceTypes.Observation,
                         new List<Tuple<string, string>>()
                         {
                             new Tuple<string, string>("status", "final"),
                             new Tuple<string, string>("subject", "Patient/2"),
                         }),
                     new ExportJobFilter(
                         KnownResourceTypes.Patient,
                         new List<Tuple<string, string>>()
                         {
                             new Tuple<string, string>("address", "Seattle"),
                         }),
                     new ExportJobFilter(
                         KnownResourceTypes.Encounter,
                         new List<Tuple<string, string>>()
                         {
                             new Tuple<string, string>("status", "planned"),
                         }),
                 };
            var exportJobRecordWithFormat = CreateExportJobRecord(
                exportJobType: ExportJobType.Patient,
                resourceType: $"{KnownResourceTypes.Patient},{KnownResourceTypes.Observation},{KnownResourceTypes.Encounter}",
                filters: filters);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithFormat);

            var checkedObservation = false;
            var checkedPatient = false;
            var checkedCompartmentPatient = false;
            var checkedEncounter = false;

            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken,
                true)
                .Returns(x =>
                {
                    var type = x[0] as string;
                    var queryParams = x[1] as IReadOnlyList<Tuple<string, string>>;

                    if (type == KnownResourceTypes.Patient)
                    {
                        Assert.Contains(queryParams, (param) => param == filters[1].Parameters[0]);
                        checkedPatient = true;
                    }
                    else
                    {
                        // Failure condition, only Patients should be searched.
                        Assert.True(false);
                    }

                    return CreateSearchResult(
                        new[]
                        {
                            CreateSearchResultEntry("2", type),
                        });
                });

            var compartmentVisited = false;
            _searchService.SearchCompartmentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken,
                true)
                .Returns(x =>
                {
                    var type = x[2] as string;
                    var queryParams = x[3] as IReadOnlyList<Tuple<string, string>>;

                    if (compartmentVisited)
                    {
                        throw new Exception();
                    }

                    if (type == KnownResourceTypes.Observation)
                    {
                        Assert.Contains(queryParams, (param) => param == filters[0].Parameters[0]);
                        Assert.Contains(queryParams, (param) => param == filters[0].Parameters[1]);
                        checkedObservation = true;
                    }
                    else if (type == KnownResourceTypes.Patient)
                    {
                        Assert.Contains(queryParams, (param) => param == filters[1].Parameters[0]);
                        checkedCompartmentPatient = true;
                    }
                    else if (type == KnownResourceTypes.Encounter)
                    {
                        Assert.Contains(queryParams, (param) => param == filters[2].Parameters[0]);
                        compartmentVisited = true;
                    }
                    else
                    {
                        // Failure condition, others shouldn't be searched yet.
                        Assert.True(false);
                    }

                    return CreateSearchResult(
                        new[]
                        {
                            CreateSearchResultEntry("1", type),
                        },
                        type == KnownResourceTypes.Encounter ? "ct" : null);
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            Assert.NotEqual(OperationStatus.Completed, _exportJobRecord.Status);

            _searchService.SearchCompartmentAsync(
               Arg.Any<string>(),
               Arg.Any<string>(),
               Arg.Any<string>(),
               Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
               _cancellationToken,
               true)
               .Returns(x =>
               {
                   var type = x[2] as string;
                   var queryParams = x[3] as IReadOnlyList<Tuple<string, string>>;

                   if (type == KnownResourceTypes.Observation)
                   {
                       // Failure condition, Observation already checked.
                       Assert.True(false);
                   }
                   else if (type == KnownResourceTypes.Encounter)
                   {
                       Assert.Contains(queryParams, (param) => param == filters[2].Parameters[0]);
                       checkedEncounter = true;
                   }
                   else
                   {
                       // Failure condition, Should only check requested resources.
                       Assert.True(false);
                   }

                   return CreateSearchResult(
                       new[]
                       {
                            CreateSearchResultEntry("2", type),
                       });
               });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            string observationIds = _inMemoryDestinationClient.GetExportedData(ObservationFileName);

            Assert.True(checkedObservation);
            Assert.True(checkedPatient);
            Assert.True(checkedCompartmentPatient);
            Assert.True(checkedEncounter);

            // Patient is visited twice (top level and compartment search) so two ids are recorded
            CheckAllSingleIdFiles(KnownResourceTypes.Patient, 2);
            Assert.Equal("1", observationIds);

            // Encounter is visited twice (before and after the exception) so two ids are recorded
            CheckAllSingleIdFiles(KnownResourceTypes.Encounter, 2);

            Assert.Equal(5, _inMemoryDestinationClient.ExportedDataFileCount);
            Assert.Equal(OperationStatus.Completed, _exportJobRecord.Status);
        }

        // If a patient/group export job with type and type filters is run, but patients aren't in the types requested, the search should be run here but no patients printed to the output
        // If a patient/group export job with type and type filters is run, and patients are in the types requested and filtered, the search should not be run as patients were searched above
        // If an export job with type and type filters is run, the search should not be run if all the types were searched above.

        [Fact]
        public async Task GivenAPatientExportJobWithFiltersAndPatientsAreNotRequested_WhenExecuted_ThenAllResourcesAreExported()
        {
            var filters = new List<ExportJobFilter>()
                 {
                     new ExportJobFilter(
                         KnownResourceTypes.Observation,
                         new List<Tuple<string, string>>()
                         {
                             new Tuple<string, string>("status", "final"),
                             new Tuple<string, string>("subject", "Patient/1"),
                         }),
                 };

            await RunTypeFilterTest(filters, $"{KnownResourceTypes.Observation}");

            string observationIds = _inMemoryDestinationClient.GetExportedData(ObservationFileName);

            Assert.Equal("2", observationIds);
            Assert.Equal(1, _inMemoryDestinationClient.ExportedDataFileCount);
            Assert.Equal(OperationStatus.Completed, _exportJobRecord.Status);
        }

        [Fact]
        public async Task GivenAnExportJobWithInvalidStorageAccount_WhenExecuted_ThenAnExceptionIsLogged()
        {
            string errorMessage = "from mock";
            IExportDestinationClient exportDestinationClient = Substitute.For<IExportDestinationClient>();
            exportDestinationClient.ConnectAsync(
                Arg.Any<ExportJobConfiguration>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string>())
                .Throws(new DestinationConnectionException(errorMessage, HttpStatusCode.BadRequest));

            var exportJobTask = CreateExportJobTask(exportDestinationClient: exportDestinationClient);

            await exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

            Assert.Equal(OperationStatus.Failed, _exportJobRecord.Status);
            Assert.Equal(errorMessage, _exportJobRecord.FailureDetails.FailureReason);
        }

        private async Task RunTypeFilterTest(IList<ExportJobFilter> filters, string resourceTypes)
        {
            var exportJobRecordWithFormat = CreateExportJobRecord(
                exportJobType: ExportJobType.Patient,
                resourceType: resourceTypes,
                filters: filters);
            SetupExportJobRecordAndOperationDataStore(exportJobRecordWithFormat);

            var resourcesChecked = 0;
            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken,
                true)
                .Returns(x =>
                {
                    var type = x[0] as string;
                    var queryParams = x[1] as IReadOnlyList<Tuple<string, string>>;

                    if (type == null)
                    {
                        type = KnownResourceTypes.Device;
                    }

                    resourcesChecked++;

                    return CreateSearchResult(
                        new[]
                        {
                            CreateSearchResultEntry(resourcesChecked.ToString(), type),
                        });
                });

            _searchService.SearchCompartmentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                _cancellationToken,
                true)
                .Returns(x =>
                {
                    var type = x[2] as string;
                    var queryParams = x[3] as IReadOnlyList<Tuple<string, string>>;

                    if (type == null)
                    {
                        type = KnownResourceTypes.Immunization;
                    }

                    resourcesChecked++;

                    return CreateSearchResult(
                        new[]
                        {
                            CreateSearchResultEntry(resourcesChecked.ToString(), type),
                        });
                });

            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);
        }

        private ExportJobRecord CreateExportJobRecord(
            string requestEndpoint = "https://localhost/ExportJob/",
            ExportJobType exportJobType = ExportJobType.All,
            string format = ExportFormatTags.ResourceName,
            string resourceType = null,
            IList<ExportJobFilter> filters = null,
            string hash = "hash",
            PartialDateTime since = null,
            string groupId = null,
            string storageAccountConnectionHash = "",
            string storageAccountUri = null,
            uint maximumNumberOfResourcesPerQuery = 0,
            uint numberOfPagesPerCommit = 0,
            string containerName = null,
            string anonymizationConfigurationLocation = null,
            string anonymizationConfigurationFileEtag = null)
        {
            return new ExportJobRecord(
                new Uri(requestEndpoint),
                exportJobType,
                format,
                resourceType,
                filters,
                hash,
                _exportJobConfiguration.RollingFileSizeInMB,
                since: since,
                groupId: groupId,
                storageAccountConnectionHash: storageAccountConnectionHash,
                storageAccountUri: storageAccountUri == null ? _exportJobConfiguration.StorageAccountUri : storageAccountUri,
                maximumNumberOfResourcesPerQuery: maximumNumberOfResourcesPerQuery == 0 ? _exportJobConfiguration.MaximumNumberOfResourcesPerQuery : maximumNumberOfResourcesPerQuery,
                numberOfPagesPerCommit: numberOfPagesPerCommit == 0 ? _exportJobConfiguration.NumberOfPagesPerCommit : numberOfPagesPerCommit,
                storageAccountContainerName: containerName,
                anonymizationConfigurationLocation: anonymizationConfigurationLocation,
                anonymizationConfigurationFileETag: anonymizationConfigurationFileEtag);
        }

        private ExportJobTask CreateExportJobTask(
            ExportJobConfiguration exportJobConfiguration = null,
            IExportDestinationClient exportDestinationClient = null,
            IScoped<IAnonymizerFactory> anonymizerFactory = null,
            ILogger<ExportJobTask> logger = null)
        {
            _resourceToByteArraySerializer.StringSerialize(Arg.Any<ResourceElement>()).Returns(x => x.ArgAt<ResourceElement>(0).Instance.Value.ToString());
            _resourceDeserializer.Deserialize(Arg.Any<ResourceWrapper>()).Returns(x => new ResourceElement(ElementNode.FromElement(ElementNode.ForPrimitive(x.ArgAt<ResourceWrapper>(0).ResourceId))));

            _contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            return new ExportJobTask(
                () => _fhirOperationDataStore.CreateMockScope(),
                Options.Create(exportJobConfiguration == null ? _exportJobConfiguration : exportJobConfiguration),
                () => _searchService.CreateMockScope(),
                _groupMemberExtractor,
                _resourceToByteArraySerializer,
                exportDestinationClient == null ? _inMemoryDestinationClient : exportDestinationClient,
                _resourceDeserializer,
                anonymizerFactory,
                Substitute.For<IMediator>(),
                _contextAccessor,
                logger ?? NullLogger<ExportJobTask>.Instance,
                null,
                null);
        }

        private SearchResult CreateSearchResult(IEnumerable<SearchResultEntry> resourceWrappers = null, string continuationToken = null)
        {
            if (resourceWrappers == null)
            {
                resourceWrappers = Array.Empty<SearchResultEntry>();
            }

            return new SearchResult(resourceWrappers, continuationToken, null, new Tuple<string, string>[0]);
        }

        private SearchResultEntry CreateSearchResultEntry(string id, string type)
        {
            return new SearchResultEntry(
                            new ResourceWrapper(
                                id,
                                "1",
                                type,
                                new RawResource("data", Core.Models.FhirResourceFormat.Json, isMetaSet: false),
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
                ExportFormatTags.ResourceName,
                null,
                null,
                "hash",
                _exportJobConfiguration.RollingFileSizeInMB,
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

        private void CheckAllSingleIdFiles(string resourceType, int numExpectedFiles, int offset = 1, int idOffset = 0)
        {
            for (int count = offset; count <= numExpectedFiles; count++)
            {
                string data = _inMemoryDestinationClient.GetExportedData(resourceType + "-" + (count + idOffset) + ".ndjson");
                Assert.Equal(count.ToString(), data);
            }
        }

        private class TestLogger : ILogger<ExportJobTask>
        {
            public List<ResourceKey> ResourceKeyList { get; set; } = new List<ResourceKey>();

            public IDisposable BeginScope<TState>(TState state)
            {
                return null;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                var ex = exception as ResourceNotFoundException;

                if (logLevel == LogLevel.Information && ex?.ResourceKey?.ResourceType == KnownResourceTypes.Group)
                {
                    ResourceKeyList.Add(ex?.ResourceKey);
                }
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return false;
            }
        }
    }
}
