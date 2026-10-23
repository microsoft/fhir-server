// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.SqlServer.Features.Metrics;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Metrics
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlConnectionPoolMetricsReporterTests
    {
        private readonly ISqlConnectionPoolMetricHandler _metricHandler = Substitute.For<ISqlConnectionPoolMetricHandler>();

        [Fact]
        public void GivenPollingCounterPayload_WhenParsed_ThenMeanIsReturned()
        {
            var payload = new Dictionary<string, object> { ["Name"] = "active-soft-connects", ["Mean"] = 42d, ["CounterType"] = "Mean" };

            bool parsed = SqlClientEventCounterListener.TryGetCounterValue(payload, out string counterName, out double value);

            Assert.True(parsed);
            Assert.Equal("active-soft-connects", counterName);
            Assert.Equal(42d, value);
        }

        [Fact]
        public void GivenIncrementingCounterPayload_WhenParsed_ThenIncrementIsReturned()
        {
            var payload = new Dictionary<string, object> { ["Name"] = "hard-connects", ["Increment"] = 7d, ["CounterType"] = "Sum" };

            bool parsed = SqlClientEventCounterListener.TryGetCounterValue(payload, out string counterName, out double value);

            Assert.True(parsed);
            Assert.Equal("hard-connects", counterName);
            Assert.Equal(7d, value);
        }

        [Fact]
        public void GivenPayloadWithoutValue_WhenParsed_ThenReturnsFalse()
        {
            var payload = new Dictionary<string, object> { ["Name"] = "hard-connects" };

            Assert.False(SqlClientEventCounterListener.TryGetCounterValue(payload, out _, out _));
        }

        [Fact]
        public async Task GivenInUseCounter_WhenReported_ThenConfiguredMaxPoolSizeIsReportedWithIt()
        {
            SqlConnectionPoolMetricsReporter reporter = CreateReporter(new SqlServerDataStoreConfiguration { ConnectionString = "Data Source=localhost;Encrypt=True", MaxPoolSize = 300 });
            await reporter.StartAsync(default);

            reporter.OnCounter(SqlConnectionPoolMetricsReporter.ActiveSoftConnectsCounter, 12);

            _metricHandler.Received(1).ReportConnectionPoolCounter(SqlConnectionPoolMetricsReporter.ActiveSoftConnectsCounter, 12);
            _metricHandler.Received(1).ReportConnectionPoolCounter(SqlConnectionPoolMetricsReporter.MaxPoolSizeCounter, 300);
            await reporter.StopAsync(default);
        }

        [Fact]
        public async Task GivenNoConfiguredMaxPoolSize_WhenInUseCounterReported_ThenConnectionStringMaxPoolSizeIsReported()
        {
            SqlConnectionPoolMetricsReporter reporter = CreateReporter(new SqlServerDataStoreConfiguration { ConnectionString = "Data Source=localhost;Encrypt=True;Max Pool Size=150" });
            await reporter.StartAsync(default);

            reporter.OnCounter(SqlConnectionPoolMetricsReporter.ActiveSoftConnectsCounter, 1);

            _metricHandler.Received(1).ReportConnectionPoolCounter(SqlConnectionPoolMetricsReporter.MaxPoolSizeCounter, 150);
            await reporter.StopAsync(default);
        }

        [Fact]
        public void GivenCounterNotInAllowList_WhenReported_ThenItIsIgnored()
        {
            SqlConnectionPoolMetricsReporter reporter = CreateReporter(new SqlServerDataStoreConfiguration { ConnectionString = "Data Source=localhost;Encrypt=True" });

            reporter.OnCounter("number-of-inactive-connection-pool-groups", 3);

            _metricHandler.DidNotReceiveWithAnyArgs().ReportConnectionPoolCounter(default, default);
        }

        [Fact]
        public void GivenHandlerThrows_WhenCounterReported_ThenExceptionIsNotPropagated()
        {
            _metricHandler.When(x => x.ReportConnectionPoolCounter(Arg.Any<string>(), Arg.Any<double>())).Throw(new InvalidOperationException());
            SqlConnectionPoolMetricsReporter reporter = CreateReporter(new SqlServerDataStoreConfiguration { ConnectionString = "Data Source=localhost;Encrypt=True" });

            Exception exception = Record.Exception(() => reporter.OnCounter("hard-connects", 1));

            Assert.Null(exception);
        }

        [Fact]
        public async Task GivenReporterStarted_WhenSqlClientIsUsed_ThenConnectionPoolCountersAreReported()
        {
            var reported = new ConcurrentDictionary<string, double>();
            _metricHandler.When(x => x.ReportConnectionPoolCounter(Arg.Any<string>(), Arg.Any<double>()))
                .Do(call => reported[call.ArgAt<string>(0)] = call.ArgAt<double>(1));
            SqlConnectionPoolMetricsReporter reporter = CreateReporter(new SqlServerDataStoreConfiguration { ConnectionString = "Data Source=localhost;Encrypt=True", MaxPoolSize = 200 }, intervalSeconds: 1);

            await reporter.StartAsync(default);
            try
            {
                using (new SqlConnection("Data Source=localhost;Encrypt=True"))
                {
                    SqlConnection.ClearAllPools();
                }

                var stopwatch = Stopwatch.StartNew();
                while (!reported.ContainsKey(SqlConnectionPoolMetricsReporter.MaxPoolSizeCounter) && stopwatch.Elapsed < TimeSpan.FromSeconds(30))
                {
                    await Task.Delay(100);
                }
            }
            finally
            {
                await reporter.StopAsync(default);
            }

            Assert.True(reported.ContainsKey("active-hard-connections"));
            Assert.True(reported.ContainsKey(SqlConnectionPoolMetricsReporter.ActiveSoftConnectsCounter));
            Assert.Equal(200, reported[SqlConnectionPoolMetricsReporter.MaxPoolSizeCounter]);
            Assert.All(reported.Keys, name => Assert.True(name == SqlConnectionPoolMetricsReporter.MaxPoolSizeCounter || SqlConnectionPoolMetricsReporter.ReportedCounters.Contains(name)));
        }

        [Fact]
        public async Task GivenReporterStarted_WhenSqlClientEventSourceIsEnabled_ThenSqlClientTracingStaysDisabled()
        {
            SqlConnectionPoolMetricsReporter reporter = CreateReporter(new SqlServerDataStoreConfiguration { ConnectionString = "Data Source=localhost;Encrypt=True" }, intervalSeconds: 1);

            await reporter.StartAsync(default);
            try
            {
                using (new SqlConnection("Data Source=localhost;Encrypt=True"))
                {
                    SqlConnection.ClearAllPools();
                }

                EventSource sqlClientEventSource = EventSource.GetSources().Single(s => s.Name == SqlClientEventCounterListener.SqlClientEventSourceName);

                Assert.True(sqlClientEventSource.IsEnabled());
                Assert.False(sqlClientEventSource.IsEnabled(EventLevel.Informational, EventKeywords.All));
                Assert.False(sqlClientEventSource.IsEnabled(EventLevel.Verbose, EventKeywords.All));
            }
            finally
            {
                await reporter.StopAsync(default);
            }
        }

        private SqlConnectionPoolMetricsReporter CreateReporter(SqlServerDataStoreConfiguration configuration, int intervalSeconds = SqlConnectionPoolMetricsReporter.DefaultIntervalSeconds)
        {
            return new SqlConnectionPoolMetricsReporter(
                _metricHandler,
                Options.Create(configuration),
                NullLogger<SqlConnectionPoolMetricsReporter>.Instance,
                intervalSeconds);
        }
    }
}
