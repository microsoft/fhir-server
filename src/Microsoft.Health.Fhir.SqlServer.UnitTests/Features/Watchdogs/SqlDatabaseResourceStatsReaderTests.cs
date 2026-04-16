// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Watchdogs
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class SqlDatabaseResourceStatsReaderTests
    {
        [Fact]
        public void GivenResourceStatsRow_WhenMapped_ThenAllValuesAreReturned()
        {
            using var reader = CreateReader(
                new DateTime(2026, 4, 16, 1, 15, 0, DateTimeKind.Unspecified),
                11.1,
                22.2,
                33.3,
                44.4,
                55.5,
                66.6,
                77.7,
                88.8);

            reader.Read();

            SqlDatabaseResourceStats stats = SqlDatabaseResourceStatsReader.Map(reader);

            Assert.Equal(new DateTimeOffset(2026, 4, 16, 1, 15, 0, TimeSpan.Zero), stats.EndTime);
            Assert.Equal(11.1, stats.CpuPercent);
            Assert.Equal(22.2, stats.DataIoPercent);
            Assert.Equal(33.3, stats.LogIoPercent);
            Assert.Equal(44.4, stats.MemoryPercent);
            Assert.Equal(55.5, stats.WorkersPercent);
            Assert.Equal(66.6, stats.SessionsPercent);
            Assert.Equal(77.7, stats.InstanceCpuPercent);
            Assert.Equal(88.8, stats.InstanceMemoryPercent);
        }

        [Fact]
        public void GivenResourceStatsRowWithoutOptionalValues_WhenMapped_ThenOptionalValuesAreNull()
        {
            using var reader = CreateReader(
                new DateTime(2026, 4, 16, 1, 20, 0, DateTimeKind.Utc),
                12.1,
                23.2,
                34.3,
                45.4,
                56.5,
                67.6,
                DBNull.Value,
                DBNull.Value);

            reader.Read();

            SqlDatabaseResourceStats stats = SqlDatabaseResourceStatsReader.Map(reader);

            Assert.Null(stats.InstanceCpuPercent);
            Assert.Null(stats.InstanceMemoryPercent);
            Assert.Equal(new DateTimeOffset(2026, 4, 16, 1, 20, 0, TimeSpan.Zero), stats.EndTime);
        }

        private static DataTableReader CreateReader(
            DateTime endTime,
            double cpuPercent,
            double dataIoPercent,
            double logIoPercent,
            double memoryPercent,
            double workersPercent,
            double sessionsPercent,
            object instanceCpuPercent,
            object instanceMemoryPercent)
        {
            var table = new DataTable();
            table.Columns.Add("end_time", typeof(DateTime));
            table.Columns.Add("avg_cpu_percent", typeof(double));
            table.Columns.Add("avg_data_io_percent", typeof(double));
            table.Columns.Add("avg_log_write_percent", typeof(double));
            table.Columns.Add("avg_memory_usage_percent", typeof(double));
            table.Columns.Add("max_worker_percent", typeof(double));
            table.Columns.Add("max_session_percent", typeof(double));
            table.Columns.Add("avg_instance_cpu_percent", typeof(double));
            table.Columns.Add("avg_instance_memory_percent", typeof(double));

            table.Rows.Add(
                endTime,
                cpuPercent,
                dataIoPercent,
                logIoPercent,
                memoryPercent,
                workersPercent,
                sessionsPercent,
                instanceCpuPercent,
                instanceMemoryPercent);

            return table.CreateDataReader();
        }
    }
}
