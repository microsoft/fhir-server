// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Medino;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Metrics;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Watchdogs
{
    /// <summary>
    /// Covers the branch that reports a broken wait collection. The integration test can only assert that the status
    /// is not <c>Failed</c>, because it cannot make a live wait read fail, so without this the branch that exists
    /// specifically to make that breakage visible would never be executed by the suite.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class QueryStoreDiagnosticsWaitStatisticsTests
    {
        private const int DeadlockErrorNumber = 1205;

        [Fact]
        public async Task GivenAFailingWaitStatisticsRead_WhenCollecting_ThenSlowQueriesArePublishedWithFailedWaitStatus()
        {
            // Arrange
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            var mediator = Substitute.For<IMediator>();
            var published = new List<SlowQueryNotification>();
            mediator.When(x => x.PublishAsync(Arg.Any<SlowQueryNotification>(), Arg.Any<CancellationToken>()))
                .Do(info => published.Add((SlowQueryNotification)info[0]));

            var slowQuery = new QueryStoreDiagnosticsWatchdog.SlowQueryResult
            {
                QueryId = 11,
                PlanId = 22,
                ExecutionCount = 4,
                TotalDurationMilliseconds = 400,
                AverageDurationMilliseconds = 100,
                MaxDurationMilliseconds = 150,
                TotalCpuMilliseconds = 200,
                AverageCpuMilliseconds = 50,
                TotalLogicalReads = 80,
                AverageLogicalReads = 20,
                QueryText = "SELECT 1",
                IntervalStart = DateTimeOffset.UtcNow.AddMinutes(-5),
                IntervalEnd = DateTimeOffset.UtcNow,
            };

            sqlRetryService
                .ExecuteReaderAsync(
                    Arg.Any<SqlCommand>(),
                    Arg.Any<Func<SqlDataReader, QueryStoreDiagnosticsWatchdog.SlowQueryResult>>(),
                    Arg.Any<ILogger>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>())
                .Returns(new List<QueryStoreDiagnosticsWatchdog.SlowQueryResult> { slowQuery });

            sqlRetryService
                .ExecuteReaderAsync(
                    Arg.Any<SqlCommand>(),
                    Arg.Any<Func<SqlDataReader, QueryStoreDiagnosticsWatchdog.WaitStatistics>>(),
                    Arg.Any<ILogger>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>())
                .Returns<IReadOnlyList<QueryStoreDiagnosticsWatchdog.WaitStatistics>>(
                    _ => throw SqlExceptionFactory.GetSqlException(DeadlockErrorNumber, "Transaction was deadlocked on lock resources."));

            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(sqlRetryService, mediator);

            // Act
            await watchdog.CollectDiagnosticsAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, CancellationToken.None);

            // Assert
            // The runtime metrics are the primary signal, so a broken wait read must not suppress them...
            SlowQueryNotification notification = Assert.Single(published);
            Assert.Equal(slowQuery.QueryId, notification.QueryId);
            Assert.Equal(slowQuery.PlanId, notification.PlanId);
            Assert.Equal(slowQuery.TotalDurationMilliseconds, notification.TotalDurationMilliseconds);

            // ...and the breakage must be visible on the notification rather than looking like "this plan waited on
            // nothing", which is what an Unavailable status would mean.
            Assert.Equal(QueryStoreDiagnosticsWatchdog.WaitStatisticsFailedStatus, notification.WaitStatisticsStatus);
            Assert.Null(notification.TotalWaitMilliseconds);
            Assert.Null(notification.AverageWaitMilliseconds);
            Assert.Null(notification.TopWaitCategory);
        }

        private static QueryStoreDiagnosticsWatchdog CreateWatchdog(ISqlRetryService sqlRetryService, IMediator mediator)
        {
            var configuration = new WatchdogConfiguration();
            configuration.QueryStoreDiagnostics.Enabled = true;
            configuration.QueryStoreDiagnostics.SlowQueryCount = 10;
            configuration.QueryStoreDiagnostics.MinDurationMilliseconds = 1;

            // The plan and statistics sections are turned off so that the only reads this test has to stand up are
            // the two it is about.
            configuration.QueryStoreDiagnostics.IncludeQueryPlans = false;
            configuration.QueryStoreDiagnostics.IncludeStatisticsHealth = false;

            return new QueryStoreDiagnosticsWatchdog(
                sqlRetryService,
                NullLogger<QueryStoreDiagnosticsWatchdog>.Instance,
                mediator,
                Options.Create(configuration));
        }
    }
}
