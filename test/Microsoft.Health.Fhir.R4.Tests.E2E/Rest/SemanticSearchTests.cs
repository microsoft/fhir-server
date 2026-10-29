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
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features;
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
        private const string UnrelatedText = "routine follow-up for a sprained ankle";
        private readonly string _testTag = Guid.NewGuid().ToString();
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
            Assert.SkipWhen(!_fixture.IsUsingInProcTestServer, "Requires the in-process host with the deterministic embedding client and direct SQL access.");

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
            Assert.SkipWhen(!_fixture.IsUsingInProcTestServer, "Requires the in-process host with the deterministic embedding client and direct SQL access.");

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
            Assert.SkipWhen(!_fixture.IsUsingInProcTestServer, "Requires the in-process host with the deterministic embedding client and direct SQL access.");

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
            Assert.SkipWhen(!_fixture.IsUsingInProcTestServer, "Requires the in-process host with the deterministic embedding client and direct SQL access.");

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
                $"Observation?semantic-text={Uri.EscapeDataString(Query)}&_tag={_testTag}&status=final&_count=1",
                Tuple.Create(KnownHeaders.Prefer, "handling=strict"));

            Bundle.EntryComponent match = Assert.Single(firstPage.Entry, entry => entry.Search.Mode == Bundle.SearchEntryMode.Match);
            Assert.Contains(match.Resource.Id, new[] { first.Id, second.Id });
            Assert.Equal(1m, match.Search.Score);
            Assert.DoesNotContain(firstPage.Entry, entry => entry.Resource.Id == excluded.Id);
            Assert.NotNull(firstPage.NextLink);

            using FhirResponse<Bundle> nextResponse = await _client.SearchAsync(
                firstPage.NextLink.ToString(),
                Tuple.Create(KnownHeaders.Prefer, "handling=strict"));
            Bundle.EntryComponent nextMatch = Assert.Single(nextResponse.Resource.Entry, entry => entry.Search.Mode == Bundle.SearchEntryMode.Match);
            Assert.Contains(nextMatch.Resource.Id, new[] { first.Id, second.Id });
            Assert.NotEqual(match.Resource.Id, nextMatch.Resource.Id);
            Assert.Equal(1m, nextMatch.Search.Score);

            first.Note.Clear();
            using FhirResponse<Observation> updateResponse = await _client.UpdateAsync(first);
            Bundle afterEmptyExtraction = await _client.SearchAsync(
                $"Observation?semantic-text={Uri.EscapeDataString(Query)}&_tag={_testTag}&status=final&_count=10",
                Tuple.Create(KnownHeaders.Prefer, "handling=strict"));

            Assert.DoesNotContain(afterEmptyExtraction.Entry, entry => entry.Resource.Id == first.Id);
            Assert.Contains(afterEmptyExtraction.Entry, entry => entry.Resource.Id == second.Id);
        }

        [Fact]
        public async Task GivenNewDirectTextSearchParameter_WhenActivatedAndReindexed_ThenExistingResourceIsSearchable()
        {
            Assert.SkipWhen(!_fixture.IsUsingInProcTestServer, "Requires the in-process host with the deterministic embedding client and direct SQL access.");

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
                $"Coverage?semantic-text={Uri.EscapeDataString(Query)}&_tag={_testTag}&status=active",
                Tuple.Create(KnownHeaders.Prefer, "handling=strict"));

            Bundle.EntryComponent match = Assert.Single(bundle.Entry, entry => entry.Search.Mode == Bundle.SearchEntryMode.Match);
            Assert.Equal(coverage.Id, match.Resource.Id);
            Assert.Equal(1m, match.Search.Score);
        }

        [Fact]
        public async Task GivenSemanticSearchWithInclude_WhenPaging_ThenIncludesFollowTheRankedPageOfMatches()
        {
            Assert.SkipWhen(!_fixture.IsUsingInProcTestServer, "Requires the in-process host with the deterministic embedding client and direct SQL access.");

            await EnsureObservationSemanticSearchParameterAsync();

            // Patients have no semantic text, so they have no embeddings and can only appear as included resources.
            Patient farPatient = await CreateAsync(new Patient { Active = true });
            Patient nearPatient = await CreateAsync(new Patient { Active = true });

            // The weaker match is written first so that relevance order differs from storage order.
            Observation far = await CreateAsync(CreateObservation(UnrelatedText, ObservationStatus.Final, farPatient.Id));
            Observation near = await CreateAsync(CreateObservation(Query, ObservationStatus.Final, nearPatient.Id));

            Bundle firstPage = await SearchStrictAsync(
                $"Observation?semantic-text={Uri.EscapeDataString(Query)}&_tag={_testTag}&_include=Observation:subject&_count=1");

            Bundle.EntryComponent firstMatch = Assert.Single(firstPage.Entry, entry => entry.Search.Mode == Bundle.SearchEntryMode.Match);
            Assert.Equal(near.Id, firstMatch.Resource.Id);
            Assert.Equal(1m, firstMatch.Search.Score);
            Bundle.EntryComponent firstInclude = Assert.Single(firstPage.Entry, entry => entry.Search.Mode == Bundle.SearchEntryMode.Include);
            Assert.Equal(nearPatient.Id, firstInclude.Resource.Id);
            Assert.Null(firstInclude.Search.Score);
            Assert.Equal(2, firstPage.Entry.Count);
            Assert.NotNull(firstPage.NextLink);

            Bundle secondPage = await SearchStrictAsync(firstPage.NextLink.ToString());

            Bundle.EntryComponent secondMatch = Assert.Single(secondPage.Entry, entry => entry.Search.Mode == Bundle.SearchEntryMode.Match);
            Assert.Equal(far.Id, secondMatch.Resource.Id);
            Assert.NotNull(secondMatch.Search.Score);
            Assert.True(secondMatch.Search.Score < firstMatch.Search.Score);
            Bundle.EntryComponent secondInclude = Assert.Single(secondPage.Entry, entry => entry.Search.Mode == Bundle.SearchEntryMode.Include);
            Assert.Equal(farPatient.Id, secondInclude.Resource.Id);
            Assert.Equal(2, secondPage.Entry.Count);
        }

        [Fact]
        public async Task GivenSemanticSearchWithRevinclude_WhenSearching_ThenReferencingResourcesWithoutEmbeddingsAreIncluded()
        {
            Assert.SkipWhen(!_fixture.IsUsingInProcTestServer, "Requires the in-process host with the deterministic embedding client and direct SQL access.");

            await EnsureObservationSemanticSearchParameterAsync();

            Observation far = await CreateAsync(CreateObservation(UnrelatedText, ObservationStatus.Final));
            Observation near = await CreateAsync(CreateObservation(Query, ObservationStatus.Final));

            // The reports have no conclusion, so they have no embeddings.
            DiagnosticReport farReport = await CreateAsync(CreateDiagnosticReport(far.Id));
            DiagnosticReport nearReport = await CreateAsync(CreateDiagnosticReport(near.Id));

            Bundle bundle = await SearchStrictAsync(
                $"Observation?semantic-text={Uri.EscapeDataString(Query)}&_tag={_testTag}&_revinclude=DiagnosticReport:result&_count=10");

            Bundle.EntryComponent[] matches = bundle.Entry.Where(entry => entry.Search.Mode == Bundle.SearchEntryMode.Match).ToArray();
            Assert.Equal(new[] { near.Id, far.Id }, matches.Select(entry => entry.Resource.Id));
            Assert.Equal(1m, matches[0].Search.Score);
            Assert.True(matches[1].Search.Score < matches[0].Search.Score);

            Bundle.EntryComponent[] includes = bundle.Entry.Where(entry => entry.Search.Mode == Bundle.SearchEntryMode.Include).ToArray();
            Assert.Equal(
                new[] { farReport.Id, nearReport.Id }.OrderBy(id => id, StringComparer.Ordinal),
                includes.Select(entry => entry.Resource.Id).OrderBy(id => id, StringComparer.Ordinal));
            Assert.All(includes, entry => Assert.Null(entry.Search.Score));
            Assert.Equal(4, bundle.Entry.Count);
        }

        [Fact]
        public async Task GivenSemanticSearchWithIncludeOfAnotherMatch_WhenSearching_ThenTheResourceIsReturnedOnceAsAMatch()
        {
            Assert.SkipWhen(!_fixture.IsUsingInProcTestServer, "Requires the in-process host with the deterministic embedding client and direct SQL access.");

            await EnsureObservationSemanticSearchParameterAsync();

            Observation member = await CreateAsync(CreateObservation(UnrelatedText, ObservationStatus.Final));
            Observation panel = CreateObservation(Query, ObservationStatus.Final);
            panel.HasMember.Add(new ResourceReference($"Observation/{member.Id}"));
            panel = await CreateAsync(panel);

            Bundle bundle = await SearchStrictAsync(
                $"Observation?semantic-text={Uri.EscapeDataString(Query)}&_tag={_testTag}&_include=Observation:has-member&_count=10");

            Assert.Equal(new[] { panel.Id, member.Id }, bundle.Entry.Select(entry => entry.Resource.Id));
            Assert.All(bundle.Entry, entry => Assert.Equal(Bundle.SearchEntryMode.Match, entry.Search.Mode));
            Assert.Equal(1m, bundle.Entry[0].Search.Score);
            Assert.True(bundle.Entry[1].Search.Score < bundle.Entry[0].Search.Score);
        }

        [Fact]
        public async Task GivenSemanticSearchWithSummaryCount_WhenSearching_ThenDistinctMatchedResourcesAreCounted()
        {
            Assert.SkipWhen(!_fixture.IsUsingInProcTestServer, "Requires the in-process host with the deterministic embedding client and direct SQL access.");

            await EnsureObservationSemanticSearchParameterAsync();

            Patient patient = await CreateAsync(new Patient { Active = true });

            // Two notes produce two vector rows for one resource; the count must not count rows.
            Observation multipleNotes = CreateObservation(Query, ObservationStatus.Final, patient.Id);
            multipleNotes.Note.Add(new Annotation { Text = UnrelatedText });
            await CreateAsync(multipleNotes);
            await CreateAsync(CreateObservation(UnrelatedText, ObservationStatus.Final, patient.Id));
            await CreateAsync(CreateObservation(Query, ObservationStatus.Preliminary, patient.Id));

            Bundle count = await SearchStrictAsync(
                $"Observation?semantic-text={Uri.EscapeDataString(Query)}&_tag={_testTag}&status=final&_summary=count");

            Assert.Equal(2, count.Total);
            Assert.Empty(count.Entry);

            Bundle countWithInclude = await SearchStrictAsync(
                $"Observation?semantic-text={Uri.EscapeDataString(Query)}&_tag={_testTag}&_include=Observation:subject&_summary=count");

            Assert.Equal(3, countWithInclude.Total);
            Assert.Empty(countWithInclude.Entry);
        }

        private Task EnsureObservationSemanticSearchParameterAsync()
        {
            return EnsureSearchParameterIsEnabledAsync(
                "observation-semantic",
                SemanticSearchTestParameterResolver.ObservationCanonical,
                "ObservationSemantic",
                ResourceType.Observation,
                "Observation.note.text");
        }

        private async Task<Bundle> SearchStrictAsync(string url)
        {
            using FhirResponse<Bundle> response = await _client.SearchAsync(url, Tuple.Create(KnownHeaders.Prefer, "handling=strict"));
            return response.Resource;
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

        private static Observation CreateObservation(string text, ObservationStatus status, string patientId = null)
        {
            return new Observation
            {
                Status = status,
                Code = new CodeableConcept("http://loinc.org", "75325-1", "Symptom"),
                Subject = patientId == null ? null : new ResourceReference($"Patient/{patientId}"),
                Note = { new Annotation { Text = text } },
            };
        }

        private static DiagnosticReport CreateDiagnosticReport(string observationId)
        {
            return new DiagnosticReport
            {
                Status = DiagnosticReport.DiagnosticReportStatus.Final,
                Code = new CodeableConcept("http://loinc.org", "11502-2", "Laboratory report"),
                Result = { new ResourceReference($"Observation/{observationId}") },
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
            resource.Meta ??= new Meta();
            resource.Meta.Tag.Add(new Coding(null, _testTag));

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
