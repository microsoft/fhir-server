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

        [Fact]
        public async Task GivenResourceVersionsWithOwnedVectors_WhenHardDeletingInsideAnAmbientTransaction_ThenDeletedOwnedVectorsAreRemoved()
        {
            string resourceId = Guid.NewGuid().ToString();
            string modelName = Guid.NewGuid().ToString();

            using (SqlConnectionWrapper connectionWrapper = await _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false))
            using (SqlCommandWrapper command = connectionWrapper.CreateRetrySqlCommand())
            {
                command.CommandText = @"
DECLARE @ResourceTypeId smallint = (SELECT TOP (1) ResourceTypeId FROM dbo.ResourceType ORDER BY ResourceTypeId)
DECLARE @SearchParamId smallint = (SELECT TOP (1) SearchParamId FROM dbo.SearchParam ORDER BY SearchParamId)
DECLARE @EmbeddingModelId smallint
DECLARE @BaseSurrogateId bigint = DATEDIFF_BIG(millisecond, CONVERT(datetime2, '0001-01-01'), SYSUTCDATETIME()) * CONVERT(bigint, 80000)
DECLARE @HistorySurrogateId bigint = @BaseSurrogateId + NEXT VALUE FOR dbo.ResourceSurrogateIdUniquifierSequence
DECLARE @CurrentSurrogateId bigint = @BaseSurrogateId + NEXT VALUE FOR dbo.ResourceSurrogateIdUniquifierSequence
DECLARE @InitialTranCount int = @@TRANCOUNT
DECLARE @HistoryDeleteTranCount int
DECLARE @DeleteTranCount int
DECLARE @ResourceCountAfterHistoryDelete int
DECLARE @VectorCountAfterHistoryDelete int
DECLARE @ResourceCount int
DECLARE @VectorCount int

BEGIN TRY
  BEGIN TRANSACTION

  INSERT INTO dbo.EmbeddingModel (ModelName, ModelVersion, Dimension)
  VALUES (@ModelName, 'test', 1536)
  SET @EmbeddingModelId = CONVERT(smallint, SCOPE_IDENTITY())

  INSERT INTO dbo.Resource
      (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash)
  VALUES
      (@ResourceTypeId, @ResourceId, 1, 1, @HistorySurrogateId, 0, 'PUT', 0x01, 0, NULL),
      (@ResourceTypeId, @ResourceId, 2, 0, @CurrentSurrogateId, 0, 'PUT', 0x01, 0, NULL)

  INSERT INTO dbo.VectorSearchParam
      (ResourceTypeId, ResourceSurrogateId, SearchParamId, ChunkOrdinal, EmbeddingModelId, ChunkText, SourceTextHash, SourceResourceTypeId, SourceResourceId, SourceResourceVersion, SourcePath, Embedding)
  VALUES
      (@ResourceTypeId, @HistorySurrogateId, @SearchParamId, 0, @EmbeddingModelId, N'history text', HASHBYTES('SHA2_256', N'history text'), @ResourceTypeId, @ResourceId, '1', N'content', CAST(CONCAT('[', REPLICATE('0,', 1535), '0]') AS vector(1536))),
      (@ResourceTypeId, @CurrentSurrogateId, @SearchParamId, 0, @EmbeddingModelId, N'current text', HASHBYTES('SHA2_256', N'current text'), @ResourceTypeId, @ResourceId, '2', N'content', CAST(CONCAT('[', REPLICATE('0,', 1535), '0]') AS vector(1536)))

  EXECUTE dbo.HardDeleteResource
      @ResourceTypeId = @ResourceTypeId,
      @ResourceId = @ResourceId,
      @KeepCurrentVersion = 1,
      @IsResourceChangeCaptureEnabled = 0

  SET @HistoryDeleteTranCount = @@TRANCOUNT
  SET @ResourceCountAfterHistoryDelete = (SELECT COUNT(*) FROM dbo.Resource WHERE ResourceTypeId = @ResourceTypeId AND ResourceId = @ResourceId)
  SET @VectorCountAfterHistoryDelete = (SELECT COUNT(*) FROM dbo.VectorSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (@HistorySurrogateId, @CurrentSurrogateId))

  EXECUTE dbo.HardDeleteResource
      @ResourceTypeId = @ResourceTypeId,
      @ResourceId = @ResourceId,
      @KeepCurrentVersion = 0,
      @IsResourceChangeCaptureEnabled = 0

  SET @DeleteTranCount = @@TRANCOUNT
  SET @ResourceCount = (SELECT COUNT(*) FROM dbo.Resource WHERE ResourceTypeId = @ResourceTypeId AND ResourceId = @ResourceId)
    SET @VectorCount = (SELECT COUNT(*) FROM dbo.VectorSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (@HistorySurrogateId, @CurrentSurrogateId))

  ROLLBACK TRANSACTION
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION
  THROW
END CATCH

SELECT @InitialTranCount,
    @HistoryDeleteTranCount,
       @DeleteTranCount,
    @ResourceCountAfterHistoryDelete,
    @VectorCountAfterHistoryDelete,
       @ResourceCount,
       @VectorCount";

                command.Parameters.Add(new SqlParameter { ParameterName = "ResourceId", Value = resourceId });
                command.Parameters.Add(new SqlParameter { ParameterName = "ModelName", Value = modelName });

                using (SqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None))
                {
                    Assert.True(await reader.ReadAsync(CancellationToken.None));
                    Assert.Equal(0, reader.GetInt32(0));
                    Assert.Equal(1, reader.GetInt32(1));
                    Assert.Equal(1, reader.GetInt32(2));
                    Assert.Equal(1, reader.GetInt32(3));
                    Assert.Equal(1, reader.GetInt32(4));
                    Assert.Equal(0, reader.GetInt32(5));
                    Assert.Equal(0, reader.GetInt32(6));
                }
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
