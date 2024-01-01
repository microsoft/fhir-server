// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel.Types;
using Hl7.Fhir.Rest;
using MathNet.Numerics;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Microsoft.SqlServer.Management.Sdk.Sfc;
using Xunit;
using Xunit.Abstractions;
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
    public class SqlCustomQueryTests : IClassFixture<FhirStorageTestsFixture>
    {
        private readonly FhirStorageTestsFixture _fixture;
        private readonly ITestOutputHelper _output;

        public SqlCustomQueryTests(FhirStorageTestsFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [SkippableFact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenASqlQuery_IfAStoredProcExistsWithMatchingHash_ThenStoredProcUsed()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4,
                "This test is only valid for R4");

            // set the wait time to 1 second
            CustomQueries.WaitTime = 1;

            // use more unique call to avoid racing
            var query = new[] { Tuple.Create("birthdate", "gt1800-01-01"), Tuple.Create("address-city", "City"), Tuple.Create("address-state", "State") };

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
            while (sw.Elapsed.TotalSeconds < 10) // previous single try after 1.1 sec delay was not reliable.
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
            using (SqlConnection conn = await _fixture.SqlHelper.GetSqlConnectionAsync())
            {
                _output.WriteLine("Checking database for sproc being run.");
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
SELECT TOP 1 O.name
  FROM sys.dm_exec_procedure_stats S
       JOIN sys.objects O ON O.object_id = S.object_id
  WHERE O.type = 'p' AND O.name = 'CustomQuery_'+@hash
  ORDER BY
       S.last_execution_time DESC";
                cmd.Parameters.AddWithValue("@hash", hash);

                var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    return true;
                }
            }

            _output.WriteLine("No evidence found of sproc being run.");

            return false;
        }

        private void AddSproc(string hash)
        {
            _fixture.SqlHelper.ExecuteSqlCmd(@$"
CREATE OR ALTER PROCEDURE[dbo].[CustomQuery_{hash}]
   @p0 smallint = 690
  ,@p1 datetime2 = '1800-01-01T23:59:59.9999999Z'
  ,@p2 smallint = 103
  ,@p3 smallint = 685
  ,@p4 nvarchar(256) = N'City%'
  ,@p5 smallint = 688
  ,@p6 nvarchar(256) = N'State%'
  ,@p7 smallint = 217
  ,@p8 smallint = 28
  ,@p9 smallint = 202
  ,@p10 nvarchar(256) = N'http://snomed.info/sct'
  ,@p11 varchar(256) = '444814009'
  ,@p12 int = 11
AS
set nocount on
DECLARE @FilteredData AS TABLE(T1 smallint, Sid1 bigint, IsMatch bit, IsPartial bit, Row int)
;WITH
cte0 AS
(
  SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
    FROM dbo.DateTimeSearchParam
    WHERE IsHistory = 0
      AND SearchParamId = @p0
      AND EndDateTime > @p1
      AND ResourceTypeId = @p2
)
,cte1 AS
(
  SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
    FROM dbo.StringSearchParam
         JOIN cte0 ON ResourceTypeId = T1 AND ResourceSurrogateId = Sid1
    WHERE IsHistory = 0
      AND SearchParamId = @p3
      AND Text LIKE @p4
      AND ResourceTypeId = @p2
)
,cte2 AS
(
  SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
    FROM dbo.StringSearchParam
         JOIN cte1 ON ResourceTypeId = T1 AND ResourceSurrogateId = Sid1
    WHERE IsHistory = 0
      AND SearchParamId = @p5
      AND Text LIKE @p6
      AND ResourceTypeId = @p2
)
,cte3 AS
(
  SELECT refSource.ResourceTypeId AS T2, refSource.ResourceSurrogateId AS Sid2, refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1
    FROM dbo.ReferenceSearchParam refSource
         JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId
         JOIN cte2 ON refTarget.ResourceTypeId = T1 AND refTarget.ResourceSurrogateId = Sid1
    WHERE refSource.SearchParamId = @p7
      AND refTarget.IsHistory = 0
      AND refSource.IsHistory = 0
      AND refSource.ResourceTypeId IN (@p8)
      AND refSource.ReferenceResourceTypeId IN (@p2)
      AND refTarget.ResourceTypeId = @p2
)
,cte4 AS
(
  SELECT T1, Sid1, ResourceTypeId AS T2, ResourceSurrogateId AS Sid2
    FROM dbo.TokenSearchParam
         JOIN cte3 ON ResourceTypeId = T2 AND ResourceSurrogateId = Sid2
    WHERE IsHistory = 0
      AND SearchParamId = @p9
      AND SystemId IN (SELECT SystemId FROM dbo.System WHERE Value = @p10)
      AND Code = @p11 
)
,cte5 AS
(
  SELECT DISTINCT TOP(@p12) T1, Sid1, 1 AS IsMatch, 0 AS IsPartial
    FROM cte4
    ORDER BY T1 ASC, Sid1 ASC
)
/* HASH mjWyjUhfaQSwdGxzuqf2zIoL40RwSu3TGuwOHSjeC98= */
SELECT DISTINCT r.ResourceTypeId, r.ResourceId, r.Version, r.IsDeleted, r.ResourceSurrogateId, r.RequestMethod, CAST(IsMatch AS bit) AS IsMatch, CAST(IsPartial AS bit) AS IsPartial, r.IsRawResourceMetaSet, r.SearchParamHash, r.RawResource
  FROM dbo.Resource r
       JOIN cte5 ON r.ResourceTypeId = cte5.T1 AND r.ResourceSurrogateId = cte5.Sid1
  WHERE IsHistory = 0
  ORDER BY r.ResourceTypeId ASC, r.ResourceSurrogateId ASC
            ").Wait();
        }
    }
}
