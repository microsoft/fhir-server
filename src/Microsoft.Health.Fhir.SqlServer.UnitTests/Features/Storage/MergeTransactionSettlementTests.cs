// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    /// <summary>
    /// Covers the cleanup contract of <see cref="MergeTransactionSettlement"/>: a merge that never reached
    /// dbo.MergeResources must still try to settle its transaction, but that attempt runs while a request is already
    /// failing, so it must be bounded rather than allowed to consume the store's full retry and command timeout
    /// budget.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class MergeTransactionSettlementTests
    {
        private const long TransactionId = 42;
        private const string CommitTransaction = "dbo.MergeResourcesCommitTransaction";

        /// <summary>
        /// Bound injected by the tests that must observe the give-up path. Its exact value does not decide any
        /// assertion: the fake settlement below completes only when its token is cancelled, so those tests can only
        /// finish if the bound actually fires.
        /// </summary>
        private static readonly TimeSpan InjectedTimeout = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// Turns "settlement never returned" into a failed assertion instead of a hung test host. It is two orders of
        /// magnitude above <see cref="InjectedTimeout"/>, so it never decides a result on a correct implementation.
        /// </summary>
        private static readonly TimeSpan UnresponsiveGuard = TimeSpan.FromSeconds(30);

        [Fact]
        public async Task DisposeAsync_WhenTheMergeNeverReachedMergeResources_ThenSettlementIsBoundedAndDoesNotRetry()
        {
            // Arrange - disposal happens on an already-failing request. Running the settlement through the ordinary
            // retry path lets a datastore blip spend the configured retry count multiplied by the configured command
            // timeout before the caller ever sees its own failure.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            List<SettlementCall> calls = ConfigureSettlement(sqlRetryService);
            var settlement = new MergeTransactionSettlement(CreateStoreClient(sqlRetryService), NullLogger.Instance, TransactionId);

            // Act
            await settlement.DisposeAsync();

            // Assert
            SettlementCall call = Assert.Single(calls);
            Assert.Equal(CommitTransaction, call.CommandText, StringComparer.Ordinal);
            Assert.True(call.DisableRetries, "Settlement must not re-enter the SQL retry loop while the caller waits.");
            Assert.InRange(call.CommandTimeout, 1, (int)MergeTransactionSettlement.SettlementTimeout.TotalSeconds);
            Assert.False(call.IsReadOnly, "Settlement is a write and must never be sent to a read-only replica.");
        }

        [Fact]
        public async Task DisposeAsync_WhenTheMergeNeverReachedMergeResources_ThenSettlementGetsItsOwnCancellableToken()
        {
            // Arrange - the token must be cancellable (that is what bounds the attempt) but it must not be the
            // caller's token: the most common way to reach disposal without transferring is that the caller's token
            // was already cancelled, and settling under it would remove the only in-band chance to clean up.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            List<SettlementCall> calls = ConfigureSettlement(sqlRetryService);
            var settlement = new MergeTransactionSettlement(CreateStoreClient(sqlRetryService), NullLogger.Instance, TransactionId);

            // Act
            await settlement.DisposeAsync();

            // Assert
            SettlementCall call = Assert.Single(calls);
            Assert.True(call.CancellationToken.CanBeCanceled, "Settlement must run under an explicitly bounded token.");
            Assert.False(call.CancellationToken.IsCancellationRequested, "Settlement must not start already cancelled.");
        }

        [Fact]
        public void SettlementTimeout_IsASmallExplicitUpperBound()
        {
            // The watchdog is the durable backstop, so this in-band attempt only exists to save the watchdog a
            // round trip. It must stay small enough that a failing request is never held for a datastore outage.
            Assert.InRange(MergeTransactionSettlement.SettlementTimeout, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task DisposeAsync_WhenSettlementNeverResponds_ThenItGivesUpWithinItsBoundInsteadOfBlockingTheCaller()
        {
            // Arrange - the fake settlement completes only when its own token is cancelled, so this test can only
            // finish if the bound fires. Nothing here sleeps or compares wall-clock durations.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            List<SettlementCall> calls = ConfigureSettlement(sqlRetryService, token => Task.Delay(Timeout.Infinite, token));
            var logger = new CapturingLogger();
            var settlement = new MergeTransactionSettlement(CreateStoreClient(sqlRetryService), logger, TransactionId, InjectedTimeout);

            // Act
            Task disposal = settlement.DisposeAsync().AsTask();
            Task finished = await Task.WhenAny(disposal, Task.Delay(UnresponsiveGuard));

            // Assert
            Assert.True(ReferenceEquals(finished, disposal), "Settlement did not give up: an unresponsive datastore blocked disposal.");
            await disposal;

            SettlementCall call = Assert.Single(calls);
            Assert.True(call.CancellationToken.IsCancellationRequested, "The settlement attempt must be cancelled by its own bound.");
            Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task DisposeAsync_WhenSettlementIsAbandoned_ThenTheWarningIdentifiesTheTransaction()
        {
            // Arrange
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            ConfigureSettlement(sqlRetryService, token => Task.Delay(Timeout.Infinite, token));
            var logger = new CapturingLogger();
            var settlement = new MergeTransactionSettlement(CreateStoreClient(sqlRetryService), logger, TransactionId, InjectedTimeout);

            // Act
            Task disposal = settlement.DisposeAsync().AsTask();
            Task finished = await Task.WhenAny(disposal, Task.Delay(UnresponsiveGuard));
            Assert.True(ReferenceEquals(finished, disposal), "Settlement did not give up: an unresponsive datastore blocked disposal.");
            await disposal;

            // Assert - an abandoned transaction is only recoverable by the watchdog, so it can never be dropped
            // silently.
            (LogLevel Level, string Message, Exception Exception) warning = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
            Assert.Contains(TransactionId.ToString(System.Globalization.CultureInfo.InvariantCulture), warning.Message, StringComparison.Ordinal);
            Assert.NotNull(warning.Exception);
        }

        [Fact]
        public async Task DisposeAsync_WhenSettlementFails_ThenItLogsAWarningAndDoesNotThrow()
        {
            // Arrange - throwing from disposal would replace the failure that actually ended the merge.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            SqlException failure = SqlExceptionFactory.GetSqlException(50000, "settlement failed");
            ConfigureSettlement(sqlRetryService, _ => Task.FromException(failure));
            var logger = new CapturingLogger();
            var settlement = new MergeTransactionSettlement(CreateStoreClient(sqlRetryService), logger, TransactionId);

            // Act
            await settlement.DisposeAsync();

            // Assert
            (LogLevel Level, string Message, Exception Exception) warning = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
            Assert.Same(failure, warning.Exception);
            Assert.Contains(TransactionId.ToString(System.Globalization.CultureInfo.InvariantCulture), warning.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task DisposeAsync_AfterTransferToMergeExecution_ThenTheTransactionIsLeftToMergeResources()
        {
            // Arrange - once dbo.MergeResources owns the transaction, settling it here would fight the deliberate
            // decision to let the watchdog roll a non single-transaction merge forward.
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            List<SettlementCall> calls = ConfigureSettlement(sqlRetryService);
            var settlement = new MergeTransactionSettlement(CreateStoreClient(sqlRetryService), NullLogger.Instance, TransactionId);

            // Act
            settlement.TransferToMergeExecution();
            await settlement.DisposeAsync();

            // Assert
            Assert.Empty(calls);
        }

        private static SqlStoreClient CreateStoreClient(ISqlRetryService sqlRetryService)
        {
            return new SqlStoreClient(
                sqlRetryService,
                NullLogger<SqlStoreClient>.Instance,
                new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max) { Current = SchemaVersionConstants.Max });
        }

        /// <summary>
        /// Records how each settlement was issued and, optionally, replaces the datastore round trip with a
        /// deterministic behavior such as "never answers until cancelled".
        /// </summary>
        private static List<SettlementCall> ConfigureSettlement(ISqlRetryService sqlRetryService, Func<CancellationToken, Task> behavior = null)
        {
            var calls = new List<SettlementCall>();

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
                    SqlCommand cmd = callInfo.ArgAt<SqlCommand>(0);
                    CancellationToken cancellationToken = callInfo.ArgAt<CancellationToken>(4);
                    calls.Add(new SettlementCall(cmd.CommandText, cmd.CommandTimeout, cancellationToken, callInfo.ArgAt<bool>(5), callInfo.ArgAt<bool>(6)));

                    return behavior == null ? Task.CompletedTask : behavior(cancellationToken);
                });

            return calls;
        }

        private sealed record SettlementCall(string CommandText, int CommandTimeout, CancellationToken CancellationToken, bool IsReadOnly, bool DisableRetries);

        private sealed class CapturingLogger : ILogger
        {
            public List<(LogLevel Level, string Message, Exception Exception)> Entries { get; } = new List<(LogLevel Level, string Message, Exception Exception)>();

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
                => NoOpScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                Entries.Add((logLevel, formatter(state, exception), exception));
            }

            private sealed class NoOpScope : IDisposable
            {
                public static readonly NoOpScope Instance = new NoOpScope();

                public void Dispose()
                {
                }
            }
        }
    }
}
