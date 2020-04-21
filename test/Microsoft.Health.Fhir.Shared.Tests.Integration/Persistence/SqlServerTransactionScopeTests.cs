// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.SqlServer.Features.Client;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [CollectionDefinition("SqlTransactionScopeTests", DisableParallelization = true)]
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    public class SqlServerTransactionScopeTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private readonly string _connectionString;
        private SqlServerFhirStorageTestsFixture _fixture;

        public SqlServerTransactionScopeTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _connectionString = fixture.TestConnectionString;
            _fixture = fixture;
        }

        [Fact]
        public async Task GivenATransactionScope_WhenReading_TheUncommittedValuesShouldOnlyBeAvailableWithTheTransactionAndWithHints()
        {
            var newId = Guid.NewGuid().ToString();

            using (var transactionScope = _fixture.SqlTransactionHandler.BeginTransaction())
            {
                using (SqlConnectionWrapper connectionWrapperWithTransaction = _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
                {
                    using (SqlCommand command = connectionWrapperWithTransaction.CreateSqlCommand())
                    {
                        command.CommandText = @"
                            INSERT INTO Resource
                            VALUES(97, @newId, 1, 0, 5095719085917680000, 0, null, CAST('test' AS VARBINARY(MAX)))";

                        command.Parameters.Add(new SqlParameter { ParameterName = "newId", Value = newId });

                        command.ExecuteNonQuery();
                    }
                }

                // Within the same transaction, the resource should be found
                using (SqlConnectionWrapper connectionWrapperWithTransaction = _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
                {
                    await VerifyCommandResults(connectionWrapperWithTransaction, newId, true);
                }

                // Outside of the transaction, the resource should not be found
                using (SqlConnectionWrapper connectionWrapperWithTransaction = _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(false))
                {
                    await VerifyCommandResults(connectionWrapperWithTransaction, newId, false);
                }

                // Outside of the transaction, but with the readuncommitted hint, the resource should be found.
                using (SqlConnectionWrapper connectionWrapperWithTransaction = _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(false))
                {
                    await VerifyCommandResults(connectionWrapperWithTransaction, newId, true, "WITH (READUNCOMMITTED)");
                }
            }

            // Outside of the transactionscope, the resource should not be found
            using (SqlConnectionWrapper connectionWrapperWithTransaction = _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(false))
            {
                await VerifyCommandResults(connectionWrapperWithTransaction, newId, false);
            }

            // Outside of the transactionscope, but with the readuncommitted hint, the resource should not be found
            using (SqlConnectionWrapper connectionWrapperWithTransaction = _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(false))
            {
                await VerifyCommandResults(connectionWrapperWithTransaction, newId, false, "WITH (READUNCOMMITTED)");
            }
        }

        [Fact]
        public async Task GivenATransactionScope_WhenReadingAfterComplete_TheValuesShouldBeAvailable()
        {
            var newId = Guid.NewGuid().ToString();

            using (var transactionScope = _fixture.SqlTransactionHandler.BeginTransaction())
            {
                using (SqlConnectionWrapper connectionWrapperWithTransaction = _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
                {
                    using (SqlCommand command = connectionWrapperWithTransaction.CreateSqlCommand())
                    {
                        command.CommandText = @"
                            INSERT INTO Resource
                            VALUES(97, @newId, 1, 0, 5095719085917680001, 0, null, CAST('test' AS VARBINARY(MAX)))";

                        command.Parameters.Add(new SqlParameter { ParameterName = "newId", Value = newId });

                        command.ExecuteNonQuery();
                    }
                }

                transactionScope.Complete();
            }

            // Outside of the transactionscope, the resource should not be found
            using (SqlConnectionWrapper connectionWrapperWithTransaction = _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(false))
            {
                await VerifyCommandResults(connectionWrapperWithTransaction, newId, true);
            }
        }

        private static async Task VerifyCommandResults(SqlConnectionWrapper connectionWrapper, string newId, bool shouldFind, string tableHints = "")
        {
            using (SqlCommand command = connectionWrapper.CreateSqlCommand())
            {
                command.CommandText = $@"
                            SELECT * 
                            FROM resource {tableHints}
                            WHERE ResourceId = @newId";

                command.Parameters.Add(new SqlParameter { ParameterName = "newId", Value = newId });

                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (shouldFind)
                    {
                        while (reader.Read())
                        {
                            Assert.Equal(newId, reader["resourceId"]);
                        }
                    }
                    else
                    {
                        Assert.False(reader.HasRows);
                    }
                }
            }
        }
    }
}
