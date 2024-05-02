// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
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
            var searchParamHash = new string("RandomSearchParam").ComputeHash();

            using (var transactionScope = _fixture.SqlTransactionHandler.BeginTransaction())
            {
                using (SqlConnectionWrapper connectionWrapperWithTransaction = await _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, true))
                using (SqlCommandWrapper sqlCommandWrapper = connectionWrapperWithTransaction.CreateRetrySqlCommand())
                {
                    sqlCommandWrapper.CommandText = @"
                        INSERT INTO Resource
                          (ResourceTypeId,ResourceId,Version,IsHistory,ResourceSurrogateId,IsDeleted,RequestMethod,RawResource,IsRawResourceMetaSet,SearchParamHash)
                        VALUES(97, @newId, 1, 0, 5095719085917680000, 0, null, CAST('test' AS VARBINARY(MAX)), 0, @searchParamHash)";

                    sqlCommandWrapper.Parameters.Add(new SqlParameter { ParameterName = "newId", Value = newId });
                    sqlCommandWrapper.Parameters.Add(new SqlParameter { ParameterName = "searchParamHash", Value = searchParamHash });

                    await sqlCommandWrapper.ExecuteNonQueryAsync(CancellationToken.None);
                }

                // Within the same transaction, the resource should be found
                using (SqlConnectionWrapper connectionWrapperWithTransaction = await _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, true))
                {
                    await VerifyCommandResults(connectionWrapperWithTransaction, newId, true);
                }

                // Outside of the transaction, the resource should not be found
                using (SqlConnectionWrapper connectionWrapperWithTransaction = await _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false))
                {
                    await VerifyCommandResults(connectionWrapperWithTransaction, newId, false);
                }

                // Outside of the transaction, but with the readuncommitted hint, the resource should be found.
                using (SqlConnectionWrapper connectionWrapperWithTransaction = await _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false))
                {
                    await VerifyCommandResults(connectionWrapperWithTransaction, newId, true, "WITH (READUNCOMMITTED)");
                }
            }

            // Outside of the transactionscope, the resource should not be found
            using (SqlConnectionWrapper connectionWrapperWithTransaction = await _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false))
            {
                await VerifyCommandResults(connectionWrapperWithTransaction, newId, false);
            }

            // Outside of the transactionscope, but with the readuncommitted hint, the resource should not be found
            using (SqlConnectionWrapper connectionWrapperWithTransaction = await _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false))
            {
                await VerifyCommandResults(connectionWrapperWithTransaction, newId, false, "WITH (READUNCOMMITTED)");
            }
        }

        [Fact]
        public async Task GivenATransactionScope_WhenReadingAfterComplete_TheValuesShouldBeAvailable()
        {
            var newId = Guid.NewGuid().ToString();
            var searchParamHash = new string("RandomSearchParam").ComputeHash();

            using (var transactionScope = _fixture.SqlTransactionHandler.BeginTransaction())
            {
                using (SqlConnectionWrapper connectionWrapperWithTransaction = await _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, true))
                using (SqlCommandWrapper sqlCommandWrapper = connectionWrapperWithTransaction.CreateRetrySqlCommand())
                {
                    sqlCommandWrapper.CommandText = @"
                        INSERT INTO Resource
                          (ResourceTypeId,ResourceId,Version,IsHistory,ResourceSurrogateId,IsDeleted,RequestMethod,RawResource,IsRawResourceMetaSet,SearchParamHash)
                        VALUES(97, @newId, 1, 0, 5095719085917680001, 0, null, CAST('test' AS VARBINARY(MAX)), 0, @searchParamHash)";

                    sqlCommandWrapper.Parameters.Add(new SqlParameter { ParameterName = "newId", Value = newId });
                    sqlCommandWrapper.Parameters.Add(new SqlParameter { ParameterName = "searchParamHash", Value = searchParamHash });

                    await sqlCommandWrapper.ExecuteNonQueryAsync(CancellationToken.None);
                }

                transactionScope.Complete();
            }

            // Outside of the transaction scope, the resource should not be found
            using (SqlConnectionWrapper connectionWrapperWithTransaction = await _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false))
            {
                await VerifyCommandResults(connectionWrapperWithTransaction, newId, true);
            }
        }

        private static async Task VerifyCommandResults(SqlConnectionWrapper connectionWrapper, string newId, bool shouldFind, string tableHints = "")
        {
            using (SqlCommandWrapper sqlCommandWrapper = connectionWrapper.CreateRetrySqlCommand())
            {
                sqlCommandWrapper.CommandText = $@"
                            SELECT * 
                            FROM resource {tableHints}
                            WHERE ResourceId = @newId";

                sqlCommandWrapper.Parameters.Add(new SqlParameter { ParameterName = "newId", Value = newId });

                using (var reader = await sqlCommandWrapper.ExecuteReaderAsync(CancellationToken.None))
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
