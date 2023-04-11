// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Xunit.Abstractions;
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

            // Create a query with a known hash
            var queryParameters = new[]
            {
                Tuple.Create("participating-organization.identifier", $"TAB%7C910000290"),
                Tuple.Create(SearchParameterNames.Include, $"OrganizationAffiliation:location"),
                Tuple.Create("participating-organization.type", $"practice"),
            };

            // Query before adding an sproc to the database
            await _fixture.SearchService.SearchAsync(KnownResourceTypes.OrganizationAffiliation, queryParameters, CancellationToken.None);

            // assert an sproc was not used
            Assert.False(await CheckIfSprocUsed());

            // add the sproc
            _output.WriteLine("Adding new sproc to database.");
            AddSproc();

            await Task.Delay(1100);

            // Query after adding an sproc to the database
            await _fixture.SearchService.SearchAsync(KnownResourceTypes.OrganizationAffiliation, queryParameters, CancellationToken.None);

            // assert an sproc was not used
            Assert.True(await CheckIfSprocUsed());
        }

        private async Task<bool> CheckIfSprocUsed()
        {
            using (SqlConnection conn = await _fixture.SqlHelper.GetSqlConnectionAsync())
            {
                _output.WriteLine("Checking database for sproc being run.");
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT SCHEMA_NAME(sysobject.schema_id), OBJECT_NAME(stats.object_id),  stats.last_execution_time\r\n" +
                    "FROM sys.dm_exec_procedure_stats stats\r\n" +
                    "INNER JOIN sys.objects sysobject ON sysobject.object_id = stats.object_id\r\n" +
                    "WHERE  sysobject.type = 'P'\r\n" +
                    "and (sysobject.object_id = object_id('dbo.CustomQuery_B007D1A9282416ACD224E136F2B6E65BC26867FAA5AAC6D62D7AE3E54CD376EB') \r\n" +
                    "OR sysobject.name = 'CustomQuery_B007D1A9282416ACD224E136F2B6E65BC26867FAA5AAC6D62D7AE3E54CD376EB')\r\n" +
                    "ORDER BY stats.last_execution_time DESC  ";
                var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var text = reader.GetString(1);
                    if (text.Contains("CustomQuery"))
                    {
                        return true;
                    }
                }
            }

            _output.WriteLine("No evidence found of sproc being run.");

            return false;
        }

        private void AddSproc()
        {
            _fixture.SqlHelper.ExecuteSqlCmd(
                "CREATE OR ALTER PROCEDURE [dbo].[CustomQuery_B007D1A9282416ACD224E136F2B6E65BC26867FAA5AAC6D62D7AE3E54CD376EB]\r\n" +
                "(@p0 SmallInt, @p1 SmallInt, @p2 SmallInt, @p3 SmallInt, @p4 NVarChar(256), @p5 SmallInt, @p6 VarChar(256), @p7 Int, @p8 SmallInt, @p9 Int)\r\n" +
                "AS\r\n" +
                "BEGIN\r\n" +
                "WITH cte0 AS\r\n(\r\n" +
                "SELECT refSource.ResourceTypeId AS T1, refSource.ResourceSurrogateId AS Sid1, refTarget.ResourceTypeId AS T2, refTarget.ResourceSurrogateId AS Sid2 \r\n" +
                "FROM dbo.ReferenceSearchParam refSource\r\n    INNER JOIN dbo.Resource refTarget\r\n    ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId\r\n" +
                "AND refSource.ReferenceResourceId = refTarget.ResourceId\r\n    WHERE refSource.SearchParamId = @p0\r\n        AND refTarget.IsHistory = 0\r\n" +
                "AND refSource.IsHistory = 0\r\n        AND refSource.ResourceTypeId IN (@p1)\r\n        AND refSource.ReferenceResourceTypeId IN (@p2)\r\n" +
                "AND refSource.ResourceTypeId = @p1\r\n),\r\ncte1 AS\r\n(\r\n    SELECT T1, Sid1, ResourceTypeId AS T2, \r\n    ResourceSurrogateId AS Sid2\r\n" +
                "FROM dbo.TokenSearchParam\r\n    INNER JOIN cte0\r\n    ON ResourceTypeId = T2\r\n        AND ResourceSurrogateId = Sid2\r\n    WHERE IsHistory = 0\r\n" +
                "AND SearchParamId = @p3\r\n        AND Code = @p4\r\n),\r\ncte2 AS\r\n(\r\n" +
                "SELECT refSource.ResourceTypeId AS T1, refSource.ResourceSurrogateId AS Sid1, refTarget.ResourceTypeId AS T2, refTarget.ResourceSurrogateId AS Sid2 \r\n" +
                "FROM dbo.ReferenceSearchParam refSource\r\n    INNER JOIN dbo.Resource refTarget\r\n    ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId\r\n" +
                "AND refSource.ReferenceResourceId = refTarget.ResourceId\r\n    WHERE refSource.SearchParamId = @p0\r\n        AND refTarget.IsHistory = 0\r\n" +
                "AND refSource.IsHistory = 0\r\n        AND refSource.ResourceTypeId IN (@p1)\r\n        AND refSource.ReferenceResourceTypeId IN (@p2)\r\n " +
                " AND EXISTS(SELECT * FROM cte1 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1)\r\n        AND refSource.ResourceTypeId = @p1\r\n" +
                "),\r\ncte3 AS\r\n(\r\n    SELECT T1, Sid1, ResourceTypeId AS T2, \r\n    ResourceSurrogateId AS Sid2\r\n    FROM dbo.TokenSearchParam\r\n    INNER JOIN cte2\r\n" +
                "ON ResourceTypeId = T2\r\n        AND ResourceSurrogateId = Sid2\r\n    WHERE IsHistory = 0\r\n        AND SearchParamId = @p5\r\n        AND Code = @p6\r\n),\r\ncte4 AS\r\n" +
                "(\r\n    SELECT ROW_NUMBER() OVER(ORDER BY T1 ASC, Sid1 ASC) AS Row, *\r\n    FROM\r\n    (\r\n        SELECT DISTINCT TOP (@p7) T1, Sid1, 1 AS IsMatch, 0 AS IsPartial \r\n" +
                "FROM cte3\r\n        ORDER BY T1 ASC, Sid1 ASC\r\n    ) t\r\n),\r\ncte5 AS\r\n(\r\n" +
                "SELECT DISTINCT refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch \r\n    FROM dbo.ReferenceSearchParam refSource\r\n" +
                "INNER JOIN dbo.Resource refTarget\r\n    ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId\r\n        AND refSource.ReferenceResourceId = refTarget.ResourceId\r\n" +
                "WHERE refSource.SearchParamId = @p8\r\n        AND refTarget.IsHistory = 0\r\n        AND refSource.IsHistory = 0\r\n        AND refTarget.IsDeleted = 0\r\n" +
                "AND refSource.ResourceTypeId IN (98)\r\n        AND EXISTS( SELECT * FROM cte4 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1 AND Row < @p9)\r\n" +
                "),\r\ncte6 AS\r\n(\r\n    SELECT DISTINCT T1, Sid1, IsMatch, 0 AS IsPartial \r\n    FROM cte5\r\n),\r\ncte7 AS\r\n(\r\n    SELECT T1, Sid1, IsMatch, IsPartial \r\n" +
                "FROM cte4\r\n    UNION ALL\r\n    SELECT T1, Sid1, IsMatch, IsPartial\r\n    FROM cte6 WHERE NOT EXISTS (SELECT * FROM cte4 WHERE cte4.Sid1 = cte6.Sid1 AND cte4.T1 = cte6.T1)\r\n" +
                ")\r\nSELECT DISTINCT r.ResourceTypeId, r.ResourceId, r.Version, r.IsDeleted, r.ResourceSurrogateId, r.RequestMethod, CAST(IsMatch AS bit) AS IsMatch, CAST(IsPartial AS bit) AS IsPartial, r.IsRawResourceMetaSet, r.SearchParamHash, r.RawResource\r\n" +
                "FROM dbo.Resource r\r\n\r\nINNER JOIN cte7\r\nON r.ResourceTypeId = cte7.T1 AND \r\nr.ResourceSurrogateId = cte7.Sid1\r\n" +
                "ORDER BY IsMatch DESC, r.ResourceTypeId ASC, r.ResourceSurrogateId ASC\r\n OPTION (OPTIMIZE FOR UNKNOWN)\r\n/* HASH yWa2X9xny4iE6Z6Qe/XNkbgqhUX+qwWjXLgRz9r7E0I= */" +
                "END");
        }
    }
}
