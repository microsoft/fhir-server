// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Verifies the model-aware keys shared by vector storage and transport.
    /// </summary>
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Schema)]
    public class SqlServerVectorStorageTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private readonly SqlServerFhirStorageTestsFixture _fixture;

        /// <summary>
        /// Initializes tests using an isolated SQL storage fixture.
        /// </summary>
        /// <param name="fixture">The SQL storage fixture.</param>
        public SqlServerVectorStorageTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
        }

        /// <summary>
        /// Verifies model identity participates in uniqueness without including payload columns.
        /// </summary>
        /// <param name="tableValuedParameter">Whether to exercise the TVP rather than the persisted table.</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenVectorKeys_WhenOnlyTheModelDiffers_ThenBothRowsAreStoredAndDuplicateFullKeysAreRejected(bool tableValuedParameter)
        {
            using SqlConnectionWrapper connection = await _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false);
            using SqlCommandWrapper command = connection.CreateRetrySqlCommand();
            string target = tableValuedParameter ? "@Vectors" : "dbo.VectorSearchParam";
            command.CommandText = $"""
                SET XACT_ABORT OFF;
                DECLARE @Vectors dbo.VectorSearchParamList;
                DECLARE @Embedding nvarchar(max) = CONCAT('[', REPLICATE('0,', 1535), '1]');
                DECLARE @Model1 smallint, @Model2 smallint, @DuplicateError int;
                DECLARE @ResourceTypeId smallint = (SELECT TOP (1) ResourceTypeId FROM dbo.ResourceType);
                DECLARE @SearchParamId smallint = (SELECT TOP (1) SearchParamId FROM dbo.SearchParam);
                DECLARE @ResourceSurrogateId bigint = NEXT VALUE FOR dbo.ResourceSurrogateIdUniquifierSequence;

                BEGIN TRANSACTION;
                BEGIN TRY
                    INSERT INTO dbo.EmbeddingModel (ModelName, ModelVersion, Dimension)
                    VALUES (CONVERT(varchar(36), NEWID()), '1', 1536);
                    SET @Model1 = CONVERT(smallint, SCOPE_IDENTITY());
                    INSERT INTO dbo.EmbeddingModel (ModelName, ModelVersion, Dimension)
                    VALUES (CONVERT(varchar(36), NEWID()), '1', 1536);
                    SET @Model2 = CONVERT(smallint, SCOPE_IDENTITY());

                    INSERT INTO {target}
                        (ResourceTypeId, ResourceSurrogateId, SearchParamId, ChunkOrdinal, EmbeddingModelId, SourceTextHash, SourceTextCompressed, Embedding)
                    VALUES
                        (@ResourceTypeId, @ResourceSurrogateId, @SearchParamId, 0, @Model1, HASHBYTES('SHA2_256', 'first'), 0x01, @Embedding),
                        (@ResourceTypeId, @ResourceSurrogateId, @SearchParamId, 0, @Model2, HASHBYTES('SHA2_256', 'second'), 0x02, @Embedding);

                    BEGIN TRY
                        INSERT INTO {target}
                            (ResourceTypeId, ResourceSurrogateId, SearchParamId, ChunkOrdinal, EmbeddingModelId, SourceTextHash, SourceTextCompressed, Embedding)
                        VALUES
                            (@ResourceTypeId, @ResourceSurrogateId, @SearchParamId, 0, @Model1, HASHBYTES('SHA2_256', 'different payload'), 0x03, @Embedding);
                    END TRY
                    BEGIN CATCH
                        SET @DuplicateError = ERROR_NUMBER();
                    END CATCH;

                    SELECT COUNT(*), COUNT(DISTINCT EmbeddingModelId), @DuplicateError
                    FROM {target}
                    WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId = @ResourceSurrogateId;

                    ROLLBACK TRANSACTION;
                END TRY
                BEGIN CATCH
                    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
                    THROW;
                END CATCH;

                SELECT C.name
                FROM sys.indexes I
                JOIN sys.index_columns IC ON IC.object_id = I.object_id AND IC.index_id = I.index_id
                JOIN sys.columns C ON C.object_id = IC.object_id AND C.column_id = IC.column_id
                WHERE I.object_id = {(tableValuedParameter ? "(SELECT type_table_object_id FROM sys.table_types WHERE name = 'VectorSearchParamList' AND schema_id = SCHEMA_ID('dbo'))" : "OBJECT_ID('dbo.VectorSearchParam')")}
                  AND I.is_unique = 1
                  AND IC.key_ordinal > 0
                ORDER BY IC.key_ordinal;
                """;

            using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
            Assert.True(await reader.ReadAsync());
            Assert.Equal(2, reader.GetInt32(0));
            Assert.Equal(2, reader.GetInt32(1));
            Assert.Equal(2627, reader.GetInt32(2));
            Assert.False(await reader.ReadAsync());

            Assert.True(await reader.NextResultAsync());
            foreach (string key in new[] { "ResourceTypeId", "ResourceSurrogateId", "SearchParamId", "EmbeddingModelId", "ChunkOrdinal" })
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(key, reader.GetString(0));
            }

            Assert.False(await reader.ReadAsync());
        }
    }
}
