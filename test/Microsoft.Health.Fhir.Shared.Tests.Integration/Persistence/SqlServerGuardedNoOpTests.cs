// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Executes the authoritative guarded no-op statement, and the merge transaction handling around it, against a
    /// real SQL Server database. The unit tests for the same feature stub the SQL seam, so only these tests prove that
    /// the statement parses, uses the intended indexes and locks, is serialized against a concurrent writer, and that
    /// a guarded operation leaves no open merge transaction behind.
    /// </summary>
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerGuardedNoOpTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private readonly SqlServerFhirStorageTestsFixture _fixture;
        private readonly SqlServerFhirDataStore _store;
        private readonly FhirJsonSerializer _jsonSerializer = new FhirJsonSerializer(null);

        public SqlServerGuardedNoOpTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            _store = fixture.SqlServerFhirDataStore;
        }

        [Fact]
        public async Task GivenAGuardedNoOpUpdate_WhenTheStoredVersionStillMatches_ThenItIsHonoredWithoutCreatingANewVersion()
        {
            // Arrange
            string patientId = Guid.NewGuid().ToString();
            ResourceWrapper wrapper = GetResourceWrapper(Samples.GetDefaultPatient().UpdateId(patientId));
            await _store.MergeAsync(new[] { CreateOperation(wrapper) }, CancellationToken.None);

            // Act - resubmitting byte identical content is a logical no-op, so nothing is sent to dbo.MergeResources
            // and the guarded probe is what settles the comparison.
            MergeOutcome outcome = await _store.MergeAsync(
                new[] { CreateOperation(wrapper, comparedVersion: "1") },
                CancellationToken.None);

            // Assert
            DataStoreOperationOutcome result = outcome.Results.Values.Single();
            Assert.True(result.IsOperationSuccessful);
            Assert.Equal("1", result.UpsertOutcome.Wrapper.Version);

            // The point of a no-op is that it is invisible: no new FHIR version and no history row.
            Assert.Equal(new[] { (Version: 1, IsHistory: false) }, await GetStoredRowsAsync(patientId));
        }

        [Fact]
        public async Task GivenAGuardedNoOpUpdate_WhenTheStoredVersionMovedOn_ThenItFailsItsPreconditionWithoutMutatingTheResource()
        {
            // Arrange - the resource is at version 2 while the caller still guards against version 1.
            string patientId = Guid.NewGuid().ToString();
            ResourceWrapper wrapper = GetResourceWrapper(Samples.GetDefaultPatient().UpdateId(patientId));
            await _store.MergeAsync(new[] { CreateOperation(wrapper) }, CancellationToken.None);
            UpdateResource(wrapper);
            await _store.MergeAsync(new[] { CreateOperation(wrapper) }, CancellationToken.None);

            // Act
            MergeOutcome outcome = await _store.MergeAsync(
                new[] { CreateOperation(wrapper, weakETag: WeakETag.FromVersionId("1")) },
                CancellationToken.None);

            // Assert
            DataStoreOperationOutcome result = outcome.Results.Values.Single();
            Assert.False(result.IsOperationSuccessful);
            Assert.IsType<PreconditionFailedException>(result.Exception);

            // Nothing was written: still exactly the two versions created above.
            Assert.Equal(
                new[] { (Version: 2, IsHistory: false), (Version: 1, IsHistory: true) },
                await GetStoredRowsAsync(patientId));
        }

        [Fact]
        public async Task GivenTheAuthoritativeProbe_WhenAConcurrentWriterHoldsTheRow_ThenItWaitsForThatWriterAndObservesTheCommittedVersion()
        {
            // Arrange - a resource at version 1, and a writer that has taken the row's clustered key lock exactly the
            // way dbo.MergeResources does when it stamps the previous version as history, but has not committed.
            string patientId = Guid.NewGuid().ToString();
            ResourceWrapper wrapper = GetResourceWrapper(Samples.GetDefaultPatient().UpdateId(patientId));
            await _store.MergeAsync(new[] { CreateOperation(wrapper) }, CancellationToken.None);

            short resourceTypeId = _fixture.SqlServerFhirModel.GetResourceTypeId("Patient");
            long surrogateId = (await GetStoredRowIdsAsync(patientId)).Single();
            var probeKeys = new[] { new ResourceKeyListRow(resourceTypeId, patientId, null) };

            await using var writerConnection = new SqlConnection(_fixture.TestConnectionString);
            await writerConnection.OpenAsync(CancellationToken.None);
            await using SqlTransaction writerTransaction = (SqlTransaction)await writerConnection.BeginTransactionAsync(CancellationToken.None);

            await using (var writerCommand = new SqlCommand(
                "UPDATE dbo.Resource SET Version = 2 WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId = @ResourceSurrogateId",
                writerConnection,
                writerTransaction))
            {
                writerCommand.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
                writerCommand.Parameters.AddWithValue("@ResourceSurrogateId", surrogateId);
                Assert.Equal(1, await writerCommand.ExecuteNonQueryAsync(CancellationToken.None));
            }

            // Act
            Task<IReadOnlyList<ResourceDateKey>> probe = _store.StoreClient.GetCurrentResourceVersionsAsync(
                probeKeys,
                acquireUpdateLocks: true,
                enlistedConnection: null,
                CancellationToken.None);

            // Assert - an unlocked read would answer immediately from a pre-write state. This read has to be
            // serialized with the writer, so it must still be waiting while that writer holds the row.
            Task first = await Task.WhenAny(probe, Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None));
            Assert.NotSame(probe, first);
            Assert.False(probe.IsCompleted);

            await writerTransaction.CommitAsync(CancellationToken.None);

            ResourceDateKey observed = Assert.Single(await probe);
            Assert.Equal("2", observed.VersionId);
        }

        [Fact]
        public async Task GivenASequentialTransactionBundle_WhenAGuardedNoOpIsHonored_ThenNoVersionIsCreatedAndNoMergeTransactionIsLeftOpen()
        {
            // Arrange - a sequential transaction bundle runs its merges enlisted in an ambient transaction, so the
            // guarded probe runs on that transaction's own connection and its locks are held until the bundle commits.
            string patientId = Guid.NewGuid().ToString();
            ResourceWrapper wrapper = GetResourceWrapper(Samples.GetDefaultPatient().UpdateId(patientId));
            await _store.MergeAsync(new[] { CreateOperation(wrapper) }, CancellationToken.None);

            int openTransactionsBefore = await CountOpenMergeTransactionsAsync();

            // Act
            using (var transactionScope = _fixture.SqlTransactionHandler.BeginTransaction())
            {
                MergeOutcome outcome = await _store.MergeAsync(
                    new[] { CreateOperation(wrapper, comparedVersion: "1") },
                    new MergeOptions(enlistTransaction: true, isBundleTransaction: true),
                    CancellationToken.None);

                Assert.True(outcome.Results.Values.Single().IsOperationSuccessful);

                // The probe must hold an update lock on the resource for as long as the bundle's transaction lives,
                // which is what makes the comparison atomic with the rest of the bundle.
                await AssertRowLockIsHeldAsync(patientId);

                transactionScope.Complete();
            }

            // Assert
            Assert.Equal(new[] { (Version: 1, IsHistory: false) }, await GetStoredRowsAsync(patientId));
            Assert.Equal(openTransactionsBefore, await CountOpenMergeTransactionsAsync());

            // The bundle's transaction is over, so the probe's lock must be gone with it.
            await AssertRowIsNotLockedAsync(patientId);
        }

        [Fact]
        public async Task GivenASequentialTransactionBundle_WhenTheGuardedProbeCannotObtainItsLock_ThenItFailsAsRetryableContentionAndLeavesNoOpenMergeTransaction()
        {
            // Arrange - a writer holds the resource's row and does not commit, so the probe cannot settle its
            // comparison. Its lock request is bounded and comes back as SQL error 1222, which says nothing about
            // whether the caller's version is stale: reporting 412 would fabricate a stale precondition and letting
            // the SqlException escape would report a 500 for ordinary contention.
            string patientId = Guid.NewGuid().ToString();
            ResourceWrapper wrapper = GetResourceWrapper(Samples.GetDefaultPatient().UpdateId(patientId));
            await _store.MergeAsync(new[] { CreateOperation(wrapper) }, CancellationToken.None);

            short resourceTypeId = _fixture.SqlServerFhirModel.GetResourceTypeId("Patient");
            long surrogateId = (await GetStoredRowIdsAsync(patientId)).Single();

            await using var writerConnection = new SqlConnection(_fixture.TestConnectionString);
            await writerConnection.OpenAsync(CancellationToken.None);
            await using SqlTransaction writerTransaction = (SqlTransaction)await writerConnection.BeginTransactionAsync(CancellationToken.None);

            await using (var writerCommand = new SqlCommand(
                "UPDATE dbo.Resource SET SearchParamHash = 'held' WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId = @ResourceSurrogateId",
                writerConnection,
                writerTransaction))
            {
                writerCommand.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
                writerCommand.Parameters.AddWithValue("@ResourceSurrogateId", surrogateId);
                Assert.Equal(1, await writerCommand.ExecuteNonQueryAsync(CancellationToken.None));
            }

            int openTransactionsBefore = await CountOpenMergeTransactionsAsync();

            // Act
            using (var transactionScope = _fixture.SqlTransactionHandler.BeginTransaction())
            {
                await Assert.ThrowsAsync<TransactionDeadlockException>(() => _store.MergeAsync(
                    new[] { CreateOperation(wrapper, comparedVersion: "1") },
                    new MergeOptions(enlistTransaction: true, isBundleTransaction: true),
                    CancellationToken.None));
            }

            await writerTransaction.RollbackAsync(CancellationToken.None);

            // Assert
            Assert.Equal(openTransactionsBefore, await CountOpenMergeTransactionsAsync());
            Assert.Equal(new[] { (Version: 1, IsHistory: false) }, await GetStoredRowsAsync(patientId));
        }

        [Fact]
        public async Task GivenASequentialTransactionBundle_WhenAGuardedOperationFailsItsPrecondition_ThenNoMergeTransactionIsLeftOpen()
        {
            // Arrange - a bundle transaction that contains a failed operation never reaches dbo.MergeResources, which
            // is what normally settles the transaction id handed out at the start of the merge.
            string patientId = Guid.NewGuid().ToString();
            ResourceWrapper wrapper = GetResourceWrapper(Samples.GetDefaultPatient().UpdateId(patientId));
            await _store.MergeAsync(new[] { CreateOperation(wrapper) }, CancellationToken.None);

            int openTransactionsBefore = await CountOpenMergeTransactionsAsync();

            // Act
            using (var transactionScope = _fixture.SqlTransactionHandler.BeginTransaction())
            {
                MergeOutcome outcome = await _store.MergeAsync(
                    new[] { CreateOperation(wrapper, weakETag: WeakETag.FromVersionId("99")) },
                    new MergeOptions(enlistTransaction: true, isBundleTransaction: true),
                    CancellationToken.None);

                Assert.IsType<PreconditionFailedException>(outcome.Results.Values.Single().Exception);
            }

            // Assert - the failed bundle must not leave an open transaction behind for the watchdog to time out.
            Assert.Equal(openTransactionsBefore, await CountOpenMergeTransactionsAsync());
            Assert.Equal(new[] { (Version: 1, IsHistory: false) }, await GetStoredRowsAsync(patientId));
        }

        private static ResourceWrapperOperation CreateOperation(ResourceWrapper wrapper, WeakETag weakETag = null, string comparedVersion = null)
        {
            return new ResourceWrapperOperation(
                wrapper,
                allowCreate: true,
                keepHistory: true,
                weakETag: weakETag,
                requireETagOnUpdate: false,
                keepVersion: false,
                bundleResourceContext: null,
                comparedVersion: comparedVersion);
        }

        private static void UpdateResource(ResourceWrapper resource)
        {
            resource.RawResource = new RawResource(
                resource.RawResource.Data.Replace("\"birthDate\":\"1974-12-25\"", "\"birthDate\":\"2000-01-01\"", StringComparison.Ordinal),
                FhirResourceFormat.Json,
                true);
        }

        private ResourceWrapper GetResourceWrapper(ResourceElement resource)
        {
            var poco = resource.ToPoco();
            poco.VersionId = "1";
            poco.Meta ??= new Meta();
            poco.Meta.LastUpdated = DateTime.UtcNow;

            var raw = new RawResource(_jsonSerializer.SerializeToString(poco), FhirResourceFormat.Json, true);
            return new ResourceWrapper(resource, raw, new ResourceRequest("Merge"), false, null, null, null, "hash")
            {
                LastModified = DateTime.UtcNow,
            };
        }

        private async Task<IReadOnlyList<(int Version, bool IsHistory)>> GetStoredRowsAsync(string resourceId)
        {
            var rows = new List<(int Version, bool IsHistory)>();
            await ReadAsync(
                "SELECT Version, IsHistory FROM dbo.Resource WHERE ResourceTypeId = @ResourceTypeId AND ResourceId = @ResourceId ORDER BY Version DESC",
                resourceId,
                reader => rows.Add((reader.GetInt32(0), reader.GetBoolean(1))));
            return rows;
        }

        private async Task<IReadOnlyList<long>> GetStoredRowIdsAsync(string resourceId)
        {
            var rows = new List<long>();
            await ReadAsync(
                "SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceTypeId = @ResourceTypeId AND ResourceId = @ResourceId AND IsHistory = 0",
                resourceId,
                reader => rows.Add(reader.GetInt64(0)));
            return rows;
        }

        /// <summary>
        /// Proves that no update lock is still held on the resource's current row by reading it through the clustered
        /// index - the lock resource the guarded probe claims - with a lock request that fails immediately rather than
        /// waiting.
        /// </summary>
        private async Task AssertRowIsNotLockedAsync(string resourceId)
        {
            var rows = new List<int>();
            await ReadAsync(
                @"SET LOCK_TIMEOUT 0
SELECT B.Version
  FROM dbo.Resource A
       JOIN dbo.Resource B WITH (ROWLOCK, UPDLOCK, INDEX = PKC_Resource)
         ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  WHERE A.ResourceTypeId = @ResourceTypeId AND A.ResourceId = @ResourceId AND A.IsHistory = 0",
                resourceId,
                reader => rows.Add(reader.GetInt32(0)));
            Assert.NotEmpty(rows);
        }

        /// <summary>
        /// Proves that an update lock is held on the resource's current row: the same read fails immediately with
        /// SQL error 1222 instead of returning it.
        /// </summary>
        private async Task AssertRowLockIsHeldAsync(string resourceId)
        {
            SqlException exception = await Assert.ThrowsAsync<SqlException>(() => AssertRowIsNotLockedAsync(resourceId));
            Assert.Equal(1222, exception.Number);
        }

        private async Task ReadAsync(string commandText, string resourceId, Action<SqlDataReader> readRow)
        {
            using SqlConnectionWrapper connection = await _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false);
            using SqlCommandWrapper command = connection.CreateRetrySqlCommand();
            command.CommandText = commandText;
            command.Parameters.AddWithValue("@ResourceTypeId", _fixture.SqlServerFhirModel.GetResourceTypeId("Patient"));
            command.Parameters.AddWithValue("@ResourceId", resourceId);

            using SqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None);
            while (await reader.ReadAsync(CancellationToken.None))
            {
                readRow(reader);
            }
        }

        private async Task<int> CountOpenMergeTransactionsAsync()
        {
            using SqlConnectionWrapper connection = await _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false);
            using SqlCommandWrapper command = connection.CreateRetrySqlCommand();
            command.CommandText = "SELECT count(*) FROM dbo.Transactions WHERE IsCompleted = 0";
            return Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None), CultureInfo.InvariantCulture);
        }
    }
}
