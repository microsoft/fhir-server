// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Medino;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerFhirDataStoreUnitTests
    {
        /// <summary>
        /// Turns "settlement never returned" into a failed assertion instead of a hung test host. It is far above the
        /// bound under test, so it never decides a result on a correct implementation.
        /// </summary>
        private static readonly TimeSpan UnresponsiveSettlementGuard = TimeSpan.FromSeconds(30);

        public static IEnumerable<object[]> RemoveTrailingZerosTestCases()
        {
            // All zero milliseconds - should remove dot and milliseconds
            yield return new object[] { new DateTimeOffset(2022, 3, 9, 1, 40, 52, 0, TimeSpan.FromHours(2)), "2022-03-09T01:40:52+02:00" };

            // Trailing zeros - should remove only trailing zeros
            yield return new object[] { new DateTimeOffset(2022, 3, 9, 1, 40, 52, 69, TimeSpan.FromHours(2)), "2022-03-09T01:40:52.069+02:00" };

            // No trailing zeros - should return unchanged
            yield return new object[] { new DateTimeOffset(2022, 3, 9, 1, 40, 52, 123, TimeSpan.FromHours(2)).AddTicks(4567), "2022-03-09T01:40:52.1234567+02:00" };

            // Single non-zero digit - should return single digit
            yield return new object[] { new DateTimeOffset(2022, 3, 9, 1, 40, 52, 100, TimeSpan.FromHours(2)), "2022-03-09T01:40:52.1+02:00" };

            // Multiple trailing zeros - should remove all
            yield return new object[] { new DateTimeOffset(2022, 3, 9, 1, 40, 52, 10, TimeSpan.FromHours(2)), "2022-03-09T01:40:52.01+02:00" };

            // Middle non-zero digit - should preserve pattern
            yield return new object[] { new DateTimeOffset(2022, 3, 9, 1, 40, 52, 101, TimeSpan.FromHours(2)), "2022-03-09T01:40:52.101+02:00" };

            // Negative offset - should handle correctly
            yield return new object[] { new DateTimeOffset(2022, 3, 9, 1, 40, 52, 18, TimeSpan.FromHours(-5)), "2022-03-09T01:40:52.018-05:00" };
        }

        [Theory]
        [MemberData(nameof(RemoveTrailingZerosTestCases))]
        public void RemoveTrailingZerosFromMillisecondsForAGivenDate_ShouldFormatCorrectly(DateTimeOffset date, string expected)
        {
            var result = InvokeRemoveTrailingZerosFromMillisecondsForAGivenDate(date);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ChangesAreOnlyInMetadata_WithNoMetaInEither_ShouldReturnTrue()
        {
            var inputWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"id\":\"123\",\"active\":true}");
            var existingWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"id\":\"123\",\"active\":true}");

            var result = InvokeChangesAreOnlyInMetadata(inputWrapper, existingWrapper);

            Assert.True(result);
        }

        [Fact]
        public void ChangesAreOnlyInMetadata_WithMetaInInputOnly_ShouldReturnFalse()
        {
            var inputWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"meta\":{\"versionId\":\"2\"},\"id\":\"123\",\"active\":true}");
            var existingWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"id\":\"123\",\"active\":true}");

            var result = InvokeChangesAreOnlyInMetadata(inputWrapper, existingWrapper);

            Assert.False(result);
        }

        [Fact]
        public void ChangesAreOnlyInMetadata_WithMetaInExistingOnly_ShouldReturnFalse()
        {
            var inputWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"id\":\"123\",\"active\":true}");
            var existingWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"meta\":{\"versionId\":\"1\"},\"id\":\"123\",\"active\":true}");

            var result = InvokeChangesAreOnlyInMetadata(inputWrapper, existingWrapper);

            Assert.False(result);
        }

        [Fact]
        public void ChangesAreOnlyInMetadata_WithDifferentMetaInBoth_ShouldReturnTrue()
        {
            var inputWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"meta\":{\"versionId\":\"2\",\"lastUpdated\":\"2023-01-02T00:00:00Z\"},\"id\":\"123\",\"active\":true}");
            var existingWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"meta\":{\"versionId\":\"1\",\"lastUpdated\":\"2023-01-01T00:00:00Z\"},\"id\":\"123\",\"active\":true}");

            var result = InvokeChangesAreOnlyInMetadata(inputWrapper, existingWrapper);

            Assert.True(result);
        }

        [Fact]
        public void ChangesAreOnlyInMetadata_WithContentChangeOutsideMeta_ShouldReturnFalse()
        {
            var inputWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"meta\":{\"versionId\":\"2\"},\"id\":\"123\",\"active\":false}");
            var existingWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"meta\":{\"versionId\":\"1\"},\"id\":\"123\",\"active\":true}");

            var result = InvokeChangesAreOnlyInMetadata(inputWrapper, existingWrapper);

            Assert.False(result);
        }

        [Fact]
        public void ChangesAreOnlyInMetadata_WithComplexMetaContent_ShouldReturnTrue()
        {
            var inputWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"meta\":{\"versionId\":\"2\",\"lastUpdated\":\"2023-01-02T00:00:00Z\",\"profile\":[\"http://example.com/profile\"],\"tag\":[{\"system\":\"http://example.com\",\"code\":\"test\"}]},\"id\":\"123\",\"active\":true}");
            var existingWrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"meta\":{\"versionId\":\"1\"},\"id\":\"123\",\"active\":true}");

            var result = InvokeChangesAreOnlyInMetadata(inputWrapper, existingWrapper);

            Assert.True(result);
        }

        // Note: GetJsonValue tests require instance creation which has complex dependencies
        // This method is indirectly tested through integration tests (UpdateTests, FhirPathPatchTests)

        private static ResourceWrapper CreateResourceWrapper(string rawResourceData)
        {
            return new ResourceWrapper(
                "123",
                "1",
                "Patient",
                new RawResource(rawResourceData, FhirResourceFormat.Json, isMetaSet: true),
                null,
                DateTimeOffset.UtcNow,
                false,
                null,
                null,
                null,
                null);
        }

        private static string InvokeRemoveTrailingZerosFromMillisecondsForAGivenDate(DateTimeOffset date)
        {
            var method = typeof(SqlServerFhirDataStore).GetMethod(
                "RemoveTrailingZerosFromMillisecondsForAGivenDate",
                BindingFlags.NonPublic | BindingFlags.Static);

            if (method == null)
            {
                throw new InvalidOperationException("Method 'RemoveTrailingZerosFromMillisecondsForAGivenDate' not found");
            }

            return (string)method.Invoke(null, new object[] { date });
        }

        private static bool InvokeChangesAreOnlyInMetadata(ResourceWrapper inputWrapper, ResourceWrapper existingWrapper)
        {
            var method = typeof(SqlServerFhirDataStore).GetMethod(
                "ChangesAreOnlyInMetadata",
                BindingFlags.NonPublic | BindingFlags.Static);

            if (method == null)
            {
                throw new InvalidOperationException("Method 'ChangesAreOnlyInMetadata' not found");
            }

            return (bool)method.Invoke(null, new object[] { inputWrapper, existingWrapper });
        }

        // Note: GetJsonValue tests require instance creation which has complex dependencies
        // This method is indirectly tested through integration tests (UpdateTests, FhirPathPatchTests)

        [Fact]
        public async Task MergeAsync_OnSqlConflict_WhenEnlistedInAmbientTransaction_ThrowsResourceConflictExceptionWithoutRetry()
        {
            // Arrange
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            var sqlException = SqlExceptionFactory.GetSqlException(SqlErrorCodes.Conflict, "SQL Conflict");
            using var cts = new CancellationTokenSource();

            sqlRetryService.ExecuteReaderAsync(
                Arg.Any<SqlCommand>(),
                Arg.Any<Func<SqlDataReader, ResourceWrapper>>(),
                Arg.Any<ILogger>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
            .Throws(sqlException);

            sqlRetryService
                .TryLogEvent(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
                .Returns(async callInfo =>
                {
                    // Tripwire: if the retry loop is incorrectly entered, cancellation makes the test fail fast rather than hang.
                    cts.Cancel();
                    await Task.CompletedTask;
                });

            // An ambient C# transaction (as opened by a sequential transaction bundle) is the condition that
            // zombies on a SQL conflict, so the data store must fail fast rather than retry within it.
            var transactionHandler = new SqlTransactionHandler();
            using var transactionScope = transactionHandler.BeginTransaction();

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService, transactionHandler);
            var resources = CreateResourceWrapperOperations();

            // Act & Assert
            await Assert.ThrowsAsync<ResourceConflictException>(
                () => dataStore.MergeAsync(resources, new MergeOptions(enlistTransaction: true, isBundleTransaction: true), cts.Token));

            await sqlRetryService.DidNotReceive()
                .TryLogEvent("MergeAsync", "Warn", Arg.Any<string>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task MergeAsync_OnSqlConflictWithIfMatch_WhenEnlistedInAmbientTransaction_ThrowsPreconditionFailedExceptionWithoutRetry()
        {
            // Arrange
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            var sqlException = SqlExceptionFactory.GetSqlException(SqlErrorCodes.Conflict, "SQL Conflict");
            using var cts = new CancellationTokenSource();

            sqlRetryService.ExecuteReaderAsync(
                Arg.Any<SqlCommand>(),
                Arg.Any<Func<SqlDataReader, ResourceWrapper>>(),
                Arg.Any<ILogger>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
            .Throws(sqlException);

            var transactionHandler = new SqlTransactionHandler();
            using var transactionScope = transactionHandler.BeginTransaction();

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService, transactionHandler);
            WeakETag weakETag = WeakETag.FromVersionId("1");
            var resources = CreateResourceWrapperOperations(weakETag);

            // Act
            PreconditionFailedException exception = await Assert.ThrowsAsync<PreconditionFailedException>(
                () => dataStore.MergeAsync(resources, new MergeOptions(enlistTransaction: true, isBundleTransaction: true), cts.Token));

            // Assert
            Assert.Equal(string.Format(Core.Resources.ResourceVersionConflict, weakETag.VersionId), exception.Message);
            await sqlRetryService.DidNotReceive()
                .TryLogEvent("MergeAsync", "Warn", Arg.Any<string>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task MergeAsync_OnSqlConflictWithComparedVersion_WhenEnlistedInAmbientTransaction_ThrowsPreconditionFailedExceptionWithoutRetry()
        {
            // Arrange
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            var sqlException = SqlExceptionFactory.GetSqlException(SqlErrorCodes.Conflict, "SQL Conflict");
            using var cts = new CancellationTokenSource();

            sqlRetryService.ExecuteReaderAsync(
                Arg.Any<SqlCommand>(),
                Arg.Any<Func<SqlDataReader, ResourceWrapper>>(),
                Arg.Any<ILogger>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
            .Throws(sqlException);

            var transactionHandler = new SqlTransactionHandler();
            using var transactionScope = transactionHandler.BeginTransaction();

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService, transactionHandler);
            const string comparedVersion = "1";
            var resources = CreateResourceWrapperOperations(comparedVersion: comparedVersion);

            // Act
            PreconditionFailedException exception = await Assert.ThrowsAsync<PreconditionFailedException>(
                () => dataStore.MergeAsync(resources, new MergeOptions(enlistTransaction: true, isBundleTransaction: true), cts.Token));

            // Assert
            Assert.Equal(string.Format(Core.Resources.ResourceVersionConflict, comparedVersion), exception.Message);
            await sqlRetryService.DidNotReceive()
                .TryLogEvent("MergeAsync", "Warn", Arg.Any<string>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task MergeAsync_OnSqlConflict_WhenEnlistTransactionWithoutAmbientScope_RetriesBeforeThrowing()
        {
            // Arrange - a regular (non-bundle) upsert sets enlistTransaction: true but runs without an ambient
            // C# transaction scope. There is nothing to zombie, so a SQL conflict must still be retried
            // (last-write-wins), not converted into a fail-fast 409.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            var sqlException = SqlExceptionFactory.GetSqlException(SqlErrorCodes.Conflict, "SQL Conflict");
            using var cts = new CancellationTokenSource();

            sqlRetryService.ExecuteReaderAsync(
                Arg.Any<SqlCommand>(),
                Arg.Any<Func<SqlDataReader, ResourceWrapper>>(),
                Arg.Any<ILogger>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
            .Throws(sqlException);

            sqlRetryService
                .TryLogEvent(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
                .Returns(async callInfo =>
                {
                    cts.Cancel();
                    await Task.CompletedTask;
                });

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService);
            var resources = CreateResourceWrapperOperations();

            // Act & Assert
            await Assert.ThrowsAsync<TaskCanceledException>(
                () => dataStore.MergeAsync(resources, new MergeOptions(enlistTransaction: true, isBundleTransaction: false), cts.Token));

            await sqlRetryService.Received(1)
                .TryLogEvent("MergeAsync", "Warn", Arg.Any<string>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task MergeAsync_OnSqlConflict_WithDefaultOptions_RetriesBeforeThrowing()
        {
            // Arrange
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            var sqlException = SqlExceptionFactory.GetSqlException(SqlErrorCodes.Conflict, "SQL Conflict");
            using var cts = new CancellationTokenSource();

            sqlRetryService.ExecuteReaderAsync(
                Arg.Any<SqlCommand>(),
                Arg.Any<Func<SqlDataReader, ResourceWrapper>>(),
                Arg.Any<ILogger>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
            .Throws(sqlException);

            // When trying to log events, a TaskCanceledException will be thrown.
            sqlRetryService
                .TryLogEvent(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
                .Returns(async callInfo =>
                {
                    cts.Cancel();
                    await Task.CompletedTask;
                });

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService);
            var resources = CreateResourceWrapperOperations();

            // Act & Assert
            await Assert.ThrowsAsync<TaskCanceledException>(
                () => dataStore.MergeAsync(resources, MergeOptions.Default, cts.Token));

            await sqlRetryService.Received(1)
                .TryLogEvent("MergeAsync", "Warn", Arg.Any<string>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task MergeAsync_OnSqlSurrogateIdCollision_RetryBeforeThrowing()
        {
            // Arrange
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            var sqlException = SqlExceptionFactory.GetSqlException(FhirSqlErrorCodes.SurrogateIdCollision, "Surrogate Id Collision.");
            using var cts = new CancellationTokenSource();

            sqlRetryService.ExecuteReaderAsync(
                Arg.Any<SqlCommand>(),
                Arg.Any<Func<SqlDataReader, ResourceWrapper>>(),
                Arg.Any<ILogger>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
            .Throws(sqlException);

            sqlRetryService
                .TryLogEvent(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
                .Returns(async callInfo =>
                {
                    cts.Cancel();
                    await Task.CompletedTask;
                });

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService);
            var resources = CreateResourceWrapperOperations();

            // Act & Assert
            await Assert.ThrowsAsync<TaskCanceledException>(
                () => dataStore.MergeAsync(resources, MergeOptions.Default, cts.Token));

            await sqlRetryService.Received(1)
                .TryLogEvent("MergeAsync", "Warn", Arg.Any<string>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task MergeAsync_GivenNoOpDeleteGuardedByComparedVersion_WhenAuthoritativeVersionMovedOn_ThenOperationFailsPrecondition()
        {
            // Arrange - the batch snapshot shows the resource already deleted at exactly the version the caller
            // guarded against, which reduces the delete to a logical no-op that never reaches dbo.MergeResources and
            // therefore never gets that stored procedure's atomic version comparison. A concurrent writer has since
            // recreated the resource at version 2, which only an authoritative, lock-serialized read can observe.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            using var cts = new CancellationTokenSource();
            const string rawResource = "{\"resourceType\":\"Patient\",\"id\":\"123\"}";

            ConfigureSuccessfulNonQueryCalls(sqlRetryService);
            ConfigureSnapshotRead(sqlRetryService, CreateResourceWrapperWithVersion(rawResource, "1", deleted: true));
            List<(string CommandText, bool IsReadOnly)> probes = ConfigureCurrentVersionProbe(
                sqlRetryService,
                _ => new[] { new ResourceDateKey(1, "123", 0, "2", isDeleted: false) });

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService);
            ResourceWrapperOperation operation = CreateOperation(
                CreateResourceWrapperWithVersion(rawResource, "1", deleted: true),
                comparedVersion: "1");

            // Act
            MergeOutcome outcome = await dataStore.MergeAsync(new[] { operation }, cts.Token);

            // Assert
            DataStoreOperationOutcome result = outcome.Results.Values.Single();
            Assert.False(result.IsOperationSuccessful);
            PreconditionFailedException exception = Assert.IsType<PreconditionFailedException>(result.Exception);
            Assert.Equal(string.Format(Core.Resources.ResourceVersionConflict, "1"), exception.Message);

            // The probe has to be authoritative, not merely fresh: a read-only replica lags the primary by an
            // unbounded amount, and without an update lock the read is not serialized against writers. HOLDLOCK is
            // deliberately absent: paired with UPDLOCK it escalates to a serializable key-range lock, which is the
            // exact UPDLOCK+HOLDLOCK combination dbo.MergeResources documents as deadlock prone on its own version
            // comparison join (see the commented out hint in MergeResources.sql), and it is not needed to serialize
            // this read against a concurrent writer of the same, already-matched row.
            (string CommandText, bool IsReadOnly) probe = Assert.Single(probes);
            Assert.False(probe.IsReadOnly);
            Assert.Contains("UPDLOCK", probe.CommandText, StringComparison.Ordinal);
            Assert.DoesNotContain("HOLDLOCK", probe.CommandText, StringComparison.Ordinal);
        }

        [Fact]
        public async Task MergeAsync_GivenVersionGuardedOperation_ThenBatchSnapshotIsReadFromThePrimary()
        {
            // Arrange - the batch snapshot decides every version comparison in the merge loop, so serving it
            // from a read-only replica can reject a client whose If-Match value is in fact the current one.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            using var cts = new CancellationTokenSource();
            const string rawResource = "{\"resourceType\":\"Patient\",\"id\":\"123\"}";

            ConfigureSuccessfulNonQueryCalls(sqlRetryService);
            List<bool> snapshotReadIsReadOnlyFlags = ConfigureSnapshotRead(sqlRetryService, CreateResourceWrapperWithVersion(rawResource, "1", deleted: true));
            ConfigureCurrentVersionProbe(sqlRetryService, _ => new[] { new ResourceDateKey(1, "123", 0, "1", isDeleted: true) });

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService);
            ResourceWrapperOperation operation = CreateOperation(
                CreateResourceWrapperWithVersion(rawResource, "1", deleted: true),
                weakETag: WeakETag.FromVersionId("1"));

            // Act
            await dataStore.MergeAsync(new[] { operation }, cts.Token);

            // Assert
            Assert.False(Assert.Single(snapshotReadIsReadOnlyFlags));
        }

        [Fact]
        public async Task MergeAsync_GivenNoOpUpdateGuardedByWeakETag_WhenAuthoritativeVersionMovedOn_ThenOperationFailsPrecondition()
        {
            // Arrange - the same race on the other no-op shortcut (submitted content byte identical to what is
            // stored), guarded by a client If-Match header rather than an internal conditional comparison.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            using var cts = new CancellationTokenSource();
            const string rawResource = "{\"resourceType\":\"Patient\",\"id\":\"123\"}";

            ConfigureSuccessfulNonQueryCalls(sqlRetryService);
            ConfigureSnapshotRead(sqlRetryService, CreateResourceWrapperWithVersion(rawResource, "1"));
            List<(string CommandText, bool IsReadOnly)> probes = ConfigureCurrentVersionProbe(
                sqlRetryService,
                _ => new[] { new ResourceDateKey(1, "123", 0, "2", isDeleted: false) });

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService);
            ResourceWrapperOperation operation = CreateOperation(
                CreateResourceWrapperWithVersion(rawResource, "1"),
                weakETag: WeakETag.FromVersionId("1"));

            // Act
            MergeOutcome outcome = await dataStore.MergeAsync(new[] { operation }, cts.Token);

            // Assert
            DataStoreOperationOutcome result = outcome.Results.Values.Single();
            Assert.False(result.IsOperationSuccessful);
            PreconditionFailedException exception = Assert.IsType<PreconditionFailedException>(result.Exception);
            Assert.Equal(string.Format(Core.Resources.ResourceVersionConflict, "1"), exception.Message);
            Assert.Single(probes);
        }

        [Fact]
        public async Task MergeAsync_GivenGuardedNoOpDelete_WhenAuthoritativeVersionStillMatches_ThenSucceedsWithoutCreatingAVersion()
        {
            // Arrange - control for the two races above. A guarded no-op that is genuinely uncontended must still be
            // honored as a no-op, and must not be pushed through dbo.MergeResources just to prove its precondition,
            // because that would create a gratuitous FHIR version.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            using var cts = new CancellationTokenSource();
            const string rawResource = "{\"resourceType\":\"Patient\",\"id\":\"123\"}";

            List<string> executedCommands = ConfigureSuccessfulNonQueryCalls(sqlRetryService);
            ConfigureSnapshotRead(sqlRetryService, CreateResourceWrapperWithVersion(rawResource, "1", deleted: true));
            ConfigureCurrentVersionProbe(sqlRetryService, _ => new[] { new ResourceDateKey(1, "123", 0, "1", isDeleted: true) });

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService);
            ResourceWrapperOperation operation = CreateOperation(
                CreateResourceWrapperWithVersion(rawResource, "1", deleted: true),
                comparedVersion: "1");

            // Act
            MergeOutcome outcome = await dataStore.MergeAsync(new[] { operation }, cts.Token);

            // Assert
            Assert.True(outcome.Results.Values.Single().IsOperationSuccessful);
            Assert.DoesNotContain("dbo.MergeResources", executedCommands, StringComparer.Ordinal);
            Assert.DoesNotContain("dbo.MergeResourcesAndSearchParams", executedCommands, StringComparer.Ordinal);
        }

        [Fact]
        public async Task MergeAsync_GivenGuardedNoOpUpdate_WhenWeakETagHasNonCanonicalNumericFormat_ThenAuthoritativeCompareIsNormalizedLikeTheSnapshotCompare()
        {
            // Arrange - a client can send If-Match: W/"01" for version 1 (a leading zero is not canonical, but is
            // still the same integer). The pre-existing batch snapshot comparison already normalizes a WeakETag
            // through int parsing before comparing it to the stored version (see the eTag computation earlier in
            // MergeInternalAsync), so "01" is accepted as matching a stored "1". The authoritative no-op probe added
            // for this guard must apply the same normalization; comparing the raw strings would reject this
            // identical, uncontended version as a spurious 412 solely because this operation happened to be reduced
            // to a no-op.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            using var cts = new CancellationTokenSource();
            const string rawResource = "{\"resourceType\":\"Patient\",\"id\":\"123\"}";

            ConfigureSuccessfulNonQueryCalls(sqlRetryService);
            ConfigureSnapshotRead(sqlRetryService, CreateResourceWrapperWithVersion(rawResource, "1"));
            ConfigureCurrentVersionProbe(sqlRetryService, _ => new[] { new ResourceDateKey(1, "123", 0, "1", isDeleted: false) });

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService);
            ResourceWrapperOperation operation = CreateOperation(
                CreateResourceWrapperWithVersion(rawResource, "1"),
                weakETag: WeakETag.FromVersionId("01"));

            // Act
            MergeOutcome outcome = await dataStore.MergeAsync(new[] { operation }, cts.Token);

            // Assert
            DataStoreOperationOutcome result = outcome.Results.Values.Single();
            Assert.True(result.IsOperationSuccessful);
        }

        [Fact]
        public async Task MergeAsync_GivenGuardedNoOpDelete_WhenAuthoritativeTargetDisappeared_ThenFailsPreconditionRatherThanNotFound()
        {
            // Arrange - the guarded target was hard deleted or purged after the snapshot was taken. Cosmos DB reports
            // a guarded disappearance as a failed precondition, and SQL Server must agree rather than reporting a
            // generic not-found or silently succeeding as a no-op.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            using var cts = new CancellationTokenSource();
            const string rawResource = "{\"resourceType\":\"Patient\",\"id\":\"123\"}";

            ConfigureSuccessfulNonQueryCalls(sqlRetryService);
            ConfigureSnapshotRead(sqlRetryService, CreateResourceWrapperWithVersion(rawResource, "1", deleted: true));
            ConfigureCurrentVersionProbe(sqlRetryService, _ => Array.Empty<ResourceDateKey>());

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService);
            ResourceWrapperOperation operation = CreateOperation(
                CreateResourceWrapperWithVersion(rawResource, "1", deleted: true),
                weakETag: WeakETag.FromVersionId("1"));

            // Act
            MergeOutcome outcome = await dataStore.MergeAsync(new[] { operation }, cts.Token);

            // Assert
            DataStoreOperationOutcome result = outcome.Results.Values.Single();
            Assert.False(result.IsOperationSuccessful);
            PreconditionFailedException exception = Assert.IsType<PreconditionFailedException>(result.Exception);
            Assert.Equal(string.Format(Core.Resources.ResourceVersionConflict, "1"), exception.Message);
        }

        [Fact]
        public async Task MergeAsync_GivenComparedVersionGuardedTargetMissingFromSnapshot_ThenFailsPreconditionRatherThanNotFound()
        {
            // Arrange - regression guard for the pre-existing disappearance path, where the target is already absent
            // from the batch snapshot itself.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            using var cts = new CancellationTokenSource();

            ConfigureSuccessfulNonQueryCalls(sqlRetryService);
            ConfigureSnapshotRead(sqlRetryService);

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService);
            ResourceWrapperOperation operation = CreateOperation(
                CreateResourceWrapperWithVersion("{\"resourceType\":\"Patient\",\"id\":\"123\"}", "3"),
                comparedVersion: "3");

            // Act
            MergeOutcome outcome = await dataStore.MergeAsync(new[] { operation }, cts.Token);

            // Assert
            DataStoreOperationOutcome result = outcome.Results.Values.Single();
            Assert.False(result.IsOperationSuccessful);
            PreconditionFailedException exception = Assert.IsType<PreconditionFailedException>(result.Exception);
            Assert.Equal(string.Format(Core.Resources.ResourceVersionConflict, "3"), exception.Message);
        }

        [Fact]
        public async Task MergeAsync_GivenWeakETagGuardedDeleteAgainstMissingTarget_ThenFailsPreconditionRatherThanSilentSuccess()
        {
            // Arrange - the target is entirely absent from the batch snapshot (it never existed, or was hard
            // deleted/purged before this merge began). dbo.MergeResources is never invoked for this operation, so
            // nothing else in the merge enforces the client's If-Match here. A supplied WeakETag can never be
            // satisfied by a target that does not exist, so SQL Server must fail the precondition just as Cosmos DB
            // does for the same disappearance, rather than silently treating this guarded delete as an idempotent
            // no-op success.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            using var cts = new CancellationTokenSource();

            ConfigureSuccessfulNonQueryCalls(sqlRetryService);
            ConfigureSnapshotRead(sqlRetryService);

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService);
            ResourceWrapperOperation operation = CreateOperation(
                CreateResourceWrapperWithVersion("{\"resourceType\":\"Patient\",\"id\":\"123\"}", "1", deleted: true),
                weakETag: WeakETag.FromVersionId("1"));

            // Act
            MergeOutcome outcome = await dataStore.MergeAsync(new[] { operation }, cts.Token);

            // Assert
            DataStoreOperationOutcome result = outcome.Results.Values.Single();
            Assert.False(result.IsOperationSuccessful);
            PreconditionFailedException exception = Assert.IsType<PreconditionFailedException>(result.Exception);
            Assert.Equal(string.Format(Core.Resources.ResourceVersionConflict, "1"), exception.Message);
        }

        [Fact]
        public async Task MergeAsync_GivenUnguardedDeleteAgainstMissingTarget_ThenSucceedsIdempotently()
        {
            // Arrange - control for the guarded case above. A plain delete carrying no client precondition at all
            // (no WeakETag, no ComparedVersion) must remain the pre-existing idempotent no-op success when its
            // target does not exist.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            using var cts = new CancellationTokenSource();

            ConfigureSuccessfulNonQueryCalls(sqlRetryService);
            ConfigureSnapshotRead(sqlRetryService);

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService);
            ResourceWrapperOperation operation = CreateOperation(
                CreateResourceWrapperWithVersion("{\"resourceType\":\"Patient\",\"id\":\"123\"}", "1", deleted: true));

            // Act
            MergeOutcome outcome = await dataStore.MergeAsync(new[] { operation }, cts.Token);

            // Assert
            DataStoreOperationOutcome result = outcome.Results.Values.Single();
            Assert.True(result.IsOperationSuccessful);
            Assert.Null(result.UpsertOutcome);
        }

        [Fact]
        public async Task MergeAsync_GivenGuardedNoOpRacedInMultiResourceBatch_ThenOnlyThatOperationFails()
        {
            // Arrange - a failed precondition belongs to one entry of a batch. It must be reported as that entry's
            // outcome rather than aborting the whole merge, otherwise one racing bundle entry would take unrelated
            // entries down with it.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            using var cts = new CancellationTokenSource();
            const string guardedRawResource = "{\"resourceType\":\"Patient\",\"id\":\"guarded-1\"}";
            const string otherRawResource = "{\"resourceType\":\"Patient\",\"id\":\"other-1\"}";

            ConfigureSuccessfulNonQueryCalls(sqlRetryService);
            ConfigureSnapshotRead(
                sqlRetryService,
                CreateResourceWrapperWithVersion(guardedRawResource, "1", deleted: true, resourceId: "guarded-1"),
                CreateResourceWrapperWithVersion(otherRawResource, "1", deleted: true, resourceId: "other-1"));
            ConfigureCurrentVersionProbe(sqlRetryService, _ => new[] { new ResourceDateKey(1, "guarded-1", 0, "2", isDeleted: false) });

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService);
            ResourceWrapperOperation guarded = CreateOperation(
                CreateResourceWrapperWithVersion(guardedRawResource, "1", deleted: true, resourceId: "guarded-1"),
                comparedVersion: "1");
            ResourceWrapperOperation other = CreateOperation(
                CreateResourceWrapperWithVersion(otherRawResource, "1", deleted: true, resourceId: "other-1"));

            // Act
            MergeOutcome outcome = await dataStore.MergeAsync(new[] { guarded, other }, cts.Token);

            // Assert
            Assert.Equal(2, outcome.Results.Count);
            DataStoreOperationOutcome guardedResult = outcome.Results[guarded.GetIdentifier()];
            DataStoreOperationOutcome otherResult = outcome.Results[other.GetIdentifier()];

            Assert.False(guardedResult.IsOperationSuccessful);
            Assert.IsType<PreconditionFailedException>(guardedResult.Exception);
            Assert.True(otherResult.IsOperationSuccessful);
        }

        [Fact]
        public async Task MergeAsync_OnSqlConflictInMultiResourceBatch_WhenGuardedOperationStillMatches_ThenConflictIsNotReportedAsPreconditionFailure()
        {
            // Arrange - dbo.MergeResources raises one generic conflict for a whole batch and does not say which row
            // caused it. Here the guarded operation's version is still exactly what the client supplied, so the
            // conflict came from the other, unguarded operation and must not be reported as this client's 412.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            var sqlException = SqlExceptionFactory.GetSqlException(SqlErrorCodes.Conflict, "SQL Conflict");
            using var cts = new CancellationTokenSource();
            const string guardedRawResource = "{\"resourceType\":\"Patient\",\"id\":\"guarded-1\"}";
            const string otherRawResource = "{\"resourceType\":\"Patient\",\"id\":\"other-1\"}";

            sqlRetryService.ExecuteReaderAsync(
                Arg.Any<SqlCommand>(),
                Arg.Any<Func<SqlDataReader, ResourceWrapper>>(),
                Arg.Any<ILogger>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
            .Throws(sqlException);

            List<(string CommandText, bool IsReadOnly)> probes = ConfigureCurrentVersionProbe(
                sqlRetryService,
                _ => new[] { new ResourceDateKey(1, "guarded-1", 0, "1", isDeleted: false) });

            var transactionHandler = new SqlTransactionHandler();
            using var transactionScope = transactionHandler.BeginTransaction();

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService, transactionHandler);
            var resources = new List<ResourceWrapperOperation>
            {
                CreateOperation(CreateResourceWrapperWithVersion(guardedRawResource, "1", resourceId: "guarded-1"), weakETag: WeakETag.FromVersionId("1")),
                CreateOperation(CreateResourceWrapperWithVersion(otherRawResource, "1", resourceId: "other-1")),
            };

            // Act & Assert
            await Assert.ThrowsAsync<ResourceConflictException>(
                () => dataStore.MergeAsync(resources, new MergeOptions(enlistTransaction: true, isBundleTransaction: true), cts.Token));

            // Correlating a failure that already happened must never take locks: the caller's own ambient transaction
            // may still hold write locks on these rows and cannot be used to release them from here.
            (string CommandText, bool IsReadOnly) probe = Assert.Single(probes);
            Assert.DoesNotContain("UPDLOCK", probe.CommandText, StringComparison.Ordinal);
            Assert.False(probe.IsReadOnly);
        }

        [Fact]
        public async Task MergeAsync_OnSqlConflictInMultiResourceBatch_WhenGuardedOperationHasNonCanonicalNumericWeakETagThatStillMatches_ThenConflictIsNotReportedAsPreconditionFailure()
        {
            // Arrange - control for the correlation above with a non-canonical numeric WeakETag. A client can send
            // If-Match: W/"01" for version "1" (a leading zero is not canonical, but is still the same integer). The
            // batch snapshot comparison and the guarded no-op probe both normalize a WeakETag through int parsing
            // before comparing it to the stored version (see ParseWeakETagVersionOrSentinel), so the correlation used
            // to attribute a generic SQL conflict to a specific operation must apply the same normalization. Without
            // it, this operation's identical, uncontended "01" vs "1" version would be rejected as a raw string
            // mismatch and the unrelated batch conflict would be misreported as this client's 412.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            var sqlException = SqlExceptionFactory.GetSqlException(SqlErrorCodes.Conflict, "SQL Conflict");
            using var cts = new CancellationTokenSource();
            const string guardedRawResource = "{\"resourceType\":\"Patient\",\"id\":\"guarded-1\"}";
            const string otherRawResource = "{\"resourceType\":\"Patient\",\"id\":\"other-1\"}";

            sqlRetryService.ExecuteReaderAsync(
                Arg.Any<SqlCommand>(),
                Arg.Any<Func<SqlDataReader, ResourceWrapper>>(),
                Arg.Any<ILogger>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
            .Throws(sqlException);

            ConfigureCurrentVersionProbe(sqlRetryService, _ => new[] { new ResourceDateKey(1, "guarded-1", 0, "1", isDeleted: false) });

            var transactionHandler = new SqlTransactionHandler();
            using var transactionScope = transactionHandler.BeginTransaction();

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService, transactionHandler);
            var resources = new List<ResourceWrapperOperation>
            {
                CreateOperation(CreateResourceWrapperWithVersion(guardedRawResource, "1", resourceId: "guarded-1"), weakETag: WeakETag.FromVersionId("01")),
                CreateOperation(CreateResourceWrapperWithVersion(otherRawResource, "1", resourceId: "other-1")),
            };

            // Act & Assert
            await Assert.ThrowsAsync<ResourceConflictException>(
                () => dataStore.MergeAsync(resources, new MergeOptions(enlistTransaction: true, isBundleTransaction: true), cts.Token));
        }

        [Fact]
        public async Task MergeAsync_OnSqlConflictInMultiResourceBatch_WhenGuardedOperationIsStale_ThenPreconditionFailedIsReported()
        {
            // Arrange - control for the correlation above. When the guarded operation genuinely lost its race, the
            // conflict does belong to it and must still surface as a 412 naming the version the client supplied.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            var sqlException = SqlExceptionFactory.GetSqlException(SqlErrorCodes.Conflict, "SQL Conflict");
            using var cts = new CancellationTokenSource();
            const string guardedRawResource = "{\"resourceType\":\"Patient\",\"id\":\"guarded-1\"}";
            const string otherRawResource = "{\"resourceType\":\"Patient\",\"id\":\"other-1\"}";

            sqlRetryService.ExecuteReaderAsync(
                Arg.Any<SqlCommand>(),
                Arg.Any<Func<SqlDataReader, ResourceWrapper>>(),
                Arg.Any<ILogger>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
            .Throws(sqlException);

            ConfigureCurrentVersionProbe(sqlRetryService, _ => new[] { new ResourceDateKey(1, "guarded-1", 0, "2", isDeleted: false) });

            var transactionHandler = new SqlTransactionHandler();
            using var transactionScope = transactionHandler.BeginTransaction();

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService, transactionHandler);
            var resources = new List<ResourceWrapperOperation>
            {
                CreateOperation(CreateResourceWrapperWithVersion(guardedRawResource, "1", resourceId: "guarded-1"), weakETag: WeakETag.FromVersionId("1")),
                CreateOperation(CreateResourceWrapperWithVersion(otherRawResource, "1", resourceId: "other-1")),
            };

            // Act
            PreconditionFailedException exception = await Assert.ThrowsAsync<PreconditionFailedException>(
                () => dataStore.MergeAsync(resources, new MergeOptions(enlistTransaction: true, isBundleTransaction: true), cts.Token));

            // Assert
            Assert.Equal(string.Format(Core.Resources.ResourceVersionConflict, "1"), exception.Message);
        }

        [Fact]
        public async Task MergeAsync_GivenGuardedNoOp_ThenTheAuthoritativeProbeTakesItsUpdateLockOnTheClusteredKey()
        {
            // Arrange - the probe waits on, and is waited on by, the same writers dbo.MergeResources runs. Every one
            // of those writers touches a resource through the clustered key (ResourceTypeId, ResourceSurrogateId)
            // first - dbo.MergeResources locks it that way in its retry check and again when it stamps the previous
            // version as history - so a probe that instead claimed its update lock on the
            // IX_Resource_ResourceTypeId_ResourceId seek would acquire the two lock resources of a single row in the
            // opposite order and could deadlock a bundle that later writes that row. Locating the row through the
            // index without a lock and then locking the clustered key keeps this read in the store's established
            // lock order.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            using var cts = new CancellationTokenSource();
            const string rawResource = "{\"resourceType\":\"Patient\",\"id\":\"123\"}";

            ConfigureSuccessfulNonQueryCalls(sqlRetryService);
            ConfigureSnapshotRead(sqlRetryService, CreateResourceWrapperWithVersion(rawResource, "1"));
            List<(string CommandText, bool IsReadOnly)> probes = ConfigureCurrentVersionProbe(
                sqlRetryService,
                _ => new[] { new ResourceDateKey(1, "123", 0, "1", isDeleted: false) });

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService);
            ResourceWrapperOperation operation = CreateOperation(
                CreateResourceWrapperWithVersion(rawResource, "1"),
                weakETag: WeakETag.FromVersionId("1"));

            // Act
            await dataStore.MergeAsync(new[] { operation }, cts.Token);

            // Assert
            string probeCommandText = Assert.Single(probes).CommandText;
            string[] lines = probeCommandText.Split('\n');

            string lockingJoin = Assert.Single(lines, line => line.Contains("UPDLOCK", StringComparison.Ordinal));
            Assert.Contains("ROWLOCK", lockingJoin, StringComparison.Ordinal);
            Assert.DoesNotContain("HOLDLOCK", probeCommandText, StringComparison.Ordinal);
            Assert.DoesNotContain("IX_Resource_ResourceTypeId_ResourceId", lockingJoin, StringComparison.Ordinal);

            // Every column read here is also carried by IX_Resource_ResourceTypeId_ResourceId, so without an explicit
            // clustered index hint the optimizer covers the read from that nonclustered index and takes the update
            // lock on the index row instead of the row itself.
            Assert.Contains("INDEX = PKC_Resource", lockingJoin, StringComparison.Ordinal);
            Assert.Contains("ResourceSurrogateId = A.ResourceSurrogateId", probeCommandText, StringComparison.Ordinal);

            // An authoritative probe blocks on a concurrent writer by design. A bounded lock timeout keeps that wait
            // from turning into an unbounded stall (and, when the merge is enlisted in a bundle's transaction, from
            // holding that bundle open indefinitely); the resulting error is classified as contention below.
            Assert.Contains("SET LOCK_TIMEOUT", probeCommandText, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(1205)] // deadlock victim
        [InlineData(1222)] // lock request timeout
        public async Task MergeAsync_GivenGuardedNoOpProbeLostToContention_ThenReportsRetryableContentionRatherThanASqlFailure(int sqlErrorNumber)
        {
            // Arrange - the authoritative probe takes locks and waits for concurrent writers, so SQL can pick it as a
            // deadlock victim or time its lock request out. Neither outcome says anything about whether the client's
            // version is stale, so reporting 412 would be a lie; letting the raw SqlException escape reports a
            // 500 for an ordinary, retryable contention outcome.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            using var cts = new CancellationTokenSource();
            const string rawResource = "{\"resourceType\":\"Patient\",\"id\":\"123\"}";

            List<string> executedCommands = ConfigureSuccessfulNonQueryCalls(sqlRetryService);
            ConfigureSnapshotRead(sqlRetryService, CreateResourceWrapperWithVersion(rawResource, "1"));
            ConfigureFailingCurrentVersionProbe(sqlRetryService, SqlExceptionFactory.GetSqlException(sqlErrorNumber, "Lock contention"));

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService);
            ResourceWrapperOperation operation = CreateOperation(
                CreateResourceWrapperWithVersion(rawResource, "1"),
                weakETag: WeakETag.FromVersionId("1"));

            // Act
            MergeOutcome outcome = await dataStore.MergeAsync(new[] { operation }, cts.Token);

            // Assert
            DataStoreOperationOutcome result = outcome.Results.Values.Single();
            Assert.False(result.IsOperationSuccessful);
            Assert.IsType<TransactionDeadlockException>(result.Exception);
            Assert.Equal(Core.Resources.TransactionDeadlock, result.Exception.Message);

            // The contention belongs to one operation, so the batch's own merge transaction must still be settled.
            Assert.Contains("dbo.MergeResourcesCommitTransaction", executedCommands, StringComparer.Ordinal);
        }

        [Fact]
        public async Task MergeAsync_GivenGuardedNoOpProbeFailure_ThenTheMergeTransactionIsSettledInsteadOfOrphaned()
        {
            // Arrange - the guarded probe runs after dbo.MergeResourcesBeginTransaction has already handed out a
            // transaction id. Any failure that escapes from that point on must still settle that transaction,
            // otherwise every failed probe leaves an open transaction behind for the watchdog to time out, and a
            // sequential bundle can leak one per entry.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            using var cts = new CancellationTokenSource();
            const string rawResource = "{\"resourceType\":\"Patient\",\"id\":\"123\"}";

            List<string> executedCommands = ConfigureSuccessfulNonQueryCalls(sqlRetryService);
            ConfigureSnapshotRead(sqlRetryService, CreateResourceWrapperWithVersion(rawResource, "1"));
            ConfigureFailingCurrentVersionProbe(sqlRetryService, SqlExceptionFactory.GetSqlException(50000, "probe failed"));

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService);
            ResourceWrapperOperation operation = CreateOperation(
                CreateResourceWrapperWithVersion(rawResource, "1"),
                weakETag: WeakETag.FromVersionId("1"));

            // Act - an unclassified probe failure still propagates; only the transaction handling changes.
            await Assert.ThrowsAsync<SqlException>(() => dataStore.MergeAsync(new[] { operation }, cts.Token));

            // Assert
            Assert.Contains("dbo.MergeResourcesBeginTransaction", executedCommands, StringComparer.Ordinal);
            Assert.Contains("dbo.MergeResourcesCommitTransaction", executedCommands, StringComparer.Ordinal);
        }

        [Fact]
        public async Task MergeAsync_GivenBundleTransactionWithAFailedPrecondition_ThenTheMergeTransactionIsSettledInsteadOfOrphaned()
        {
            // Arrange - a bundle transaction that contains any failed operation returns without sending anything to
            // dbo.MergeResources. The transaction id handed out before the merge loop is therefore never used, and
            // must be settled here rather than left open until the transaction watchdog times it out.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            using var cts = new CancellationTokenSource();
            const string rawResource = "{\"resourceType\":\"Patient\",\"id\":\"123\"}";

            List<string> executedCommands = ConfigureSuccessfulNonQueryCalls(sqlRetryService);
            ConfigureSnapshotRead(sqlRetryService, CreateResourceWrapperWithVersion(rawResource, "1"));
            ConfigureCurrentVersionProbe(sqlRetryService, _ => new[] { new ResourceDateKey(1, "123", 0, "2", isDeleted: false) });

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService);
            ResourceWrapperOperation operation = CreateOperation(
                CreateResourceWrapperWithVersion(rawResource, "1"),
                comparedVersion: "1");

            // Act
            MergeOutcome outcome = await dataStore.MergeAsync(
                new[] { operation },
                new MergeOptions(enlistTransaction: false, isBundleTransaction: true),
                cts.Token);

            // Assert
            Assert.Equal(MergeOutcomeFinalState.CompletedWithFailures, outcome.State);
            Assert.IsType<PreconditionFailedException>(outcome.Results.Values.Single().Exception);
            Assert.DoesNotContain("dbo.MergeResources", executedCommands, StringComparer.Ordinal);
            Assert.Contains("dbo.MergeResourcesCommitTransaction", executedCommands, StringComparer.Ordinal);
        }

        [Fact]
        public async Task MergeAsync_GivenSettlementThatItselfFails_ThenTheOriginalMergeFailureIsWhatPropagates()
        {
            // Arrange - settling the merge transaction is best effort cleanup running on an already-failing request.
            // Its own failure must never replace or hide the failure the caller actually needs to see.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            using var cts = new CancellationTokenSource();
            const string rawResource = "{\"resourceType\":\"Patient\",\"id\":\"123\"}";

            SqlException probeFailure = SqlExceptionFactory.GetSqlException(50000, "probe failed");
            SqlException settlementFailure = SqlExceptionFactory.GetSqlException(50001, "settlement failed");

            List<RecordedNonQueryCall> executedCommands = ConfigureRecordedNonQueryCalls(
                sqlRetryService,
                call => string.Equals(call.CommandText, "dbo.MergeResourcesCommitTransaction", StringComparison.Ordinal)
                    ? Task.FromException(settlementFailure)
                    : Task.CompletedTask);
            ConfigureSnapshotRead(sqlRetryService, CreateResourceWrapperWithVersion(rawResource, "1"));
            ConfigureFailingCurrentVersionProbe(sqlRetryService, probeFailure);

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService);
            ResourceWrapperOperation operation = CreateOperation(
                CreateResourceWrapperWithVersion(rawResource, "1"),
                weakETag: WeakETag.FromVersionId("1"));

            // Act
            SqlException thrown = await Assert.ThrowsAsync<SqlException>(() => dataStore.MergeAsync(new[] { operation }, cts.Token));

            // Assert
            Assert.Same(probeFailure, thrown);
            Assert.Contains(executedCommands, call => string.Equals(call.CommandText, "dbo.MergeResourcesCommitTransaction", StringComparison.Ordinal));
        }

        [Fact]
        public async Task MergeAsync_GivenSettlementThatNeverResponds_ThenTheOriginalMergeFailureStillPropagates()
        {
            // Arrange - a datastore blip must not hold an already-failing request open for the store's full
            // retry/command-timeout budget. The fake settlement below answers only when its own token is cancelled,
            // so this test can only complete if the settlement attempt is bounded.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            using var cts = new CancellationTokenSource();
            const string rawResource = "{\"resourceType\":\"Patient\",\"id\":\"123\"}";

            SqlException probeFailure = SqlExceptionFactory.GetSqlException(50000, "probe failed");

            List<RecordedNonQueryCall> executedCommands = ConfigureRecordedNonQueryCalls(
                sqlRetryService,
                call => string.Equals(call.CommandText, "dbo.MergeResourcesCommitTransaction", StringComparison.Ordinal)
                    ? Task.Delay(Timeout.Infinite, call.CancellationToken)
                    : Task.CompletedTask);
            ConfigureSnapshotRead(sqlRetryService, CreateResourceWrapperWithVersion(rawResource, "1"));
            ConfigureFailingCurrentVersionProbe(sqlRetryService, probeFailure);

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService);
            ResourceWrapperOperation operation = CreateOperation(
                CreateResourceWrapperWithVersion(rawResource, "1"),
                weakETag: WeakETag.FromVersionId("1"));

            // Act
            Task<SqlException> merge = Assert.ThrowsAsync<SqlException>(() => dataStore.MergeAsync(new[] { operation }, cts.Token));
            Task finished = await Task.WhenAny(merge, Task.Delay(UnresponsiveSettlementGuard));

            // Assert
            Assert.True(ReferenceEquals(finished, merge), "MergeAsync stayed blocked on an unresponsive settlement instead of surfacing its own failure.");
            Assert.Same(probeFailure, await merge);

            RecordedNonQueryCall settlement = Assert.Single(executedCommands, call => string.Equals(call.CommandText, "dbo.MergeResourcesCommitTransaction", StringComparison.Ordinal));
            Assert.True(settlement.CancellationToken.IsCancellationRequested, "The abandoned settlement attempt must be cancelled by its own bound.");
        }

        [Fact]
        public async Task MergeAsync_GivenCallerCancellationBeforeMergeResources_ThenSettlementIsStillAttemptedWithAnUncancelledToken()
        {
            // Arrange - cancellation is the most common way to leave a merge before dbo.MergeResources. Settling
            // under the caller's token would mean the one case that most needs cleanup never gets an attempt.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            using var cts = new CancellationTokenSource();
            const string rawResource = "{\"resourceType\":\"Patient\",\"id\":\"123\"}";

            List<RecordedNonQueryCall> executedCommands = ConfigureRecordedNonQueryCalls(sqlRetryService);
            ConfigureSnapshotRead(sqlRetryService, CreateResourceWrapperWithVersion(rawResource, "1"));
            ConfigureCurrentVersionProbe(
                sqlRetryService,
                _ =>
                {
                    cts.Cancel();
                    cts.Token.ThrowIfCancellationRequested();
                    return Array.Empty<ResourceDateKey>();
                });

            var dataStore = CreateSqlServerFhirDataStore(sqlRetryService);
            ResourceWrapperOperation operation = CreateOperation(
                CreateResourceWrapperWithVersion(rawResource, "1"),
                weakETag: WeakETag.FromVersionId("1"));

            // Act
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dataStore.MergeAsync(new[] { operation }, cts.Token));

            // Assert
            RecordedNonQueryCall settlement = Assert.Single(executedCommands, call => string.Equals(call.CommandText, "dbo.MergeResourcesCommitTransaction", StringComparison.Ordinal));
            Assert.False(settlement.CancellationToken.IsCancellationRequested, "Settlement must not inherit the caller's already-cancelled token.");
            Assert.True(settlement.DisableRetries, "Settlement must not re-enter the SQL retry loop while the caller waits.");
        }

        [Fact]
        public void GivenKnownResourceType_WhenGettingResourceTypeId_ThenResourceTypeIdIsReturned()
        {
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            var model = GetModel(CreateSqlServerFhirDataStore(sqlRetryService));

            short resourceTypeId = model.GetResourceTypeId("Patient");

            Assert.Equal(1, resourceTypeId);
        }

        [Fact]
        public void GivenUnknownResourceType_WhenGettingResourceTypeId_ThenResourceNotFoundExceptionIsThrown()
        {
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            var model = GetModel(CreateSqlServerFhirDataStore(sqlRetryService));

            ResourceNotFoundException exception = Assert.Throws<ResourceNotFoundException>(() => model.GetResourceTypeId("patient"));

            Assert.Contains("is not a known resource type", exception.Message, StringComparison.Ordinal);
        }

        private static ResourceWrapper CreateResourceWrapperWithVersion(string rawResourceData, string version, bool deleted = false, string resourceId = "123")
        {
            return new ResourceWrapper(
                resourceId,
                version,
                "Patient",
                new RawResource(rawResourceData, FhirResourceFormat.Json, isMetaSet: true),
                null,
                DateTimeOffset.UtcNow,
                deleted,
                null,
                null,
                null,
                null);
        }

        private static ResourceWrapperOperation CreateOperation(ResourceWrapper wrapper, WeakETag weakETag = null, string comparedVersion = null)
        {
            return new ResourceWrapperOperation(
                wrapper,
                allowCreate: true,
                keepHistory: false,
                weakETag: weakETag,
                requireETagOnUpdate: false,
                keepVersion: false,
                bundleResourceContext: null,
                comparedVersion: comparedVersion);
        }

        /// <summary>
        /// Satisfies the non-query SQL seam used by MergeResourcesBeginTransaction, MergeResourcesCommitTransaction and
        /// dbo.MergeResources without touching a database, recording every command text so a test can prove which
        /// stored procedures were and were not invoked.
        /// </summary>
        private static List<string> ConfigureSuccessfulNonQueryCalls(ISqlRetryService sqlRetryService)
        {
            var executedCommands = new List<string>();

            ConfigureNonQueryCalls(
                sqlRetryService,
                (cmd, _, _) =>
                {
                    executedCommands.Add(cmd.CommandText);
                    return Task.CompletedTask;
                });

            return executedCommands;
        }

        /// <summary>
        /// Same seam as <see cref="ConfigureSuccessfulNonQueryCalls"/>, but it also records how each command was
        /// issued - which token bounded it and whether retries were disabled - and lets a test replace an individual
        /// command's round trip with a deterministic behavior such as "never answers until cancelled".
        /// </summary>
        private static List<RecordedNonQueryCall> ConfigureRecordedNonQueryCalls(
            ISqlRetryService sqlRetryService,
            Func<RecordedNonQueryCall, Task> behavior = null)
        {
            var executedCommands = new List<RecordedNonQueryCall>();

            ConfigureNonQueryCalls(
                sqlRetryService,
                (cmd, cancellationToken, disableRetries) =>
                {
                    var call = new RecordedNonQueryCall(cmd.CommandText, cmd.CommandTimeout, cancellationToken, disableRetries);
                    executedCommands.Add(call);
                    return behavior == null ? Task.CompletedTask : behavior(call);
                });

            return executedCommands;
        }

        private static void ConfigureNonQueryCalls(ISqlRetryService sqlRetryService, Func<SqlCommand, CancellationToken, bool, Task> handler)
        {
            sqlRetryService.ExecuteSql(
                Arg.Any<SqlCommand>(),
                Arg.Any<Func<SqlCommand, CancellationToken, Task>>(),
                Arg.Any<ILogger>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<string>())
                .Returns(callInfo =>
                {
                    var cmd = callInfo.Arg<SqlCommand>();

                    if (string.Equals(cmd.CommandText, "dbo.MergeResourcesBeginTransaction", StringComparison.Ordinal))
                    {
                        if (cmd.Parameters.Contains("@TransactionId"))
                        {
                            cmd.Parameters["@TransactionId"].Value = 1L;
                        }

                        if (cmd.Parameters.Contains("@SequenceRangeFirstValue"))
                        {
                            cmd.Parameters["@SequenceRangeFirstValue"].Value = 1;
                        }
                    }

                    return handler(cmd, callInfo.ArgAt<CancellationToken>(4), callInfo.ArgAt<bool>(6));
                });
        }

        /// <summary>
        /// Configures the batch level snapshot read performed at the top of MergeInternalAsync (dbo.GetResources).
        /// </summary>
        private static List<bool> ConfigureSnapshotRead(ISqlRetryService sqlRetryService, params ResourceWrapper[] snapshot)
        {
            var snapshotReadIsReadOnlyFlags = new List<bool>();
            sqlRetryService.ExecuteReaderAsync(
                Arg.Any<SqlCommand>(),
                Arg.Any<Func<SqlDataReader, ResourceWrapper>>(),
                Arg.Any<ILogger>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(callInfo =>
                {
                    snapshotReadIsReadOnlyFlags.Add(callInfo.ArgAt<bool>(5));
                    return Task.FromResult<IReadOnlyList<ResourceWrapper>>(snapshot.ToList());
                });

            return snapshotReadIsReadOnlyFlags;
        }

        /// <summary>
        /// Configures the authoritative current-version probe and records how each probe was issued, so a test can
        /// assert that it never targets a read-only replica and that it acquires update locks when it must be
        /// serialized against concurrent writers.
        /// </summary>
        private static List<(string CommandText, bool IsReadOnly)> ConfigureCurrentVersionProbe(
            ISqlRetryService sqlRetryService,
            Func<string, IReadOnlyList<ResourceDateKey>> probeResult)
        {
            var probes = new List<(string CommandText, bool IsReadOnly)>();

            sqlRetryService.ExecuteReaderAsync(
                Arg.Any<SqlCommand>(),
                Arg.Any<Func<SqlDataReader, ResourceDateKey>>(),
                Arg.Any<ILogger>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(callInfo =>
                {
                    string commandText = callInfo.Arg<SqlCommand>().CommandText;
                    probes.Add((commandText, callInfo.ArgAt<bool>(5)));
                    return Task.FromResult(probeResult(commandText));
                });

            return probes;
        }

        /// <summary>
        /// Configures the authoritative current-version probe to fail, so a test can prove how a probe that never
        /// produced an answer is classified.
        /// </summary>
        private static void ConfigureFailingCurrentVersionProbe(ISqlRetryService sqlRetryService, Exception failure)
        {
            sqlRetryService.ExecuteReaderAsync(
                Arg.Any<SqlCommand>(),
                Arg.Any<Func<SqlDataReader, ResourceDateKey>>(),
                Arg.Any<ILogger>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .ThrowsAsync(failure);
        }

        private static SqlServerFhirDataStore CreateSqlServerFhirDataStore(
            ISqlRetryService sqlRetryService,
            SqlTransactionHandler sqlTransactionHandler = null)
        {
            sqlTransactionHandler ??= new SqlTransactionHandler();

            ModelInfoProvider.SetProvider(MockModelInfoProviderBuilder.Create(FhirSpecification.R4).AddKnownTypes(KnownResourceTypes.Group).Build());

            var schemaInfo = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max)
            {
                Current = SchemaVersionConstants.Max,
            };

            var searchService = Substitute.For<ISearchService>();
            var searchParameterComparer = Substitute.For<ISearchParameterComparer<SearchParameterInfo>>();
            var statusDataStore = Substitute.For<ISearchParameterStatusDataStore>();
            var fhirDataStore = Substitute.For<IFhirDataStore>();
            ISearchParameterDefinitionManager defManager = new SearchParameterDefinitionManager(
                ModelInfoProvider.Instance,
                Substitute.For<IMediator>(),
                searchService.CreateMockScopeProvider(),
                searchParameterComparer,
                statusDataStore.CreateMockScopeProvider(),
                fhirDataStore.CreateMockScopeProvider(),
                NullLogger<SearchParameterDefinitionManager>.Instance);
            FilebasedSearchParameterStatusDataStore statusStore = new FilebasedSearchParameterStatusDataStore(defManager, ModelInfoProvider.Instance);

            var securityConfiguration = new SecurityConfiguration { PrincipalClaims = { "oid" } };

            var model = new SqlServerFhirModel(
                schemaInfo,
                Substitute.For<ISearchParameterDefinitionManager>(),
                () => statusStore,
                Options.Create(securityConfiguration),
                Substitute.For<IScopeProvider<SqlConnectionWrapperFactory>>(),
                Substitute.For<IMediator>(),
                sqlRetryService,
                NullLogger<SqlServerFhirModel>.Instance);

            typeof(SqlServerFhirModel)
                .GetField("_resourceTypeToId", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(model, new Dictionary<string, short>(StringComparer.Ordinal) { { "Patient", 1 } });

            typeof(SqlServerFhirModel)
                .GetField("_highestInitializedVersion", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(model, schemaInfo.Current);

            var storeClient = new SqlStoreClient(sqlRetryService, NullLogger<SqlStoreClient>.Instance, schemaInfo);

            CoreFeatureConfiguration coreFeatureConfiguration = new CoreFeatureConfiguration();
            BundleConfiguration bundleConfiguration = new BundleConfiguration();

            var sqlConnection = new SqlConnection();
            var sqlConnectionBuilder = Substitute.For<ISqlConnectionBuilder>();
            sqlConnectionBuilder.GetSqlConnectionAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs((x) => Task.FromResult(new SqlConnection()));

            SqlRetryLogicBaseProvider sqlRetryLogicBaseProvider = SqlConfigurableRetryFactory.CreateFixedRetryProvider(new SqlClientRetryOptions().Settings);

            var sqlServerDataStoreConfiguration = new SqlServerDataStoreConfiguration() { ConnectionString = sqlConnection.ConnectionString };

            var sqlConnectionWrapperFactory = new SqlConnectionWrapperFactory(sqlTransactionHandler, sqlConnectionBuilder, sqlRetryLogicBaseProvider, Options.Create(sqlServerDataStoreConfiguration));

            var dataStore = new SqlServerFhirDataStore(
                model,
                new SearchParameterToSearchValueTypeMap(),
                Options.Create(coreFeatureConfiguration),
                new BundleOrchestrator(
                    Options.Create(bundleConfiguration),
                    NullLogger<BundleOrchestrator>.Instance),
                sqlRetryService,
                sqlConnectionWrapperFactory,
                sqlTransactionHandler,
                Substitute.For<ICompressedRawResourceConverter>(),
                NullLogger<SqlServerFhirDataStore>.Instance,
                schemaInfo,
                ModelInfoProvider.Instance,
                Substitute.For<RequestContextAccessor<IFhirRequestContext>>(),
                Substitute.For<IImportErrorSerializer>(),
                storeClient);

            return dataStore;
        }

        private static SqlServerFhirModel GetModel(SqlServerFhirDataStore dataStore)
        {
            var modelField = typeof(SqlServerFhirDataStore).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance);

            if (modelField == null)
            {
                throw new InvalidOperationException("Field '_model' not found");
            }

            return (SqlServerFhirModel)modelField.GetValue(dataStore);
        }

        private static List<ResourceWrapperOperation> CreateResourceWrapperOperations(WeakETag weakETag = null, string comparedVersion = null)
        {
            var wrapper = CreateResourceWrapper("{\"resourceType\":\"Patient\",\"id\":\"123\"}");
            return new List<ResourceWrapperOperation>
            {
                new ResourceWrapperOperation(wrapper, allowCreate: true, keepHistory: false, weakETag: weakETag, requireETagOnUpdate: false, keepVersion: false, bundleResourceContext: null, comparedVersion: comparedVersion),
            };
        }

        /// <summary>
        /// One non-query SQL call recorded by <see cref="ConfigureRecordedNonQueryCalls"/>, including the bounds it
        /// was issued under.
        /// </summary>
        private sealed record RecordedNonQueryCall(string CommandText, int CommandTimeout, CancellationToken CancellationToken, bool DisableRetries);
    }
}
