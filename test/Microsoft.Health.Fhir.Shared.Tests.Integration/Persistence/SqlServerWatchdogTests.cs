// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerWatchdogTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private readonly SqlServerFhirStorageTestsFixture _fixture;

        public SqlServerWatchdogTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void Defrag()
        {
            var wd = new DefragWatchdog(
                _fixture.SchemaInformation,
                () => _fixture.SqlConnectionWrapperFactory.CreateMockScope(),
                () => new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _fixture.SchemaInformation, new NullLogger<SqlQueueClient>()).CreateMockScope(),
                new NullLogger<DefragWatchdog>());

            // populate data
            ExecuteSql("CREATE TABLE DefragTestTable (Id int IDENTITY(1, 1), Data char(500) NOT NULL PRIMARY KEY(Id))");
            ExecuteSql("INSERT INTO DefragTestTable SELECT TOP 50000 '' FROM syscolumns A1, syscolumns A2");
            ExecuteSql("DELETE FROM DefragTestTable WHERE Id % 10 IN (0,1,2,3,4,5,6,7,8)");
            var pagesBefore = GetPages();

            wd.Start();
            ExecuteSql($"UPDATE dbo.Parameters SET Number = 1 WHERE Id = '{DefragWatchdog.IsEnabledId}'");
            ExecuteSql($"INSERT INTO dbo.Parameters (Id,Number) SELECT 'Defrag.MinFragPct', 0");
            ExecuteSql($"INSERT INTO dbo.Parameters (Id,Number) SELECT 'Defrag.MinSizeGB', 0.01");
            wd.Change(2);
            Thread.Sleep(10000);
            wd.Dispose();

            // check exec
            var result = CheckQueue();
            Assert.True(result.coordExists, "Coordinator item exists");
            Assert.True(result.workExists, "Work item exists");

            var pagesAfter = GetPages();
            Assert.True(pagesAfter * 9 < pagesBefore, $"{pagesAfter} * 9 < {pagesBefore}");
        }

        private void ExecuteSql(string sql)
        {
            using var conn = new SqlConnection(_fixture.TestConnectionString);
            conn.Open();
            using var cmd = new SqlCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        private long GetPages()
        {
            using var conn = new SqlConnection(_fixture.TestConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT sum(used_page_count) FROM sys.dm_db_partition_stats WHERE object_id = object_id('DefragTestTable')", conn);
            var res = cmd.ExecuteScalar();
            return (long)res;
        }

        private (bool coordExists, bool workExists) CheckQueue()
        {
            using var conn = new SqlConnection(_fixture.TestConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT TOP 10 Definition FROM dbo.JobQueue WHERE QueueType = @QueueType ORDER BY JobId DESC", conn);
            cmd.Parameters.AddWithValue("@QueueType", Core.Features.Operations.QueueType.Defrag);
            using var reader = cmd.ExecuteReader();
            var coordExists = false;
            var workExists = false;
            while (reader.Read())
            {
                var def = reader.GetString(0);
                if (def == "Defrag")
                {
                    coordExists = true;
                }

                if (def.Contains("DefragTestTable"))
                {
                    workExists = true;
                }
            }

            return (coordExists, workExists);
        }
    }
}
