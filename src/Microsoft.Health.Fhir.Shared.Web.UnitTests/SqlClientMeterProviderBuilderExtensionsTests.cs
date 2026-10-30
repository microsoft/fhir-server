// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Web.UnitTests
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class SqlClientMeterProviderBuilderExtensionsTests
    {
        private const string EventCountersMeterName = "OpenTelemetry.Instrumentation.EventCounters";

        private static readonly string[] SnapshotCounters = ["active-hard-connections", "active-soft-connects", "number-of-active-connection-pools", "number-of-free-connections"];

        private static readonly string[] IncrementCounters = ["hard-connects", "soft-connects", "number-of-reclaimed-connections"];

        [Fact]
        public async Task GivenSqlClientInstrumentation_WhenSqlClientCreatesAPool_ThenSnapshotCountersAreGaugesAndIncrementsAreCounters()
        {
            var instruments = new ConcurrentDictionary<string, Instrument>();
            var values = new ConcurrentDictionary<string, double>();
            bool capturing = false;
            using var listener = new MeterListener
            {
                InstrumentPublished = (instrument, meterListener) =>
                {
                    // The EventCounters meter is process-wide, so instruments created by earlier meter providers are published when the
                    // listener starts. Only instruments created by the provider below count.
                    if (Volatile.Read(ref capturing) && instrument.Meter.Name == EventCountersMeterName)
                    {
                        instruments[instrument.Name] = instrument;
                        meterListener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) => values[instrument.Name] = value);
            listener.Start();
            Volatile.Write(ref capturing, true);

            using (MeterProvider meterProvider = Sdk.CreateMeterProviderBuilder().AddSqlClientConnectionPoolInstrumentation().Build())
            {
                await CreateSqlClientPoolAsync();

                await WaitUntilAsync(() => SnapshotCounters.Concat(IncrementCounters).All(counter => FindInstrument(instruments, counter) != null));

                listener.RecordObservableInstruments();
            }

            Assert.All(SnapshotCounters, counter => Assert.IsType<ObservableGauge<double>>(FindInstrument(instruments, counter)));
            Assert.All(IncrementCounters, counter => Assert.IsType<ObservableCounter<double>>(FindInstrument(instruments, counter)));
            Assert.True(values[FindInstrument(instruments, "number-of-active-connection-pools").Name] >= 1);
        }

        [Fact]
        public async Task GivenSqlClientInstrumentation_WhenCountersAreEnabled_ThenSqlClientTracingStaysDisabled()
        {
            using MeterProvider meterProvider = Sdk.CreateMeterProviderBuilder().AddSqlClientConnectionPoolInstrumentation().Build();
            await CreateSqlClientPoolAsync();

            EventSource sqlClientEventSource = EventSource.GetSources().Single(source => source.Name == SqlClientMeterProviderBuilderExtensions.SqlClientEventSourceName);

            Assert.True(sqlClientEventSource.IsEnabled());
            Assert.False(sqlClientEventSource.IsEnabled(EventLevel.Informational, EventKeywords.All));
            Assert.False(sqlClientEventSource.IsEnabled(EventLevel.Verbose, EventKeywords.All));
        }

        private static async Task CreateSqlClientPoolAsync()
        {
            // SqlClient creates the connection pool before it tries to reach the server, so a connection to a closed port still creates one.
            using var connection = new SqlConnection("Data Source=tcp:127.0.0.1,1;Connect Timeout=1;Encrypt=True");
            await Assert.ThrowsAsync<SqlException>(() => connection.OpenAsync());
        }

        private static Instrument FindInstrument(IDictionary<string, Instrument> instruments, string counterName)
        {
            return instruments.Values.FirstOrDefault(instrument =>
                instrument.Name.StartsWith("ec.Microsoft.", StringComparison.Ordinal) && instrument.Name.EndsWith("." + counterName, StringComparison.Ordinal));
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!condition() && stopwatch.Elapsed < TimeSpan.FromSeconds(30))
            {
                await Task.Delay(100);
            }

            Assert.True(condition(), "The SqlClient counters were not published within 30 seconds.");
        }
    }
}
