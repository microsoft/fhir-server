// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics.Models;
using Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Watchdogs.QueryStoreDiagnostics
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class QueryStoreDiagnosticsStorageTests
    {
        [Fact]
        public void GivenDefaultConfiguration_WhenReadingStorageOptIn_ThenItIsDisabled()
        {
            var configuration = new QueryStoreDiagnosticsConfiguration();

            Assert.False(configuration.IncludeStorageDiagnostics);
        }

        [Theory]
        [InlineData(2, 33)]
        [InlineData(0, 4)]
        [InlineData(10000, 3)]
        public void GivenTypedStorageRows_WhenLogging_ThenEveryRowAndLongValueSurvivesBoundedJson(int batchSize, int expectedPages)
        {
            var logger = new CapturingLogger();
            var configuration = new WatchdogConfiguration();
            configuration.QueryStoreDiagnostics.StorageDiagnosticsBatchSize = batchSize;
            var watchdog = CreateWatchdog(configuration, logger, Substitute.For<ISqlRetryService>());
            var snapshot = new StorageDiagnosticsSnapshot(
                Enumerable.Range(0, 65).Select(index => new TableStorageDiagnostics
                {
                    SchemaName = "dbo",
                    TableName = $"Table{index}",
                    ApproximateRowCount = 3000000000L + index,
                    UsedBytes = 4000000000L + index,
                    ReservedBytes = 5000000000L + index,
                }).ToList(),
                new[] { new IndexStorageDiagnostics { SchemaName = "dbo", TableName = "Table0", IndexId = 2, IndexName = "Filtered", ApproximateIndexRowCount = 3000000001L } },
                new[] { new ResourceTypeStorageDiagnostics { SchemaName = "dbo", TableName = "Table0", Attribution = "NonalignedIndex", ApproximateRowCount = null, UsedBytes = 6000000000L } });

            watchdog.LogStorageDiagnosticsBatches(snapshot, CancellationToken.None);

            var pages = logger.Entries.Where(entry => Equals(entry["StorageKind"], "Tables")).ToList();
            Assert.Equal(expectedPages, pages.Count);
            Assert.Equal(Enumerable.Range(1, expectedPages), pages.Select(page => (int)page["StoragePage"]));
            Assert.All(pages, page =>
            {
                Assert.Equal(expectedPages, page["StoragePageCount"]);
                Assert.Equal(65, page["StorageTotalAvailable"]);
                Assert.Equal(65, page["StorageEmitted"]);
                Assert.Equal(false, page["StorageTruncated"]);
            });
            List<TableStorageDiagnostics> tables = pages.SelectMany(page => JsonSerializer.Deserialize<List<TableStorageDiagnostics>>((string)page["StorageRows"])).ToList();
            Assert.Equal(snapshot.Tables.Select(table => table.TableName), tables.Select(table => table.TableName));
            Assert.Equal(3000000004L, tables[4].ApproximateRowCount);
            Assert.Equal(4000000004L, tables[4].UsedBytes);
            Assert.Equal(5000000004L, tables[4].ReservedBytes);
            Assert.All(pages, page => Assert.InRange((int)page["StoragePageRowCount"], 1, 32));
            var indexRow = JsonSerializer.Deserialize<List<IndexStorageDiagnostics>>((string)Assert.Single(logger.Entries, entry => Equals(entry["StorageKind"], "Indexes"))["StorageRows"]);
            Assert.Equal(3000000001L, Assert.Single(indexRow).ApproximateIndexRowCount);
            var resourceRow = JsonSerializer.Deserialize<List<ResourceTypeStorageDiagnostics>>((string)Assert.Single(logger.Entries, entry => Equals(entry["StorageKind"], "ResourceTypes"))["StorageRows"]);
            Assert.Null(Assert.Single(resourceRow).ApproximateRowCount);
            Assert.Equal(6000000000L, resourceRow[0].UsedBytes);
        }

        [Fact]
        public async Task GivenThreeResultSets_WhenReading_ThenTypedFieldsAndMissingMetadataArePreserved()
        {
            using var tables = MakeTable(new[] { typeof(string), typeof(string), typeof(long), typeof(long), typeof(long) }, new object[] { "dbo", "Resource", 3000000000L, 4000000000L, 5000000000L });
            using var indexes = MakeTable(new[] { typeof(string), typeof(string), typeof(int), typeof(string), typeof(long), typeof(long), typeof(long) }, new object[] { "dbo", "Resource", 0, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value });
            using var resources = MakeTable(new[] { typeof(string), typeof(string), typeof(string), typeof(long), typeof(string), typeof(long), typeof(long), typeof(long) }, new object[] { "dbo", "Resource", "ResourceType", 7L, "Patient", 3000000000L, 4000000000L, 5000000000L });
            using var reader = new DataTableReader(new[] { tables, indexes, resources });

            StorageDiagnosticsSnapshot snapshot = await StorageDiagnosticsSnapshot.ReadAsync(reader, CancellationToken.None);

            Assert.Equal(3000000000L, Assert.Single(snapshot.Tables).ApproximateRowCount);
            Assert.Equal(4000000000L, snapshot.Tables[0].UsedBytes);
            Assert.Equal(5000000000L, snapshot.Tables[0].ReservedBytes);
            IndexStorageDiagnostics index = Assert.Single(snapshot.Indexes);
            Assert.Equal(0, index.IndexId);
            Assert.Null(index.IndexName);
            Assert.Null(index.ApproximateIndexRowCount);
            Assert.Null(index.UsedBytes);
            Assert.Null(index.ReservedBytes);
            ResourceTypeStorageDiagnostics resource = Assert.Single(snapshot.ResourceTypes);
            Assert.Equal("ResourceType", resource.Attribution);
            Assert.Equal(7L, resource.ResourceTypeId);
            Assert.Equal("Patient", resource.ResourceTypeName);
            Assert.Equal(3000000000L, resource.ApproximateRowCount);
            Assert.Equal(4000000000L, resource.UsedBytes);
            Assert.Equal(5000000000L, resource.ReservedBytes);
        }

        [Fact]
        public async Task GivenMissingResultSet_WhenReading_ThenTheSnapshotIsRejected()
        {
            using var table = MakeTable(new[] { typeof(string), typeof(string), typeof(long), typeof(long), typeof(long) });
            using var reader = table.CreateDataReader();

            await Assert.ThrowsAsync<InvalidOperationException>(() => StorageDiagnosticsSnapshot.ReadAsync(reader, CancellationToken.None));
        }

        [Fact]
        public async Task GivenEmptyCatalog_WhenReadingAndLogging_ThenTheSnapshotIsEmptyAndNoPagesAreEmitted()
        {
            using var tables = MakeTable(new[] { typeof(string), typeof(string), typeof(long), typeof(long), typeof(long) });
            using var indexes = MakeTable(new[] { typeof(string), typeof(string), typeof(int), typeof(string), typeof(long), typeof(long), typeof(long) });
            using var resources = MakeTable(new[] { typeof(string), typeof(string), typeof(string), typeof(long), typeof(string), typeof(long), typeof(long), typeof(long) });
            using var reader = new DataTableReader(new[] { tables, indexes, resources });
            var logger = new CapturingLogger();
            var watchdog = CreateWatchdog(new WatchdogConfiguration(), logger, Substitute.For<ISqlRetryService>());

            StorageDiagnosticsSnapshot snapshot = await StorageDiagnosticsSnapshot.ReadAsync(reader, CancellationToken.None);
            watchdog.LogStorageDiagnosticsBatches(snapshot, CancellationToken.None);

            Assert.Empty(snapshot.Tables);
            Assert.Empty(snapshot.Indexes);
            Assert.Empty(snapshot.ResourceTypes);
            Assert.Empty(logger.Entries);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public async Task GivenOptionalStorageFailure_WhenCollecting_ThenStatisticsRemainAndCancellationPropagates(int failure)
        {
            bool cancel = failure >= 3;
            var sql = Substitute.For<ISqlRetryService>();
            var logger = Substitute.For<ILogger<QueryStoreDiagnosticsWatchdog>>();
            var configuration = new WatchdogConfiguration();
            configuration.QueryStoreDiagnostics.IncludeQueryPlans = false;
            configuration.QueryStoreDiagnostics.IncludeStorageDiagnostics = true;
            sql.ExecuteReaderAsync(Arg.Any<SqlCommand>(), Arg.Any<Func<SqlDataReader, QueryStoreDiagnosticsWatchdog.SlowQueryResult>>(), Arg.Any<ILogger>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
                .Returns(new[] { new QueryStoreDiagnosticsWatchdog.SlowQueryResult { QueryText = "SELECT 1", PlanId = 1, ExecutionCount = 1 } });
            sql.ExecuteReaderAsync(Arg.Any<SqlCommand>(), Arg.Any<Func<SqlDataReader, QueryStoreDiagnosticsWatchdog.WaitStatistics>>(), Arg.Any<ILogger>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
                .Returns(Array.Empty<QueryStoreDiagnosticsWatchdog.WaitStatistics>());
            sql.ExecuteReaderAsync(Arg.Any<SqlCommand>(), Arg.Any<Func<SqlDataReader, StatisticsHealthDiagnostics>>(), Arg.Any<ILogger>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
                .Returns(Array.Empty<StatisticsHealthDiagnostics>());
            using var cancellation = new CancellationTokenSource();
            sql.ExecuteSql(
                Arg.Is<SqlCommand>(command => command.CommandText == "dbo.GetQueryStoreDiagnosticsStorage"),
                Arg.Any<Func<SqlCommand, CancellationToken, Task>>(),
                Arg.Any<ILogger>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<string>())
                .Returns(_ =>
                {
                    if (cancel)
                    {
                        cancellation.Cancel();
                        if (failure == 4)
                        {
                            throw SqlExceptionFactory.GetSqlException(0, "cancelled storage read");
                        }

                        throw new OperationCanceledException(cancellation.Token);
                    }

                    if (failure == 1)
                    {
                        throw SqlExceptionFactory.GetSqlException(229, "storage permission denied");
                    }

                    if (failure == 2)
                    {
                        throw new InvalidOperationException("missing result set");
                    }

                    throw new TimeoutException("storage failed");
                });
            var watchdog = CreateWatchdog(configuration, logger, sql);

            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watchdog.CollectDiagnosticsAsync(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, cancellation.Token));
            }
            else
            {
                await watchdog.CollectDiagnosticsAsync(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, cancellation.Token);
                Assert.Contains(logger.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(ILogger.Log)
                    && call.GetArguments()[2] is IReadOnlyList<KeyValuePair<string, object>> state
                    && state.Any(property => property.Key == "StorageDiagnosticsFailed" && Equals(property.Value, true)));
            }

            await sql.Received(1).ExecuteReaderAsync(Arg.Any<SqlCommand>(), Arg.Any<Func<SqlDataReader, StatisticsHealthDiagnostics>>(), Arg.Any<ILogger>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<bool>());
            Assert.Contains(logger.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(ILogger.Log)
                && call.GetArguments()[2].ToString().StartsWith("QueryStoreDiagnosticsWatchdog slow query.", StringComparison.Ordinal));
        }

        [Fact]
        public async Task GivenStorageDisabled_WhenCollecting_ThenNoStorageReadOccurs()
        {
            var sql = Substitute.For<ISqlRetryService>();
            var configuration = new WatchdogConfiguration();
            configuration.QueryStoreDiagnostics.SlowQueryCount = 0;
            configuration.QueryStoreDiagnostics.IncludeStatisticsHealth = false;
            var watchdog = CreateWatchdog(configuration, Substitute.For<ILogger<QueryStoreDiagnosticsWatchdog>>(), sql);

            await watchdog.CollectDiagnosticsAsync(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, CancellationToken.None);

            Assert.Empty(sql.ReceivedCalls());
        }

        [Fact]
        public void GivenCancelledEmission_WhenLogging_ThenNoStoragePageIsEmitted()
        {
            var logger = new CapturingLogger();
            var watchdog = CreateWatchdog(new WatchdogConfiguration(), logger, Substitute.For<ISqlRetryService>());
            var snapshot = new StorageDiagnosticsSnapshot(new[] { new TableStorageDiagnostics() }, Array.Empty<IndexStorageDiagnostics>(), Array.Empty<ResourceTypeStorageDiagnostics>());

            Assert.ThrowsAny<OperationCanceledException>(() => watchdog.LogStorageDiagnosticsBatches(snapshot, new CancellationToken(true)));
            Assert.Empty(logger.Entries);
        }

        private static DataTable MakeTable(Type[] types, params object[][] rows)
        {
            var table = new DataTable();
            foreach (Type type in types)
            {
                table.Columns.Add($"Column{table.Columns.Count}", type);
            }

            foreach (object[] row in rows)
            {
                table.Rows.Add(row);
            }

            return table;
        }

        private static QueryStoreDiagnosticsWatchdog CreateWatchdog(WatchdogConfiguration configuration, ILogger<QueryStoreDiagnosticsWatchdog> logger, ISqlRetryService sql) =>
            new QueryStoreDiagnosticsWatchdog(sql, logger, Options.Create(configuration), new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max) { Current = SchemaVersionConstants.Max });

        private sealed class CapturingLogger : ILogger<QueryStoreDiagnosticsWatchdog>
        {
            internal List<Dictionary<string, object>> Entries { get; } = new List<Dictionary<string, object>>();

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                if (state is IReadOnlyList<KeyValuePair<string, object>> properties && properties.Any(property => property.Key == "StorageRows"))
                {
                    Entries.Add(properties.ToDictionary(property => property.Key, property => property.Value));
                }
            }
        }
    }
}
