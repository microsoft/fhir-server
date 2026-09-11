// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public sealed class SemanticSearchTests : IClassFixture<SemanticSearchTestFixture>
    {
        private const string Query = "difficulty breathing after exercise";
        private readonly SemanticSearchTestFixture _fixture;
        private readonly TestFhirClient _client;

        public SemanticSearchTests(SemanticSearchTestFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.TestFhirClient;
        }

        [Fact]
        public async Task GivenEmbeddingIsDelayed_WhenWritingResource_ThenTransactionHeartbeatAdvancesBeforeEmbeddingCompletes()
        {
            await EnsureSearchParameterIsEnabledAsync(
                "observation-semantic",
                SemanticSearchTestParameterResolver.ObservationCanonical,
                "ObservationSemantic",
                ResourceType.Observation,
                "Observation.note.text");

            BlockingDeterministicEmbeddingClient embeddingClient = _fixture.GetService<BlockingDeterministicEmbeddingClient>();
            Task embeddingStarted = embeddingClient.WaitForBlockedEmbeddingAsync();
            DateTime testStarted = DateTime.UtcNow;
            Task<Observation> createTask = CreateAsync(CreateObservation(Query, ObservationStatus.Final));

            try
            {
                await embeddingStarted.WaitAsync(TimeSpan.FromSeconds(30));

                (long transactionId, DateTime initialHeartbeat) = await GetActiveTransactionAsync(_fixture.ConnectionString, testStarted);

                using var heartbeatTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                DateTime currentHeartbeat = initialHeartbeat;
                while (currentHeartbeat <= initialHeartbeat)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200), heartbeatTimeout.Token);
                    currentHeartbeat = await GetTransactionHeartbeatAsync(_fixture.ConnectionString, transactionId, heartbeatTimeout.Token);
                }

                Assert.True(currentHeartbeat > initialHeartbeat);
            }
            finally
            {
                embeddingClient.ReleaseBlockedEmbedding();
            }

            await createTask;
        }

        [Fact]
        public async Task GivenRequestIsCancelledDuringEmbedding_WhenWritingResource_ThenCancellationIsPropagated()
        {
            await EnsureSearchParameterIsEnabledAsync(
                "observation-semantic",
                SemanticSearchTestParameterResolver.ObservationCanonical,
                "ObservationSemantic",
                ResourceType.Observation,
                "Observation.note.text");

            BlockingDeterministicEmbeddingClient embeddingClient = _fixture.GetService<BlockingDeterministicEmbeddingClient>();
            Task embeddingStarted = embeddingClient.WaitForBlockedEmbeddingAsync();
            using var cancellationTokenSource = new CancellationTokenSource();
            Task<Observation> createTask = CreateAsync(
                CreateObservation(Query, ObservationStatus.Final),
                cancellationTokenSource.Token);

            try
            {
                await embeddingStarted.WaitAsync(TimeSpan.FromSeconds(30));
                cancellationTokenSource.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => createTask.WaitAsync(TimeSpan.FromSeconds(30)));
            }
            finally
            {
                embeddingClient.ReleaseBlockedEmbedding();
            }
        }

        [Fact]
        public async Task GivenHeartbeatSqlFailsDuringEmbedding_WhenWritingResource_ThenRequestFailsPromptly()
        {
            await EnsureSearchParameterIsEnabledAsync(
                "observation-semantic",
                SemanticSearchTestParameterResolver.ObservationCanonical,
                "ObservationSemantic",
                ResourceType.Observation,
                "Observation.note.text");

            BlockingDeterministicEmbeddingClient embeddingClient = _fixture.GetService<BlockingDeterministicEmbeddingClient>();
            Task embeddingStarted = embeddingClient.WaitForBlockedEmbeddingAsync();
            DateTime testStarted = DateTime.UtcNow;
            Task<Observation> createTask = CreateAsync(CreateObservation(Query, ObservationStatus.Final));
            SqlTransaction blockingTransaction = null;

            try
            {
                await embeddingStarted.WaitAsync(TimeSpan.FromSeconds(30));
                (long transactionId, _) = await GetActiveTransactionAsync(_fixture.ConnectionString, testStarted);
                blockingTransaction = await LockTransactionHeartbeatAsync(_fixture.ConnectionString, transactionId);

                FhirClientException exception = await Assert.ThrowsAsync<FhirClientException>(
                    () => createTask.WaitAsync(TimeSpan.FromSeconds(30)));

                Assert.Equal(HttpStatusCode.RequestTimeout, exception.StatusCode);
            }
            finally
            {
                if (blockingTransaction != null)
                {
                    SqlConnection connection = blockingTransaction.Connection;
                    await blockingTransaction.RollbackAsync();
                    await blockingTransaction.DisposeAsync();
                    await connection.DisposeAsync();
                }

                embeddingClient.ReleaseBlockedEmbedding();
            }
        }

        [Fact]
        public async Task GivenDirectTextResources_WhenOrdinarySearchIsInvoked_ThenFiltersScoresAndPagingArePreserved()
        {
            await EnsureSearchParameterIsEnabledAsync(
                "observation-semantic",
                SemanticSearchTestParameterResolver.ObservationCanonical,
                "ObservationSemantic",
                ResourceType.Observation,
                "Observation.note.text");

            Observation first = await CreateAsync(CreateObservation(Query, ObservationStatus.Final));
            Observation second = await CreateAsync(CreateObservation(Query, ObservationStatus.Final));
            Observation excluded = await CreateAsync(CreateObservation(Query, ObservationStatus.Preliminary));

            Bundle firstPage = await _client.SearchAsync(
                ResourceType.Observation,
                $"semantic-text={Uri.EscapeDataString(Query)}&status=final&_count=1");

            Bundle.EntryComponent match = Assert.Single(firstPage.Entry, entry => entry.Search.Mode == Bundle.SearchEntryMode.Match);
            Assert.Contains(match.Resource.Id, new[] { first.Id, second.Id });
            Assert.Equal(1m, match.Search.Score);
            Assert.DoesNotContain(firstPage.Entry, entry => entry.Resource.Id == excluded.Id);
            Assert.NotNull(firstPage.NextLink);

            using FhirResponse<Bundle> nextResponse = await _client.SearchAsync(firstPage.NextLink.ToString());
            Bundle.EntryComponent nextMatch = Assert.Single(nextResponse.Resource.Entry, entry => entry.Search.Mode == Bundle.SearchEntryMode.Match);
            Assert.Contains(nextMatch.Resource.Id, new[] { first.Id, second.Id });
            Assert.NotEqual(match.Resource.Id, nextMatch.Resource.Id);
            Assert.Equal(1m, nextMatch.Search.Score);

            first.Note.Clear();
            using FhirResponse<Observation> updateResponse = await _client.UpdateAsync(first);
            Bundle afterEmptyExtraction = await _client.SearchAsync(
                ResourceType.Observation,
                $"semantic-text={Uri.EscapeDataString(Query)}&status=final&_count=10");

            Assert.DoesNotContain(afterEmptyExtraction.Entry, entry => entry.Resource.Id == first.Id);
            Assert.Contains(afterEmptyExtraction.Entry, entry => entry.Resource.Id == second.Id);
        }

        [Fact]
        public async Task GivenNewDirectTextSearchParameter_WhenActivatedAndReindexed_ThenExistingResourceIsSearchable()
        {
            Patient patient = await CreateAsync(new Patient { Active = true });
            Organization payor = await CreateAsync(new Organization { Active = true, Name = "Semantic search test payor" });
            Coverage coverage = await CreateAsync(CreateCoverage(patient.Id, payor.Id, Query));

            await EnsureSearchParameterIsEnabledAsync(
                "coverage-semantic",
                SemanticSearchTestParameterResolver.CoverageCanonical,
                "CoverageSemantic",
                ResourceType.Coverage,
                "Coverage.class.name");

            Bundle bundle = await _client.SearchAsync(
                ResourceType.Coverage,
                $"semantic-text={Uri.EscapeDataString(Query)}&status=active");

            Bundle.EntryComponent match = Assert.Single(bundle.Entry, entry => entry.Search.Mode == Bundle.SearchEntryMode.Match);
            Assert.Equal(coverage.Id, match.Resource.Id);
            Assert.Equal(1m, match.Search.Score);
        }

        private async Task EnsureSearchParameterIsEnabledAsync(
            string id,
            Uri canonical,
            string name,
            ResourceType resourceType,
            string expression)
        {
            var searchParameter = new SearchParameter
            {
                Id = id,
                Url = canonical.ToString(),
                Name = name,
                Status = PublicationStatus.Active,
                Code = "semantic-text",
                Type = SearchParamType.Special,
                Expression = expression,
                Description = new Markdown($"Semantic text for {resourceType}."),
                Base = new ResourceType?[] { resourceType },
                Extension =
                {
                    new Extension
                    {
                        Url = VectorSearchParameterConfig.ExtensionUrl,
                        Extension =
                        {
                            new Extension(VectorSearchParameterConfig.SourceStrategyExtensionUrl, new Code("directText")),
                            new Extension(VectorSearchParameterConfig.ExtractionPolicyExtensionUrl, new Code("perValueRow")),
                        },
                    },
                },
            };

            var updateStopwatch = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    using FhirResponse<SearchParameter> updateResponse = await _client.UpdateAsync(searchParameter);
                    break;
                }
                catch (FhirClientException exception) when (
                    exception.StatusCode == HttpStatusCode.Conflict &&
                    updateStopwatch.Elapsed < TimeSpan.FromMinutes(2))
                {
                    exception.Dispose();
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }

            (_, Uri jobUri) = await _client.PostReindexJobAsync(new Parameters());
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < TimeSpan.FromMinutes(2))
            {
                using FhirResponse<Parameters> jobResponse = await _client.CheckJobAsync(jobUri);
                DataType statusValue = jobResponse.Resource?.Parameter?.FirstOrDefault(parameter => parameter.Name == "status")?.Value;
                string status = statusValue switch
                {
                    Code code => code.Value,
                    FhirString text => text.Value,
                    _ => null,
                };

                if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Assert.False(
                    string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "Canceled", StringComparison.OrdinalIgnoreCase),
                    $"Coverage SearchParameter reindex ended with status '{status}'.");
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            Assert.Fail("Coverage SearchParameter reindex did not complete within two minutes.");
        }

        private static Observation CreateObservation(string text, ObservationStatus status)
        {
            return new Observation
            {
                Status = status,
                Code = new CodeableConcept("http://loinc.org", "75325-1", "Symptom"),
                Note = { new Annotation { Text = text } },
            };
        }

        private static Coverage CreateCoverage(string patientId, string payorId, string text)
        {
            return new Coverage
            {
                Status = FinancialResourceStatusCodes.Active,
                Beneficiary = new ResourceReference($"Patient/{patientId}"),
                Payor = { new ResourceReference($"Organization/{payorId}") },
                Class =
                {
                    new Coverage.ClassComponent
                    {
                        Type = new CodeableConcept("http://terminology.hl7.org/CodeSystem/coverage-class", "plan"),
                        Value = "semantic-plan",
                        Name = text,
                    },
                },
            };
        }

        private async Task<T> CreateAsync<T>(T resource, CancellationToken cancellationToken = default)
            where T : Resource
        {
            using FhirResponse<T> response = await _client.CreateAsync(resource, cancellationToken: cancellationToken);
            return response.Resource;
        }

        private static async Task<(long TransactionId, DateTime Heartbeat)> GetActiveTransactionAsync(string connectionString, DateTime createdAfter)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                @"SELECT TOP (1) SurrogateIdRangeFirstValue, HeartbeatDate
