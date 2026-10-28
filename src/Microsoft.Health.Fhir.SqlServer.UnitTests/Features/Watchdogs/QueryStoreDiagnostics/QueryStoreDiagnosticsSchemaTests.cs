// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics;
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
    public class QueryStoreDiagnosticsSchemaTests
    {
        [Fact]
        public async Task GivenMissingProcedure_WhenCollecting_ThenTheWatchdogLogsTheSchemaProblem()
        {
            var schema = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max) { Current = SchemaVersionConstants.Max };
            var sql = new CapturingSqlRetryService { ErrorNumber = 2812 };
            var logger = Substitute.For<ILogger<QueryStoreDiagnosticsWatchdog>>();
            var configuration = new WatchdogConfiguration();
            configuration.QueryStoreDiagnostics.Enabled = true;
            var watchdog = new QueryStoreDiagnosticsWatchdog(sql, logger, Options.Create(configuration), schema);

            await watchdog.RunWorkForTestingAsync(CancellationToken.None);

            Assert.Contains(
                logger.ReceivedCalls(),
                call => call.GetMethodInfo().Name == nameof(ILogger.Log)
                    && (LogLevel)call.GetArguments()[0] == LogLevel.Warning
                    && call.GetArguments()[2].ToString().Contains("required diagnostics stored procedure is missing", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(229)]
        [InlineData(262)]
        [InlineData(297)]
        [InlineData(300)]
        public async Task GivenInsufficientPermissions_WhenCollecting_ThenTheWatchdogLogsThePermissionFailure(int errorNumber)
        {
            var schema = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max) { Current = SchemaVersionConstants.Max };
            var sql = new CapturingSqlRetryService { ErrorNumber = errorNumber };
            var logger = Substitute.For<ILogger<QueryStoreDiagnosticsWatchdog>>();
            var configuration = new WatchdogConfiguration();
            configuration.QueryStoreDiagnostics.Enabled = true;
            var watchdog = new QueryStoreDiagnosticsWatchdog(sql, logger, Options.Create(configuration), schema);

            await watchdog.RunWorkForTestingAsync(CancellationToken.None);

            Assert.Contains(
                logger.ReceivedCalls(),
                call => call.GetMethodInfo().Name == nameof(ILogger.Log)
                    && (LogLevel)call.GetArguments()[0] == LogLevel.Warning
                    && call.GetArguments()[2].ToString().Contains("SQL permissions do not allow diagnostics collection", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(null)]
        [InlineData(119)]
        public async Task GivenUnavailableSchema_WhenTheSchemaIsUpgraded_ThenTheNextTickAttemptsCollection(int? currentVersion)
        {
            var schema = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max) { Current = currentVersion };
            var sql = new CapturingSqlRetryService();
            var logger = Substitute.For<ILogger<QueryStoreDiagnosticsWatchdog>>();
            var configuration = new WatchdogConfiguration();
            configuration.QueryStoreDiagnostics.Enabled = true;
            var watchdog = new QueryStoreDiagnosticsWatchdog(sql, logger, Options.Create(configuration), schema);

            await watchdog.RunWorkForTestingAsync(CancellationToken.None);

            Assert.Equal(0, sql.ReadCount);
            Assert.Contains(
                logger.ReceivedCalls(),
                call => call.GetMethodInfo().Name == nameof(ILogger.Log)
                    && (LogLevel)call.GetArguments()[0] == LogLevel.Warning
                    && call.GetArguments()[2].ToString().Contains("require schema version 120", StringComparison.Ordinal));

            schema.Current = SchemaVersionConstants.QueryStoreDiagnosticsVersion;
            await watchdog.RunWorkForTestingAsync(CancellationToken.None);

            Assert.Equal(1, sql.ReadCount);
            Assert.Equal("dbo.GetQueryStoreDiagnosticsState", sql.CommandText);
            Assert.Equal(CommandType.StoredProcedure, sql.CommandType);
            Assert.False(sql.IsReadOnly);
        }

        private sealed class CapturingSqlRetryService : ISqlRetryService
        {
            public int ReadCount { get; private set; }

            public string CommandText { get; private set; }

            public CommandType CommandType { get; private set; }

            public bool IsReadOnly { get; private set; }

            public int? ErrorNumber { get; set; }

            public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(SqlCommand command, Func<SqlDataReader, TResult> readerToResult, ILogger logger, string logMessage, CancellationToken cancellationToken, bool isReadOnly = false)
            {
                ReadCount++;
                CommandText = command.CommandText;
                CommandType = command.CommandType;
                IsReadOnly = isReadOnly;
                if (ErrorNumber.HasValue)
                {
                    throw SqlExceptionFactory.GetSqlException(ErrorNumber.Value, "Diagnostics read failed.");
                }

                return Task.FromResult<IReadOnlyList<TResult>>(Array.Empty<TResult>());
            }

            public Task TryLogEvent(string process, string status, string text, DateTime? startDate, CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task ExecuteSql(Func<SqlConnection, CancellationToken, SqlException, Task> action, ILogger logger, CancellationToken cancellationToken, bool isReadOnly = false, Func<SqlException, bool> shouldNotRetry = null) => throw new NotSupportedException();

            public Task ExecuteSql(SqlCommand command, Func<SqlCommand, CancellationToken, Task> action, ILogger logger, string logMessage, CancellationToken cancellationToken, bool isReadOnly = false, bool disableRetries = false, string applicationName = null) => throw new NotSupportedException();

            public Task<IReadOnlyList<IReadOnlyList<TResult>>> ExecuteMultiResultReaderAsync<TResult>(SqlCommand command, IList<Func<SqlDataReader, TResult>> readersToResult, ILogger logger, string logMessage, CancellationToken cancellationToken, bool isReadOnly = false) => throw new NotSupportedException();
        }
    }
}
