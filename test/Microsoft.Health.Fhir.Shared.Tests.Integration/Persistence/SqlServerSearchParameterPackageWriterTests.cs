// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.FhirPackages;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.FhirPackages;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Runs <see cref="SqlServerSearchParameterPackageWriter"/> against a fresh SQL database per test, so each test starts
    /// from an empty store. The applied-package record store is in memory until its SQL tables exist.
    /// </summary>
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlServerSearchParameterPackageWriterTests : IAsyncLifetime
    {
        private const string PatientIdentifierSystem = "http://partner.example/mrn";

        private readonly SqlServerFhirStorageTestsFixture _sqlFixture;
        private readonly FhirStorageTestsFixture _fixture;
        private readonly InMemoryRecordStore _recordStore = new InMemoryRecordStore();

        public SqlServerSearchParameterPackageWriterTests()
        {
            _sqlFixture = new SqlServerFhirStorageTestsFixture(SchemaVersionConstants.Max, SqlServerFhirStorageTestsFixture.GetDatabaseName("SpPackageWriter"));
            _fixture = new FhirStorageTestsFixture(_sqlFixture);
        }

        public async ValueTask InitializeAsync()
        {
            await _fixture.InitializeAsync();
            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await _fixture.DisposeAsync();
        }

        [Fact]
        public async Task GivenAnEmptyStore_WhenAPackageIsWritten_ThenEveryParameterIsEnabledAndSearchableWithoutReindex()
        {
            // Arrange
            string url = NewUrl();
            string code = NewCode();

            // Act
            await WriteAsync(Package("partner.a"), CurrentToken(), (url, code));

            // Assert
            Assert.Equal(SearchParameterStatus.Enabled, await GetStatusAsync(url));
            Assert.True(_fixture.SearchParameterDefinitionManager.TryGetSearchParameter(url, out SearchParameterInfo info));
            Assert.Equal(SearchParameterStatus.Enabled, info.SearchParameterStatus);
            Assert.Equal(1, await CountSearchParameterResourcesAsync(url));
            Assert.Single(_recordStore.Records);
            Assert.Equal(0, await CountReindexJobsAsync());

            // The fixture has no real search indexer, so supply the index entry the indexer would extract for the new
            // parameter. This proves the store accepts and searches by the parameter with no reindex.
            await UpsertPatientAsync(info, "123");
            await UpsertPatientAsync(info, "456");

            SearchResult result = await _fixture.SearchService.SearchAsync(
                KnownResourceTypes.Patient,
                new[] { Tuple.Create(code, $"{PatientIdentifierSystem}|123") },
                CancellationToken.None);

            Assert.Empty(result.UnsupportedSearchParameters);
            Assert.Single(result.Results);
        }

        [Fact]
        public async Task GivenAStringParameter_WhenAPackageIsWritten_ThenSortIsEnabledWithoutReindex()
        {
            // Arrange
            string url = NewUrl();
            ResourceElement resource = SearchParameterResource(url, NewCode(), SearchParamType.String, "Patient.name.family");

            // Act
            await CreateWriter().WriteAsync(Package("partner.a"), new[] { resource }, CurrentToken(), CancellationToken.None);

            // Assert
            Assert.True(_fixture.SearchParameterDefinitionManager.TryGetSearchParameter(url, out SearchParameterInfo info));
            Assert.Equal(SortParameterStatus.Enabled, info.SortStatus);
        }

        [Fact]
        public async Task GivenAStoreWithData_WhenAPackageIsWritten_ThenTheStoreNotEmptyStatusIsReturnedAndNothingIsWritten()
        {
            // Arrange
            await _fixture.Mediator.UpsertResourceAsync(new Patient().ToResourceElement());
            string url = NewUrl();

            // Act
            await Assert.ThrowsAsync<FhirPackageStoreNotEmptyException>(() => WriteAsync(Package("partner.a"), CurrentToken(), (url, NewCode())));

            // Assert
            await AssertNothingWrittenAsync(url, "partner.a");
        }

        [Fact]
        public async Task GivenTheRecordWriteFails_WhenAPackageIsWritten_ThenNothingIsWritten()
        {
            // Arrange
            string url = NewUrl();
            _recordStore.FailNextAdd = true;

            // Act
            await Assert.ThrowsAsync<InvalidOperationException>(() => WriteAsync(Package("partner.a"), CurrentToken(), (url, NewCode())));

            // Assert
            await AssertNothingWrittenAsync(url, "partner.a");
        }

        [Fact]
        public async Task GivenOnlySearchParametersFromAnEarlierPackage_WhenAnotherPackageIsWritten_ThenTheStoreCountsAsEmpty()
        {
            // Arrange
            string firstUrl = NewUrl();
            string secondUrl = NewUrl();
            await WriteAsync(Package("partner.a"), CurrentToken(), (firstUrl, NewCode()));

            // Act
            await WriteAsync(Package("partner.b"), CurrentToken(), (secondUrl, NewCode()));

            // Assert
            Assert.Equal(SearchParameterStatus.Enabled, await GetStatusAsync(firstUrl));
            Assert.Equal(SearchParameterStatus.Enabled, await GetStatusAsync(secondUrl));
            Assert.Equal(2, _recordStore.Records.Count);
        }

        [Fact]
        public async Task GivenAReindexIsRunning_WhenAPackageIsWritten_ThenARetryableConflictIsReturnedAndNothingIsWritten()
        {
            // Arrange
            string url = NewUrl();
            long jobId = await CreateReindexJobAsync();

            try
            {
                // Act
                FhirPackageWriteConflictException ex = await Assert.ThrowsAsync<FhirPackageWriteConflictException>(
                    () => WriteAsync(Package("partner.a"), CurrentToken(), (url, NewCode())));

                // Assert
                Assert.True(ex.IsRetryable);
                await AssertNothingWrittenAsync(url, "partner.a");
            }
            finally
            {
                await CancelReindexJobAsync(jobId);
            }
        }

        [Fact]
        public async Task GivenAPackageWasWritten_WhenAReindexStartsAfterwards_ThenTheParametersStayEnabledAndLaterWritesConflict()
        {
            // Arrange
            string url = NewUrl();
            string laterUrl = NewUrl();
            await WriteAsync(Package("partner.a"), CurrentToken(), (url, NewCode()));

            // Act
            long jobId = await CreateReindexJobAsync();

            try
            {
                // Assert
                Assert.Equal(SearchParameterStatus.Enabled, await GetStatusAsync(url));

                FhirPackageWriteConflictException ex = await Assert.ThrowsAsync<FhirPackageWriteConflictException>(
                    () => WriteAsync(Package("partner.b"), CurrentToken(), (laterUrl, NewCode())));
                Assert.True(ex.IsRetryable);
                await AssertNothingWrittenAsync(laterUrl, "partner.b");
            }
            finally
            {
                await CancelReindexJobAsync(jobId);
            }
        }

        [Fact]
        public async Task GivenTheReferenceSetChangedSinceValidation_WhenAPackageIsWritten_ThenARetryableConflictIsReturnedAndNothingIsWritten()
        {
            // Arrange
            DateTimeOffset staleToken = CurrentToken();
            await WriteAsync(Package("partner.a"), staleToken, (NewUrl(), NewCode()));
            string url = NewUrl();

            // Act
            FhirPackageWriteConflictException ex = await Assert.ThrowsAsync<FhirPackageWriteConflictException>(
                () => WriteAsync(Package("partner.b"), staleToken, (url, NewCode())));

            // Assert
            Assert.True(ex.IsRetryable);
            await AssertNothingWrittenAsync(url, "partner.b");
        }

        [Fact]
        public async Task GivenAnUnownedSearchParameterWithTheSameUrl_WhenAPackageIsWritten_ThenANonRetryableConflictIsReturnedAndNothingIsWritten()
        {
            // Arrange: a SearchParameter created outside any package, with another id, already owns the url.
            string url = NewUrl();
            await _fixture.SearchParameterStatusDataStore.UpsertStatuses(
                new[] { new ResourceSearchParameterStatus { Uri = new Uri(url), Status = SearchParameterStatus.Supported, LastUpdated = CurrentToken() } },
                CancellationToken.None);
            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            // Act
            FhirPackageWriteConflictException ex = await Assert.ThrowsAsync<FhirPackageWriteConflictException>(
                () => WriteAsync(Package("partner.a"), CurrentToken(), (url, NewCode())));

            // Assert
            Assert.False(ex.IsRetryable);
            Assert.Contains(url, ex.Message, StringComparison.Ordinal);
            Assert.Equal(SearchParameterStatus.Supported, await GetStatusAsync(url));
            Assert.Equal(0, await CountSearchParameterResourcesAsync(url));
            Assert.Empty(_recordStore.Records);
        }

        [Fact]
        public async Task GivenTheSamePackageIsWrittenTwice_WhenTheSecondWriteRuns_ThenItIsANoOp()
        {
            // Arrange
            string url = NewUrl();
            string code = NewCode();
            FhirPackage package = Package("partner.a");
            await WriteAsync(package, CurrentToken(), (url, code));

            // Act
            await WriteAsync(package, CurrentToken(), (url, code));

            // Assert
            Assert.Single(_recordStore.Records);
            Assert.Equal(1, await CountSearchParameterResourcesAsync(url));
            Assert.Equal(SearchParameterStatus.Enabled, await GetStatusAsync(url));
        }

        [Fact]
        public async Task GivenAnotherWriterInstalledTheSamePackageAfterThisOneValidated_WhenThisOneWrites_ThenItIsANoOp()
        {
            // Arrange: both writers validated against the same token; this one read the record before the other committed.
            string url = NewUrl();
            string code = NewCode();
            FhirPackage package = Package("partner.a");
            DateTimeOffset sharedToken = CurrentToken();
            await WriteAsync(package, sharedToken, (url, code));
            _recordStore.HideRecordsForNextReads(1);

            // Act
            await WriteAsync(package, sharedToken, (url, code));

            // Assert
            Assert.Single(_recordStore.Records);
            Assert.Equal(1, await CountSearchParameterResourcesAsync(url));
            Assert.Equal(SearchParameterStatus.Enabled, await GetStatusAsync(url));
        }

        [Fact]
        public async Task GivenAnotherWriterInstalledTheSamePackageBeforeDataArrived_WhenThisOneWrites_ThenItIsANoOp()
        {
            // Arrange: this writer read the record before the other committed, and data arrived before its empty-store check.
            string url = NewUrl();
            string code = NewCode();
            FhirPackage package = Package("partner.a");
            await WriteAsync(package, CurrentToken(), (url, code));
            await _fixture.Mediator.UpsertResourceAsync(new Patient().ToResourceElement());
            _recordStore.HideRecordsForNextReads(1);

            // Act
            await WriteAsync(package, CurrentToken(), (url, code));

            // Assert
            Assert.Single(_recordStore.Records);
            Assert.Equal(1, await CountSearchParameterResourcesAsync(url));
        }

        [Fact]
        public async Task GivenNoRequestContext_WhenAPackageIsWritten_ThenItIsWrittenAndTheContextIsRestored()
        {
            // Arrange: package convergence runs at startup, outside any HTTP request.
            string url = NewUrl();
            IFhirRequestContext originalContext = _fixture.FhirRequestContextAccessor.RequestContext;
            _fixture.FhirRequestContextAccessor.RequestContext = null;

            try
            {
                // Act
                await WriteAsync(Package("partner.a"), CurrentToken(), (url, NewCode()));

                // Assert
                Assert.Null(_fixture.FhirRequestContextAccessor.RequestContext);
                Assert.Equal(SearchParameterStatus.Enabled, await GetStatusAsync(url));
                Assert.Equal("POST", await GetRequestMethodAsync(url));
            }
            finally
            {
                _fixture.FhirRequestContextAccessor.RequestContext = originalContext;
            }
        }

        private static string NewUrl() => $"http://partner.example/SearchParameter/{Guid.NewGuid():N}";

        private static string NewCode() => $"p{Guid.NewGuid():N}".Substring(0, 20);

        private static FhirPackage Package(string packageId) =>
            new FhirPackage(new FhirPackageDescriptor(packageId, "1.0.0", $"sha512-{packageId}"), Array.Empty<FhirPackageEntry>());

        // Package-shaped: the package declares the id, and the writer must add meta.lastUpdated itself.
        private static ResourceElement SearchParameterResource(string url, string code, SearchParamType type = SearchParamType.Token, string expression = "Patient.identifier")
        {
            return new SearchParameter
            {
                Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant(),
                Url = url,
                Name = code,
                Status = PublicationStatus.Active,
                Code = code,
                Description = "Partner identifier.",
#if R5
                Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#else
                Base = new List<ResourceType?> { ResourceType.Patient },
#endif
                Type = type,
                Expression = expression,
            }.ToResourceElement();
        }

        private DateTimeOffset CurrentToken() => _fixture.SearchParameterOperations.SearchParamLastUpdated;

        private SqlServerSearchParameterPackageWriter CreateWriter()
        {
            // Mirrors the fixture's fallback indexer: the SearchParameter cache refresh finds new parameters by url.
            SearchParameterInfo urlParameter = _fixture.SearchParameterDefinitionManager.AllSearchParameters
                .First(p => p.Code == "url" && p.BaseResourceTypes?.Contains(KnownResourceTypes.SearchParameter) == true);
            var searchIndexer = Substitute.For<ISearchIndexer>();
            searchIndexer.Extract(Arg.Any<ResourceElement>()).Returns(call => new List<SearchIndexEntry>
            {
                new SearchIndexEntry(urlParameter, new UriSearchValue(call.Arg<ResourceElement>().ToPoco<SearchParameter>().Url, false)),
            });
            var compartmentIndexer = Substitute.For<ICompartmentIndexer>();
            compartmentIndexer.Extract(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<SearchIndexEntry>>()).Returns(new CompartmentIndices());

            var resourceWrapperFactory = new ResourceWrapperFactory(
                new RawResourceFactory(new FhirJsonSerializer()),
                _fixture.FhirRequestContextAccessor,
                searchIndexer,
                Substitute.For<IClaimsExtractor>(),
                compartmentIndexer,
                _fixture.SearchParameterDefinitionManager,
                _fixture.Deserializer);

            return new SqlServerSearchParameterPackageWriter(
                _fixture.DataStore,
                _fixture.TransactionHandler,
                _sqlFixture.SqlConnectionWrapperFactory,
                _sqlFixture.SqlServerFhirModel,
                _recordStore,
                resourceWrapperFactory,
                ModelInfoProvider.Instance,
                _fixture.SearchParameterOperations,
                _fixture.FhirRequestContextAccessor,
                NullLogger<SqlServerSearchParameterPackageWriter>.Instance);
        }

        private Task WriteAsync(FhirPackage package, DateTimeOffset token, params (string Url, string Code)[] parameters)
        {
            ResourceElement[] resources = parameters.Select(x => SearchParameterResource(x.Url, x.Code)).ToArray();
            return CreateWriter().WriteAsync(package, resources, token, CancellationToken.None);
        }

        private async Task<SearchParameterStatus?> GetStatusAsync(string url)
        {
            IReadOnlyCollection<ResourceSearchParameterStatus> statuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
            return statuses.FirstOrDefault(x => x.Uri.OriginalString == url)?.Status;
        }

        private async Task AssertNothingWrittenAsync(string url, string packageId)
        {
            Assert.Null(await GetStatusAsync(url));
            Assert.Equal(0, await CountSearchParameterResourcesAsync(url));
            Assert.DoesNotContain(_recordStore.Records, x => x.PackageId == packageId);
        }

        private async Task UpsertPatientAsync(SearchParameterInfo identifierParameter, string value)
        {
            var patient = new Patient
            {
                Id = Guid.NewGuid().ToString(),
                Meta = new Meta { LastUpdated = DateTimeOffset.UtcNow },
                Identifier = { new Identifier(PatientIdentifierSystem, value) },
            };

            var wrapper = new ResourceWrapper(
                patient.ToResourceElement(),
                new RawResource(patient.ToJson(), FhirResourceFormat.Json, isMetaSet: false),
                new ResourceRequest("PUT"),
                false,
                new[] { new SearchIndexEntry(identifierParameter, new TokenSearchValue(PatientIdentifierSystem, value, null)) },
                new CompartmentIndices(),
                new List<KeyValuePair<string, string>>(),
                "hash");

            await _fixture.DataStore.UpsertAsync(new ResourceWrapperOperation(wrapper, true, true, null, false, false, bundleResourceContext: null), CancellationToken.None);
        }

        private async Task<int> CountSearchParameterResourcesAsync(string url)
        {
            string id = SearchParameterResource(url, "x").Id;
            return await ExecuteScalarAsync(
                "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceTypeId = (SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'SearchParameter') AND ResourceId = @Id",
                new SqlParameter("@Id", id));
        }

        private Task<int> CountReindexJobsAsync() =>
            ExecuteScalarAsync($"SELECT COUNT(*) FROM dbo.JobQueue WHERE QueueType = {(byte)QueueType.Reindex}");

        private async Task<string> GetRequestMethodAsync(string url)
        {
            await using SqlConnection connection = await _fixture.SqlHelper.GetSqlConnectionAsync();
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT RequestMethod FROM dbo.Resource WHERE ResourceTypeId = (SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'SearchParameter') AND ResourceId = @Id";
            command.Parameters.Add(new SqlParameter("@Id", SearchParameterResource(url, "x").Id));
            return (string)await command.ExecuteScalarAsync();
        }

        private async Task<int> ExecuteScalarAsync(string sql, params SqlParameter[] parameters)
        {
            await using SqlConnection connection = await _fixture.SqlHelper.GetSqlConnectionAsync();
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddRange(parameters);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        private async Task<long> CreateReindexJobAsync()
        {
            var wrapper = await _fixture.OperationDataStore.CreateReindexJobAsync(new ReindexJobRecord(), CancellationToken.None);
            return long.Parse(wrapper.JobRecord.Id);
        }

        private async Task CancelReindexJobAsync(long jobId)
        {
            JobInfo jobInfo = await _fixture.QueueClient.DequeueAsync((byte)QueueType.Reindex, "test-cleanup", 0, CancellationToken.None, jobId);
            if (jobInfo != null)
            {
                jobInfo.Status = JobStatus.Cancelled;
                await _fixture.QueueClient.CompleteJobAsync(jobInfo, false, CancellationToken.None);
            }
        }

        /// <summary>
        /// Stands in for the applied-package record store until its SQL tables exist. Not transactional.
        /// </summary>
        private sealed class InMemoryRecordStore : IFhirSpecificationPackageRecordStore
        {
            private readonly ConcurrentDictionary<string, FhirSpecificationPackageRecord> _records = new ConcurrentDictionary<string, FhirSpecificationPackageRecord>(StringComparer.Ordinal);
            private int _hiddenReads;

            public IReadOnlyCollection<FhirSpecificationPackageRecord> Records => _records.Values.ToList();

            public bool FailNextAdd { get; set; }

            public void HideRecordsForNextReads(int count) => _hiddenReads = count;

            public Task<FhirSpecificationPackageRecord> GetAsync(string packageId, CancellationToken cancellationToken)
            {
                if (Interlocked.Decrement(ref _hiddenReads) >= 0)
                {
                    return Task.FromResult<FhirSpecificationPackageRecord>(null);
                }

                return Task.FromResult(_records.TryGetValue(packageId, out FhirSpecificationPackageRecord record) ? record : null);
            }

            public Task AddAsync(FhirSpecificationPackageRecord record, CancellationToken cancellationToken)
            {
                if (FailNextAdd)
                {
                    FailNextAdd = false;
                    throw new InvalidOperationException("Simulated record write failure.");
                }

                if (!_records.TryAdd(record.PackageId, record))
                {
                    throw new InvalidOperationException($"Package {record.PackageId} already has a record.");
                }

                return Task.CompletedTask;
            }
        }
    }
}
