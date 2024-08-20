// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel.Types;
using Hl7.Fhir.Rest;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Microsoft.SqlServer.Management.Sdk.Sfc;
using NSubstitute.Core;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using static Antlr4.Runtime.Atn.SemanticContext;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Tests which retrieve a custom stored procedure if one exists for a given
    /// SQL query based on hash
    /// </summary>
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlComplexQueryTests : IClassFixture<FhirStorageTestsFixture>
    {
        private readonly FhirStorageTestsFixture _fixture;
        private readonly ITestOutputHelper _output;

        public SqlComplexQueryTests(FhirStorageTestsFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Fact]
        public async Task GivenSearchQuery_IfReuseQueryPlansIsEnabled_ThenPlansAreReusedAcrossDifferentParameterValues()
        {
            // warm up stats
            await _fixture.SearchService.SearchAsync(KnownResourceTypes.Patient, [Tuple.Create("address-city", "None")], CancellationToken.None);
            await Task.Delay(5000);
            await SetGranularQueryStore();

            var retries = 0;
            while (retries < 3)
            {
                try
                {
                    await DisableResuseQueryPlans();
                    await ResetQueryStore();
                    SqlServerSearchService.ResetReuseQueryPlans();
                    await _fixture.SearchService.SearchAsync(KnownResourceTypes.Patient, [Tuple.Create("address-city", "City1")], CancellationToken.None);
                    await _fixture.SearchService.SearchAsync(KnownResourceTypes.Patient, [Tuple.Create("address-city", "City2")], CancellationToken.None);
                    //// values are different and plans are NOT reused
                    await CheckQueryStore(2, 2);

                    await ResetQueryStore();
                    await _fixture.SearchService.SearchAsync(KnownResourceTypes.Patient, [Tuple.Create("address-city", "City")], CancellationToken.None);
                    await _fixture.SearchService.SearchAsync(KnownResourceTypes.Patient, [Tuple.Create("address-city", "City")], CancellationToken.None);
                    //// values are same and plans are reused
                    await CheckQueryStore(2, 1);

                    await EnableResuseQueryPlans(); //// new behavior
                    await ResetQueryStore();
                    SqlServerSearchService.ResetReuseQueryPlans();
                    await _fixture.SearchService.SearchAsync(KnownResourceTypes.Patient, [Tuple.Create("address-city", "City1")], CancellationToken.None);
                    await _fixture.SearchService.SearchAsync(KnownResourceTypes.Patient, [Tuple.Create("address-city", "City2")], CancellationToken.None);
                    await _fixture.SearchService.SearchAsync(KnownResourceTypes.Patient, [Tuple.Create("address-city", "City3")], CancellationToken.None);
                    await _fixture.SearchService.SearchAsync(KnownResourceTypes.Patient, [Tuple.Create("address-city", "City4")], CancellationToken.None);
                    //// values are different but plans are reused
                    await CheckQueryStore(4, 1);

                    break;
                }
                catch
                {
                    retries++;
                    if (retries > 3)
                    {
                        throw;
                    }
                }
            }
        }

        private async Task CheckQueryStore(int expected_executions, int expected_compiles)
        {
            await Task.Delay(1000);
            using var conn = await _fixture.SqlHelper.GetSqlConnectionAsync();
            using var cmd = new SqlCommand(
                @"
DECLARE @executions int
       ,@compiles int
       ,@msg varchar(1000)
BEGIN TRY
  SELECT @executions = sum(count_executions), @compiles = sum(q.count_compiles)
    FROM sys.query_store_runtime_stats s
         JOIN sys.query_store_plan p on p.plan_id = s.plan_id 
         JOIN sys.query_store_query q on q.query_id = p.query_id
         JOIN sys.query_store_query_text qt on qt.query_text_id = q.query_text_id
    WHERE query_sql_text LIKE '%StringSearchParam%' AND query_sql_text NOT LIKE '%sys.query_store_query%'
  IF @expected_executions <> @executions
  BEGIN
    SET @msg = '@expected_executions='+convert(varchar,@expected_executions)+' <> @executions='+convert(varchar,@executions)
    RAISERROR(@msg,18,127)
  END
  IF @expected_compiles <> @compiles
  BEGIN
    SET @msg = '@expected_compiles='+convert(varchar,@expected_compiles)+' <> @compiles='+convert(varchar,@compiles)
    RAISERROR(@msg,18,127)
  END
END TRY
BEGIN CATCH
  THROW
END CATCH
                ",
                conn);
            cmd.Parameters.AddWithValue("@expected_executions", expected_executions);
            cmd.Parameters.AddWithValue("@expected_compiles", expected_compiles);
            conn.Open();
            cmd.ExecuteNonQuery();
        }

        private async Task DisableResuseQueryPlans()
        {
            using var conn = await _fixture.SqlHelper.GetSqlConnectionAsync();
            using var cmd = new SqlCommand("DELETE FROM dbo.Parameters WHERE Id = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", SqlServerSearchService.ReuseQueryPlansParameterId);
            conn.Open();
            cmd.ExecuteNonQuery();
        }

        private async Task EnableResuseQueryPlans()
        {
            using var conn = await _fixture.SqlHelper.GetSqlConnectionAsync();
            using var cmd = new SqlCommand("INSERT INTO dbo.Parameters (Id,Number) SELECT @Id, 1", conn);
            cmd.Parameters.AddWithValue("@Id", SqlServerSearchService.ReuseQueryPlansParameterId);
            conn.Open();
            cmd.ExecuteNonQuery();
        }

        private async Task SetGranularQueryStore()
        {
            using var conn = await _fixture.SqlHelper.GetSqlConnectionAsync();
            conn.Open();
            using var cmd = new SqlCommand("DECLARE @db varchar(100) = db_name() EXECUTE('ALTER DATABASE ['+@db+'] SET QUERY_STORE = ON (QUERY_CAPTURE_MODE = ALL)')", conn);
            cmd.ExecuteNonQuery();
        }

        private async Task ResetQueryStore()
        {
            using var conn = await _fixture.SqlHelper.GetSqlConnectionAsync();
            conn.Open();
            using var cmd = new SqlCommand("DECLARE @db varchar(100) = db_name() EXECUTE('ALTER DATABASE ['+@db+'] SET QUERY_STORE CLEAR')", conn);
            cmd.ExecuteNonQuery();
            await Task.Delay(1000);
        }

        [SkippableFact]
        public async Task GivenASqlQuery_IfAStoredProcExistsWithMatchingHash_ThenStoredProcUsed()
        {
            using var conn = await _fixture.SqlHelper.GetSqlConnectionAsync();
            _output.WriteLine($"database={conn.Database}");

            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4,
                "This test is only valid for R4");

            // set the wait time to 1 second
            CustomQueries.WaitTime = 1;

            // use more unique call to avoid racing
            var query = new[] { Tuple.Create("birthdate", "gt1800-01-01"), Tuple.Create("birthdate", "lt2000-01-01"), Tuple.Create("address-city", "City"), Tuple.Create("address-state", "State") };

            // Query before adding an sproc to the database
            await _fixture.SearchService.SearchAsync(KnownResourceTypes.Patient, query, CancellationToken.None);

            var hash = _fixture.SqlQueryHashCalculator.MostRecentSqlHash;

            // assert an sproc was not used
            Assert.False(await CheckIfSprocUsed(hash));

            // add the sproc
            _output.WriteLine("Adding new sproc to database.");
            AddSproc(hash);

            // Query after adding an sproc to the database
            var sw = Stopwatch.StartNew();
            var sprocWasUsed = false;
            while (sw.Elapsed.TotalSeconds < 100) // previous single try after 1.1 sec delay was not reliable.
            {
                await Task.Delay(300);
                await _fixture.SearchService.SearchAsync(KnownResourceTypes.Patient, query, CancellationToken.None);
                Assert.Equal(hash, _fixture.SqlQueryHashCalculator.MostRecentSqlHash);
                if (await CheckIfSprocUsed(hash))
                {
                    sprocWasUsed = true;
                    break;
                }
            }

            Assert.Single(CustomQueries.QueryStore);

            // Check if stored procedure was used
            Assert.True(sprocWasUsed);

            // restore state before this test
            CustomQueries.WaitTime = 60;

            // drop stored procedure and clear cache, so no other tests use this stored procedure.
            _fixture.SqlHelper.ExecuteSqlCmd($"DROP PROCEDURE [dbo].[CustomQuery_{hash}]").Wait();
            CustomQueries.QueryStore.Clear();
        }

        private async Task<bool> CheckIfSprocUsed(string hash)
        {
            using var conn = await _fixture.SqlHelper.GetSqlConnectionAsync();
            _output.WriteLine("Checking database for sproc being run.");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT TOP 1 O.name
  FROM sys.dm_exec_procedure_stats S
       JOIN sys.objects O ON O.object_id = S.object_id
  WHERE O.type = 'p' AND O.name = 'CustomQuery_'+@hash
  ORDER BY
       S.last_execution_time DESC";
            cmd.Parameters.AddWithValue("@hash", hash);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                return true;
            }

            _output.WriteLine("No evidence found of sproc being run.");
            return false;
        }

        private void AddSproc(string hash)
        {
            _fixture.SqlHelper.ExecuteSqlCmd(@$"
CREATE OR ALTER PROCEDURE [dbo].[CustomQuery_{hash}]
   @p0 nvarchar(256)
  ,@p1 nvarchar(256)
  ,@p2 datetime2
  ,@p3 datetime2
  ,@p4 int
AS
set nocount on
SELECT DISTINCT r.ResourceTypeId, r.ResourceId, r.Version, r.IsDeleted, r.ResourceSurrogateId, r.RequestMethod, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial, r.IsRawResourceMetaSet, r.SearchParamHash, r.RawResource
  FROM dbo.Resource r
  WHERE 1 = 2
            ").Wait();
        }
    }
}
