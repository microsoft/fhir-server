// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.FhirPackages;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Scoping;
using Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.SqlServer.Features.FhirPackages;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Runs <see cref="SqlServerSearchParameterPackageWriter"/> against a fresh SQL database per test.
    /// </summary>
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlServerSearchParameterPackageWriterTests : IAsyncLifetime
    {
        private const string PatientIdentifierSystem = "http://partner.example/mrn";

        private readonly SqlServerFhirStorageTestsFixture _sqlFixture;
        private readonly FhirStorageTestsFixture _fixture;

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
            await AssertNothingWrittenAsync(url);
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
            Assert.Equal(1, await CountSearchParameterResourcesAsync(firstUrl));
            Assert.Equal(1, await CountSearchParameterResourcesAsync(secondUrl));
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
                await AssertNothingWrittenAsync(url);
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
                await AssertNothingWrittenAsync(laterUrl);
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
            await AssertNothingWrittenAsync(url);
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
        }

        [Fact]
        public async Task GivenAParameterAlreadyExists_WhenWrittenAgain_ThenTheWriterDoesNotCreateADuplicate()
        {
            // Arrange
            string url = NewUrl();
            string code = NewCode();
            FhirPackage package = Package("partner.a");
            await WriteAsync(package, CurrentToken(), (url, code));

            // Act
            FhirPackageWriteConflictException ex = await Assert.ThrowsAsync<FhirPackageWriteConflictException>(
                () => WriteAsync(package, CurrentToken(), (url, code)));

            // Assert
            Assert.False(ex.IsRetryable);
            Assert.Equal(1, await CountSearchParameterResourcesAsync(url));
            Assert.Equal(SearchParameterStatus.Enabled, await GetStatusAsync(url));
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

        [Fact]
        public async Task GivenACommittedPackageWithNoCacheProofs_WhenTheFailedJobIsRetried_ThenItConvergesWithoutDuplicateResources()
        {
            // Arrange
            string url = NewUrl();
            ResourceElement resource = SearchParameterResource(url, NewCode());
            var package = new FhirPackage(
                new FhirPackageDescriptor("partner.a", "1.0.0", "sha512-test"),
                new[] { new FhirPackageEntry("package/SearchParameter.json", KnownResourceTypes.SearchParameter, new FhirJsonSerializer().SerializeToString(resource.ToPoco<SearchParameter>())) });
            var acquirer = Substitute.For<IFhirPackageAcquirer>();
            acquirer.AcquireAsync("partner.a", "1.0.0", "trusted", Arg.Any<CancellationToken>()).Returns(package);
            var supportResolver = Substitute.For<ISearchParameterSupportResolver>();
            supportResolver.IsSearchParameterSupported(Arg.Any<SearchParameterInfo>()).Returns((true, false));
            var dataStoreValidator = Substitute.For<IDataStoreSearchParameterValidator>();
            dataStoreValidator.ValidateSearchParameter(Arg.Any<SearchParameterInfo>(), out Arg.Any<string>()).Returns(true);
            var configuration = new OperationsConfiguration
            {
                FhirPackageInstall = new FhirPackageInstallJobConfiguration
                {
                    Enabled = true,
                    CacheUpdateMaxWaitMultiplier = 6,
                    ActiveHostsEventsMultiplier = 30,
                },
            };
            using var operations = new SearchParameterOperations(
                _fixture.SearchParameterStatusManager,
                _fixture.SearchParameterDefinitionManager,
                ModelInfoProvider.Instance,
                supportResolver,
                dataStoreValidator,
                _fixture.SearchService.CreateMockScopeProvider(),
                _fixture.DataStore.CreateMockScopeProvider(),
                Options.Create(configuration),
                NullLogger<SearchParameterOperations>.Instance);
            var handler = new SearchParameterPackageHandler(
                _fixture.SearchParameterDefinitionManager,
                operations,
                ModelInfoProvider.Instance,
                new SearchParameterComparer(NullLogger<ISearchParameterComparer<SearchParameterInfo>>.Instance),
                supportResolver,
                dataStoreValidator,
                new ModelAttributeValidator(),
                CreateWriter(operations),
                NullLogger<SearchParameterPackageHandler>.Instance);
            var installer = new FhirPackageInstaller(new[] { handler }, NullLogger<FhirPackageInstaller>.Instance);
            var verifier = new SearchParameterPackageInstallVerifier(
                _fixture.SearchService.CreateMockScopeProvider(),
                operations,
                _fixture.SearchParameterStatusManager,
                _fixture.SearchParameterDefinitionManager,
                ModelInfoProvider.Instance);
            using var services = new ServiceCollection()
                .AddSingleton(acquirer)
                .AddSingleton<IFhirPackageInstaller>(installer)
                .AddSingleton<IFhirPackageInstallVerifier>(verifier)
                .AddSingleton<ISearchParameterDefinitionManager>(_fixture.SearchParameterDefinitionManager)
                .BuildServiceProvider();
            var job = new FhirPackageInstallJob(
                services,
                _fixture.SearchParameterStatusManager,
                Options.Create(configuration),
                Options.Create(new CoreFeatureConfiguration { SearchParameterCacheRefreshIntervalSeconds = 1 }),
                NullLogger<FhirPackageInstallJob>.Instance);
            var enqueuer = new FhirPackageInstallJobEnqueuer(_fixture.QueueClient);
            var definition = new FhirPackageInstallJobDefinition { PackageId = "partner.a", Version = "1.0.0", Source = "trusted" };
            JobInfo queued = await enqueuer.EnqueueAsync(definition, CancellationToken.None);
            Assert.Equal(queued.Id, (await enqueuer.EnqueueAsync(definition, CancellationToken.None)).Id);
            var jobFactory = Substitute.For<IJobFactory>();
            jobFactory.Create(Arg.Any<JobInfo>()).Returns(_ =>
            {
                var scope = Substitute.For<IScoped<IJob>>();
                scope.Value.Returns(job);
                return scope;
            });
            var hosting = new JobHosting(_fixture.QueueClient, jobFactory, Substitute.For<IJobMetricFactory>(), NullLogger<JobHosting>.Instance)
            {
                PollingFrequencyInSeconds = 1,
                JobHeartbeatIntervalInSeconds = 1,
            };
            IFhirRequestContext originalContext = _fixture.FhirRequestContextAccessor.RequestContext;
            _fixture.FhirRequestContextAccessor.RequestContext = null;
            using var stop = new CancellationTokenSource();
            Task host = hosting.ExecuteAsync((byte)QueueType.FhirPackageInstall, 1, "package-integration", stop);
            Task publisher = Task.CompletedTask;

            try
            {
                // Act: let the first job commit and time out, then publish proofs for a distinct retry.
                JobInfo failedJob = await WaitForFinishedJobAsync(queued.Id);
                Assert.Equal(JobStatus.Failed, failedJob.Status);
                FhirPackageInstallJobResult first = JsonConvert.DeserializeObject<FhirPackageInstallJobResult>(failedJob.Result);
                Assert.Equal(queued.Id, (await enqueuer.EnqueueAsync(definition, CancellationToken.None)).Id);
                configuration.FhirPackageInstall.CacheUpdateMaxWaitMultiplier = 15;
                publisher = PublishProofsAsync();
                var retry = new FhirPackageInstallJobDefinition
                {
                    PackageId = definition.PackageId,
                    Version = definition.Version,
                    Source = definition.Source,
                    RetryOfJobId = queued.Id,
                };
                JobInfo retryJob = await enqueuer.EnqueueAsync(retry, CancellationToken.None);
                Assert.NotEqual(queued.Id, retryJob.Id);
                JobInfo completedJob = await WaitForFinishedJobAsync(retryJob.Id);
                Assert.Equal(JobStatus.Completed, completedJob.Status);
                FhirPackageInstallJobResult recovered = JsonConvert.DeserializeObject<FhirPackageInstallJobResult>(completedJob.Result);

                // Assert
                Assert.Equal(FhirPackageInstallJobState.Committed, first.State);
                Assert.True(first.IsRetryable);
                Assert.Equal(FhirPackageInstallJobState.Converged, recovered.State);
                Assert.Equal(new[] { url }, recovered.SearchParameterUrls);
                Assert.Equal(1, await CountSearchParameterResourcesAsync(url));
                Assert.Equal(SearchParameterStatus.Enabled, await GetStatusAsync(url));
                Assert.Null(_fixture.FhirRequestContextAccessor.RequestContext);
            }
            finally
            {
                try
                {
                    await stop.CancelAsync();
                    await Task.WhenAll(host, publisher);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                }
                finally
                {
                    _fixture.FhirRequestContextAccessor.RequestContext = originalContext;
                }
            }

            async Task PublishProofsAsync()
            {
                while (!stop.IsCancellationRequested)
                {
                    await operations.GetAndApplySearchParameterUpdates(stop.Token, zeroWaitForSemaphore: true);
                    await Task.Delay(TimeSpan.FromMilliseconds(250), stop.Token);
                }
            }

            async Task<JobInfo> WaitForFinishedJobAsync(long id)
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(40);
                while (DateTime.UtcNow < deadline)
                {
                    JobInfo current = await _fixture.QueueClient.GetJobByIdAsync((byte)QueueType.FhirPackageInstall, id, true, CancellationToken.None);
                    if (current.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                    {
                        return current;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(100));
                }

                throw new TimeoutException($"Package install job {id} did not finish.");
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

        private SqlServerSearchParameterPackageWriter CreateWriter(ISearchParameterOperations operations = null)
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
                resourceWrapperFactory,
                ModelInfoProvider.Instance,
                operations ?? _fixture.SearchParameterOperations,
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

        private async Task AssertNothingWrittenAsync(string url)
        {
            Assert.Null(await GetStatusAsync(url));
            Assert.Equal(0, await CountSearchParameterResourcesAsync(url));
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
    }
}
