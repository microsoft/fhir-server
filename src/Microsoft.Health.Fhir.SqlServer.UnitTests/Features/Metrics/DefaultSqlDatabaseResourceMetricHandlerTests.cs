// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.SqlServer.Features.Metrics;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Metrics
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class DefaultSqlDatabaseResourceMetricHandlerTests
    {
        [Fact]
        public void GivenMetricsWithoutOptionalValues_WhenEmitIsCalled_ThenRequiredMetricsAreRecorded()
        {
            var measurements = new List<(string Name, double Value)>();
            using var listener = CreateListener(measurements);

            var handler = new DefaultSqlDatabaseResourceMetricHandler(new TestMeterFactory());

            handler.Emit(new SqlDatabaseResourceMetricNotification
            {
                CpuPercent = 10,
                DataIoPercent = 20,
                LogIoPercent = 30,
                MemoryPercent = 40,
                WorkersPercent = 50,
                SessionsPercent = 60,
            });

            Assert.Equal(6, measurements.Count);
            AssertMeasurement(measurements, "Sql.Database.CpuPercent", 10);
            AssertMeasurement(measurements, "Sql.Database.DataIoPercent", 20);
            AssertMeasurement(measurements, "Sql.Database.LogIoPercent", 30);
            AssertMeasurement(measurements, "Sql.Database.MemoryPercent", 40);
            AssertMeasurement(measurements, "Sql.Database.WorkersPercent", 50);
            AssertMeasurement(measurements, "Sql.Database.SessionsPercent", 60);
            Assert.DoesNotContain(measurements, x => x.Name == "Sql.Database.InstanceCpuPercent");
            Assert.DoesNotContain(measurements, x => x.Name == "Sql.Database.InstanceMemoryPercent");
        }

        [Fact]
        public void GivenMetricsWithOptionalValues_WhenEmitIsCalled_ThenOptionalMetricsAreRecorded()
        {
            var measurements = new List<(string Name, double Value)>();
            using var listener = CreateListener(measurements);

            var handler = new DefaultSqlDatabaseResourceMetricHandler(new TestMeterFactory());

            handler.Emit(new SqlDatabaseResourceMetricNotification
            {
                CpuPercent = 10,
                DataIoPercent = 20,
                LogIoPercent = 30,
                MemoryPercent = 40,
                WorkersPercent = 50,
                SessionsPercent = 60,
                InstanceCpuPercent = 70,
                InstanceMemoryPercent = 80,
            });

            Assert.Equal(8, measurements.Count);
            AssertMeasurement(measurements, "Sql.Database.InstanceCpuPercent", 70);
            AssertMeasurement(measurements, "Sql.Database.InstanceMemoryPercent", 80);
        }

        private static MeterListener CreateListener(ICollection<(string Name, double Value)> measurements)
        {
            var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == BaseMeterMetricHandler.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) => measurements.Add((instrument.Name, measurement)));
            listener.Start();

            return listener;
        }

        private static void AssertMeasurement(IEnumerable<(string Name, double Value)> measurements, string name, double expectedValue)
        {
            Assert.Contains(measurements, measurement => measurement.Name == name && Math.Abs(measurement.Value - expectedValue) < 0.001);
        }

        private sealed class TestMeterFactory : IMeterFactory
        {
            public Meter Create(MeterOptions options) => new(options);

            public void Dispose()
            {
            }
        }
    }
}