FROM dbo.Transactions
WHERE IsCompleted = 0 AND CreateDate >= @CreatedAfter
ORDER BY SurrogateIdRangeFirstValue DESC;",
                connection);
            command.Parameters.AddWithValue("@CreatedAfter", createdAfter);

            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), "Expected an active resource transaction while embedding was blocked.");
            return (reader.GetInt64(0), reader.GetDateTime(1));
        }

        private static async Task<DateTime> GetTransactionHeartbeatAsync(string connectionString, long transactionId, CancellationToken cancellationToken)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "SELECT HeartbeatDate FROM dbo.Transactions WHERE SurrogateIdRangeFirstValue = @TransactionId;",
                connection);
            command.Parameters.AddWithValue("@TransactionId", transactionId);
            return (DateTime)await command.ExecuteScalarAsync(cancellationToken);
        }

        private static async Task<SqlTransaction> LockTransactionHeartbeatAsync(string connectionString, long transactionId)
        {
            var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            await using var command = new SqlCommand(
                @"UPDATE dbo.Transactions WITH (ROWLOCK)
SET HeartbeatDate = HeartbeatDate
WHERE SurrogateIdRangeFirstValue = @TransactionId;",
                connection,
                transaction);
            command.Parameters.AddWithValue("@TransactionId", transactionId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
            return transaction;
        }
    }
}
