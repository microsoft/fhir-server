// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlServerMemberMatchTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private readonly string _connectionString;

        public SqlServerMemberMatchTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _connectionString = fixture.TestConnectionString;
        }

        /// <summary>
        /// With this test it's possible for it to succeed but if it fails then it should be
        /// a specific error. Since this error (#8623) is failure for sql to create a query plan,
        /// when it fails it never returns data. If it doesn't hit the error then we don't care
        /// if it returns data.
        /// </summary>
        [Fact]
        public void GivenAComplexSqlStatement_WhenExecutingItMayThrowSpecificException()
        {
            string sql = Samples.GetFileContents("sql_8623_script", "sql");
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                SqlCommand sqlCommand = connection.CreateCommand();
                sqlCommand.CommandText = sql;

                bool pass;

                try
                {
                    int response = sqlCommand.ExecuteNonQuery();
                    pass = response >= -1;
                }
                catch (SqlException ex)
                {
                    pass = ex.Number == SqlErrorCodes.QueryProcessorNoQueryPlan;
                    Assert.True(ex.Number == SqlErrorCodes.QueryProcessorNoQueryPlan);
                }

                if (!pass)
                {
                    Assert.Fail($"{nameof(GivenAComplexSqlStatement_WhenExecutingItMayThrowSpecificException)} failed ");
                }
            }
        }
    }
}
