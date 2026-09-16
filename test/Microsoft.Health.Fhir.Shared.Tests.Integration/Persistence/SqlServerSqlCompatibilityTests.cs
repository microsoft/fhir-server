// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Medino;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerSqlCompatibilityTests
    {
        private const string OriginalSearchParamHash = "original-hash";
        private const string NewSearchParamHash = "new-hash";
        private static readonly byte[] OriginalCompressedText = Encoding.UTF8.GetBytes("compressed-original");
        private static readonly byte[] ReplacementCompressedText = Encoding.UTF8.GetBytes("compressed-replacement");
        private static readonly string Embedding = $"[{string.Join(",", Enumerable.Repeat("0", 1536))}]";

        private enum VectorReindexScenario
        {
            Omitted,
            EvaluatedEmpty,
            EvaluatedWithVector,
            VectorInputVersionMismatch,
            VectorResourceAbsentFromOrdinaryInput,
            DuplicateVector,
            InvalidVectorDimensions,
        }

        /// <summary>
        /// A basic smoke test verifying that the code is compatible with schema versions
        /// all the way back to <see cref="SchemaVersionConstants.Min"/>. Ensures that the code can
        /// insert a resource using every schema version, and that we can read resources
        /// that were inserted into with earlier schemas.
        /// </summary>
        [Fact]
        public async Task GivenADatabaseWithAnEarlierSupportedSchemaAndUpgraded_WhenUpsertingAfter_OperationSucceeds()
        {
            string databaseName = SqlServerFhirStorageTestsFixture.GetDatabaseName("Compatibility");
            var insertedElements = new List<string>();

            FhirStorageTestsFixture fhirStorageTestsFixture = null;
            try
            {
                for (int i = Math.Max(SchemaVersionConstants.Min, SchemaVersionConstants.Max - 5); i <= SchemaVersionConstants.Max; i++)
                {
                    try
                    {
                        fhirStorageTestsFixture = new FhirStorageTestsFixture(new SqlServerFhirStorageTestsFixture(i, databaseName));
                        await fhirStorageTestsFixture.InitializeAsync(); // this will either create the database or upgrade the schema.

                        Mediator mediator = fhirStorageTestsFixture.Mediator;

                        foreach (string id in insertedElements)
                        {
                            // verify that we can read entries from previous versions
                            var readResult = (await mediator.GetResourceAsync(new ResourceKey("Observation", id))).ToResourceElement(fhirStorageTestsFixture.Deserializer);
                            Assert.Equal(id, readResult.Id);
                        }

                        // add a new entry
                        var saveResult = await mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
                        var deserialized = saveResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);
                        var result = (await mediator.GetResourceAsync(new ResourceKey(deserialized.InstanceType, deserialized.Id, deserialized.VersionId))).ToResourceElement(fhirStorageTestsFixture.Deserializer);

                        Assert.NotNull(result);
                        Assert.Equal(deserialized.Id, result.Id);
                        insertedElements.Add(result.Id);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"Failure using schema version {i}", e);
                    }
                }
            }
            finally
            {
                if (fhirStorageTestsFixture != null)
                {
                    await fhirStorageTestsFixture.DisposeAsync();
                }
            }
        }

        [Fact]
        public async Task GivenCurrentSchema_WhenPre117MergeResourcesCallerOmitsNewParameters_ResourceWriteSucceeds()
        {
            await VerifyPre117ResourceWriteAsync("dbo.MergeResources");
        }

        [Fact]
        public async Task GivenCurrentSchema_WhenPre117MergeResourcesAndSearchParamsCallerOmitsNewParameters_ResourceWriteSucceeds()
        {
            await VerifyPre117ResourceWriteAsync("dbo.MergeResourcesAndSearchParams");
        }

        [Fact]
        public async Task GivenCurrentSchema_WhenOldReindexCallerOmitsVectorParameters_StoredVectorsArePreserved()
        {
            await VerifyVectorReindexAsync(VectorReindexScenario.Omitted, expectedFailedResources: 0, expectedHash: NewSearchParamHash, expectedCompressedText: OriginalCompressedText);
        }

        [Fact]
        public async Task GivenCurrentSchema_WhenEvaluatedResourceHasNoVectors_StoredVectorsAreRemoved()
        {
            await VerifyVectorReindexAsync(VectorReindexScenario.EvaluatedEmpty, expectedFailedResources: 0, expectedHash: NewSearchParamHash, expectedCompressedText: null);
        }

        [Fact]
        public async Task GivenCurrentSchema_WhenEvaluatedResourceHasCompressedVector_StoredVectorIsReplaced()
        {
            await VerifyVectorReindexAsync(VectorReindexScenario.EvaluatedWithVector, expectedFailedResources: 0, expectedHash: NewSearchParamHash, expectedCompressedText: ReplacementCompressedText);
        }

        [Fact]
        public async Task GivenCurrentSchema_WhenVectorInputTargetsANonCurrentVersion_StoredVectorsArePreserved()
        {
            await VerifyVectorReindexAsync(VectorReindexScenario.VectorInputVersionMismatch, expectedFailedResources: 0, expectedHash: NewSearchParamHash, expectedCompressedText: OriginalCompressedText);
        }

        [Fact]
        public async Task GivenCurrentSchema_WhenVectorResourceIsAbsentFromOrdinaryInput_ResourceAndVectorsArePreserved()
        {
            await VerifyVectorReindexAsync(VectorReindexScenario.VectorResourceAbsentFromOrdinaryInput, expectedFailedResources: 0, expectedHash: OriginalSearchParamHash, expectedCompressedText: OriginalCompressedText);
        }

        [Fact]
        public async Task GivenCurrentSchema_WhenVectorInputContainsDuplicateKeys_ResourceAndVectorsArePreserved()
        {
            await VerifyVectorReindexAsync(VectorReindexScenario.DuplicateVector, expectedFailedResources: null, expectedHash: OriginalSearchParamHash, expectedCompressedText: OriginalCompressedText);
        }

        [Fact]
        public async Task GivenCurrentSchema_WhenVectorReplacementFails_OrdinaryAndVectorChangesAreRolledBack()
        {
            await VerifyVectorReindexAsync(VectorReindexScenario.InvalidVectorDimensions, expectedFailedResources: null, expectedHash: OriginalSearchParamHash, expectedCompressedText: OriginalCompressedText);
        }

        [Fact]
        public async Task GivenCurrentSchema_WhenResourceIsHardDeleted_OwnedVectorsAreDeleted()
        {
            string databaseName = SqlServerFhirStorageTestsFixture.GetDatabaseName("VectorHardDelete");
            var fixture = new FhirStorageTestsFixture(new SqlServerFhirStorageTestsFixture(SchemaVersionConstants.Max, databaseName));
            string resourceId = Guid.NewGuid().ToString();
            long resourceSurrogateId = DateTimeOffset.UtcNow.Ticks << 3;

            try
            {
                await fixture.InitializeAsync();
                await CreateResourceWithVectorAsync(fixture, resourceId, resourceSurrogateId);
                await using var connection = await fixture.SqlHelper.GetSqlConnectionAsync();
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    DECLARE @ResourceTypeId smallint = (SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Observation');

                    EXECUTE dbo.HardDeleteResource
                        @ResourceTypeId = @ResourceTypeId,
                        @ResourceId = @ResourceId,
                        @KeepCurrentVersion = 0,
                        @IsResourceChangeCaptureEnabled = 0;

                    SELECT COUNT(*)
                    FROM dbo.VectorSearchParam
                    WHERE ResourceTypeId = @ResourceTypeId
                        AND ResourceSurrogateId = @ResourceSurrogateId;
                    """;
                command.Parameters.Add("@ResourceId", SqlDbType.VarChar, 64).Value = resourceId;
                command.Parameters.Add("@ResourceSurrogateId", SqlDbType.BigInt).Value = resourceSurrogateId;

                Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
            }
            finally
            {
                await fixture.DisposeAsync();
            }
        }

        /// <summary>
        /// A basic smoke test verifying that the code is compatible with schema versions
        /// all the way back to <see cref="SchemaVersionConstants.Min"/>.
        /// </summary>
        [Fact]
        public async Task GivenADatabaseWithAnEarlierSupportedSchema_WhenUpserting_OperationSucceeds()
        {
            // List<FhirStorageTestsFixture> fhirStorageTestsFixtures = new();
            var versions = Enum.GetValues(typeof(SchemaVersion)).OfType<object>().ToList().Select(x => Convert.ToInt32(x)).ToList();
            await Parallel.ForEachAsync(versions, new ParallelOptions { MaxDegreeOfParallelism = 2}, async (version, cancel) =>
            {
                if (version >= Math.Max(SchemaVersionConstants.Min, SchemaVersionConstants.Max - 5) && version <= SchemaVersionConstants.Max)
                {
                    string databaseName = SqlServerFhirStorageTestsFixture.GetDatabaseName($"Compatibility_V{version}");
                    FhirStorageTestsFixture fhirStorageTestsFixture = null;
                    try
                    {
                        fhirStorageTestsFixture = new FhirStorageTestsFixture(new SqlServerFhirStorageTestsFixture(version, databaseName));
                        await fhirStorageTestsFixture.InitializeAsync();

                        Mediator mediator = fhirStorageTestsFixture.Mediator;

                        var saveResult = await mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
                        var deserialized = saveResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);
                        var result = (await mediator.GetResourceAsync(new ResourceKey(deserialized.InstanceType, deserialized.Id, deserialized.VersionId))).ToResourceElement(fhirStorageTestsFixture.Deserializer);

                        Assert.NotNull(result);
                        Assert.Equal(deserialized.Id, result.Id);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"Failure using schema version {version}", e);
                    }
                    finally
                    {
                        if (fhirStorageTestsFixture != null)
                        {
                            await fhirStorageTestsFixture.DisposeAsync();
                        }
                    }
                }
            });
        }

        /// <summary>
        /// With <see cref="SchemaVersionConstants.AddMinMaxForDateAndStringSearchParamVersion"/> we are
        /// changing the way _sort works. There might be a situation where code has been upgraded but schema
        /// version has not been upgraded. This test does a sanity check to make sure "old" _sort
        /// still works in such a scenario.
        /// </summary>
        [SkippableFact]
        public async Task GivenADatabaseWithAnEarlierSupportedSchema_WhenSearchingWithSort_SearchIsSuccessful()
        {
            Skip.If(SchemaVersionConstants.AddMinMaxForDateAndStringSearchParamVersion < SchemaVersionConstants.Min, "Schema version required for this test is not supported");

            string databaseName = SqlServerFhirStorageTestsFixture.GetDatabaseName($"Compatibility_Sort");
            int schemaVersion = SchemaVersionConstants.AddMinMaxForDateAndStringSearchParamVersion - 1;
            var fhirStorageTestsFixture = new FhirStorageTestsFixture(new SqlServerFhirStorageTestsFixture(
                schemaVersion,
                databaseName));

            try
            {
                await fhirStorageTestsFixture.InitializeAsync();

                Mediator mediator = fhirStorageTestsFixture.Mediator;

                var saveResult = await mediator.UpsertResourceAsync(Samples.GetDefaultPatient());
                var deserialized = saveResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);

                List<Tuple<string, string>> queries = new List<Tuple<string, string>>();
                queries.Add(new Tuple<string, string>("_sort", "birthdate"));

                Core.Models.ResourceElement searchResult = await mediator.SearchResourceAsync("Patient", queries);
                var bundle = searchResult.ResourceInstance as Bundle;

                Assert.NotNull(searchResult);
                Assert.Single(bundle.Entry);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"Sort query failed for schema version {schemaVersion}", e);
            }
            finally
            {
                fhirStorageTestsFixture.Dispose();
            }
        }

        private static async Task VerifyPre117ResourceWriteAsync(string procedureName)
        {
            string databaseName = SqlServerFhirStorageTestsFixture.GetDatabaseName("OldCallerCurrentSchema");
            var fixture = new FhirStorageTestsFixture(new SqlServerFhirStorageTestsFixture(SchemaVersionConstants.Max, databaseName));
            string resourceId = Guid.NewGuid().ToString();

            try
            {
                await fixture.InitializeAsync();
                await using var connection = await fixture.SqlHelper.GetSqlConnectionAsync();
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();

                string searchParamsArgument = procedureName == "dbo.MergeResourcesAndSearchParams"
                    ? "@SearchParams = @SearchParams,"
                    : string.Empty;

                command.CommandText = $"""
                    IF NOT EXISTS
                    (
                        SELECT 1
                        FROM sys.parameters
                        WHERE object_id = OBJECT_ID('{procedureName}')
                            AND name = '@VectorSearchParams'
                    )
                        THROW 50000, 'The current procedure does not contain the vector TVP under test.', 1;

                    DECLARE @SearchParams dbo.SearchParamList;
                    DECLARE @Resources dbo.ResourceList;
                    DECLARE @ResourceWriteClaims dbo.ResourceWriteClaimList;
                    DECLARE @ReferenceSearchParams dbo.ReferenceSearchParamList;
                    DECLARE @TokenSearchParams dbo.TokenSearchParamList;
                    DECLARE @TokenTexts dbo.TokenTextList;
                    DECLARE @StringSearchParams dbo.StringSearchParamList;
                    DECLARE @UriSearchParams dbo.UriSearchParamList;
                    DECLARE @NumberSearchParams dbo.NumberSearchParamList;
                    DECLARE @QuantitySearchParams dbo.QuantitySearchParamList;
                    DECLARE @DateTimeSearchParms dbo.DateTimeSearchParamList;
                    DECLARE @ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList;
                    DECLARE @TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList;
                    DECLARE @TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList;
                    DECLARE @TokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamList;
                    DECLARE @TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList;
                    DECLARE @TokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamList;

                    INSERT INTO @Resources
                    (
                        ResourceTypeId,
                        ResourceSurrogateId,
                        ResourceId,
                        Version,
                        HasVersionToCompare,
                        IsDeleted,
                        IsHistory,
                        KeepHistory,
                        RawResource,
                        IsRawResourceMetaSet,
                        RequestMethod,
                        SearchParamHash
                    )
                    SELECT ResourceTypeId, @ResourceSurrogateId, @ResourceId, 1, 1, 0, 0, 1, @RawResource, 1, 'PUT', NULL
                    FROM dbo.ResourceType
                    WHERE Name = 'Observation';

                    -- This is the complete pre-117 named-argument contract. The schema-117
                    -- vector TVP is intentionally omitted.
                    EXECUTE {procedureName}
                        {searchParamsArgument}
                        @Resources = @Resources,
                        @ResourceWriteClaims = @ResourceWriteClaims,
                        @ReferenceSearchParams = @ReferenceSearchParams,
                        @TokenSearchParams = @TokenSearchParams,
                        @TokenTexts = @TokenTexts,
                        @StringSearchParams = @StringSearchParams,
                        @UriSearchParams = @UriSearchParams,
                        @NumberSearchParams = @NumberSearchParams,
                        @QuantitySearchParams = @QuantitySearchParams,
                        @DateTimeSearchParms = @DateTimeSearchParms,
                        @ReferenceTokenCompositeSearchParams = @ReferenceTokenCompositeSearchParams,
                        @TokenTokenCompositeSearchParams = @TokenTokenCompositeSearchParams,
                        @TokenDateTimeCompositeSearchParams = @TokenDateTimeCompositeSearchParams,
                        @TokenQuantityCompositeSearchParams = @TokenQuantityCompositeSearchParams,
                        @TokenStringCompositeSearchParams = @TokenStringCompositeSearchParams,
                        @TokenNumberNumberCompositeSearchParams = @TokenNumberNumberCompositeSearchParams;

                    SELECT COUNT(*)
                    FROM dbo.Resource
                    WHERE ResourceId = @ResourceId
                        AND Version = 1
                        AND IsHistory = 0
                        AND IsDeleted = 0
                        AND RawResource = @RawResource
                        AND NOT EXISTS
                        (
                            SELECT 1
                            FROM dbo.VectorSearchParam
                            WHERE ResourceTypeId = dbo.Resource.ResourceTypeId
                                AND ResourceSurrogateId = dbo.Resource.ResourceSurrogateId
                        );
                    """;

                command.Parameters.Add("@ResourceSurrogateId", SqlDbType.BigInt).Value = DateTimeOffset.UtcNow.Ticks << 3;
                command.Parameters.Add("@ResourceId", SqlDbType.VarChar, 64).Value = resourceId;
                command.Parameters.Add("@RawResource", SqlDbType.VarBinary, -1).Value =
                    Encoding.UTF8.GetBytes($"{{\"resourceType\":\"Observation\",\"id\":\"{resourceId}\",\"status\":\"final\"}}");

                Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
            }
            finally
            {
                await fixture.DisposeAsync();
            }
        }

        private static async Task VerifyVectorReindexAsync(
            VectorReindexScenario scenario,
            int? expectedFailedResources,
            string expectedHash,
            byte[] expectedCompressedText)
        {
            string databaseName = SqlServerFhirStorageTestsFixture.GetDatabaseName($"VectorReindex_{scenario}");
            var fixture = new FhirStorageTestsFixture(new SqlServerFhirStorageTestsFixture(SchemaVersionConstants.Max, databaseName));
            string resourceId = Guid.NewGuid().ToString();
            long resourceSurrogateId = DateTimeOffset.UtcNow.Ticks << 3;

            try
            {
                await fixture.InitializeAsync();
                await CreateResourceWithVectorAsync(fixture, resourceId, resourceSurrogateId);
                await using var connection = await fixture.SqlHelper.GetSqlConnectionAsync();
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = CreateReindexCommandText(scenario);
                command.Parameters.Add("@ResourceId", SqlDbType.VarChar, 64).Value = resourceId;
                command.Parameters.Add("@ResourceSurrogateId", SqlDbType.BigInt).Value = resourceSurrogateId;
                command.Parameters.Add("@SearchParamHash", SqlDbType.VarChar, 64).Value = NewSearchParamHash;
                command.Parameters.Add("@SourceTextHash", SqlDbType.Binary, 32).Value = Enumerable.Repeat((byte)2, 32).ToArray();
                command.Parameters.Add("@SourceTextCompressed", SqlDbType.VarBinary, -1).Value = ReplacementCompressedText;
                command.Parameters.Add("@Embedding", SqlDbType.NVarChar, -1).Value = scenario == VectorReindexScenario.InvalidVectorDimensions ? "[0]" : Embedding;

                if (scenario == VectorReindexScenario.DuplicateVector)
                {
                    SqlException exception = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
                    Assert.Equal(2627, exception.Number);
                    Assert.Equal(string.Empty, exception.Procedure);
                }
                else if (scenario == VectorReindexScenario.InvalidVectorDimensions)
                {
                    SqlException exception = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
                    Assert.Equal(42204, exception.Number);
                    Assert.Equal("dbo.UpdateResourceSearchParams", exception.Procedure);
                }
                else
                {
                    Assert.Equal(expectedFailedResources, Convert.ToInt32(await command.ExecuteScalarAsync()));
                }

                await using var verifyCommand = connection.CreateCommand();
                verifyCommand.CommandText = """
                    DECLARE @ResourceTypeId smallint = (SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Observation');

                    SELECT R.SearchParamHash, V.SourceTextCompressed, @@TRANCOUNT
                    FROM dbo.Resource R
                    LEFT JOIN dbo.VectorSearchParam V
                      ON V.ResourceTypeId = R.ResourceTypeId
                     AND V.ResourceSurrogateId = R.ResourceSurrogateId
                    WHERE R.ResourceTypeId = @ResourceTypeId
                      AND R.ResourceSurrogateId = @ResourceSurrogateId;
                    """;
                verifyCommand.Parameters.Add("@ResourceSurrogateId", SqlDbType.BigInt).Value = resourceSurrogateId;

                await using var reader = await verifyCommand.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(expectedHash, reader.GetString(0));
                if (expectedCompressedText == null)
                {
                    Assert.True(reader.IsDBNull(1));
                }
                else
                {
                    Assert.Equal(expectedCompressedText, (byte[])reader.GetValue(1));
                }

                Assert.Equal(0, reader.GetInt32(2));
                Assert.False(await reader.ReadAsync());
            }
            finally
            {
                await fixture.DisposeAsync();
            }
        }

        private static async Task CreateResourceWithVectorAsync(FhirStorageTestsFixture fixture, string resourceId, long resourceSurrogateId)
        {
            await using var connection = await fixture.SqlHelper.GetSqlConnectionAsync();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DECLARE @ResourceTypeId smallint = (SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Observation');
                DECLARE @EmbeddingModelId smallint;

                INSERT INTO dbo.Resource
                    (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash)
                VALUES
                    (@ResourceTypeId, @ResourceId, 1, 0, @ResourceSurrogateId, 0, 'PUT', 0x01, 1, @SearchParamHash);

                SELECT @EmbeddingModelId = EmbeddingModelId
                FROM dbo.EmbeddingModel
                WHERE ModelName = 'integration-test-model'
                  AND ModelVersion = '1';

                IF @EmbeddingModelId IS NULL
                BEGIN
                    INSERT INTO dbo.EmbeddingModel (ModelName, ModelVersion, Dimension, DistanceMetric)
                    VALUES ('integration-test-model', '1', 1536, 'cosine');
                    SET @EmbeddingModelId = SCOPE_IDENTITY();
                END

                INSERT INTO dbo.VectorSearchParam
                    (ResourceTypeId, ResourceSurrogateId, SearchParamId, ChunkOrdinal, EmbeddingModelId, SourceTextHash, SourceTextCompressed, Embedding)
                VALUES
                    (@ResourceTypeId, @ResourceSurrogateId, 1, 0, @EmbeddingModelId, @SourceTextHash, @SourceTextCompressed, CAST(@Embedding AS vector(1536)));
                """;
            command.Parameters.Add("@ResourceId", SqlDbType.VarChar, 64).Value = resourceId;
            command.Parameters.Add("@ResourceSurrogateId", SqlDbType.BigInt).Value = resourceSurrogateId;
            command.Parameters.Add("@SearchParamHash", SqlDbType.VarChar, 64).Value = OriginalSearchParamHash;
            command.Parameters.Add("@SourceTextHash", SqlDbType.Binary, 32).Value = Enumerable.Repeat((byte)1, 32).ToArray();
            command.Parameters.Add("@SourceTextCompressed", SqlDbType.VarBinary, -1).Value = OriginalCompressedText;
            command.Parameters.Add("@Embedding", SqlDbType.NVarChar, -1).Value = Embedding;
            await command.ExecuteNonQueryAsync();
        }

        private static string CreateReindexCommandText(VectorReindexScenario scenario)
        {
            bool includeVectorParameters = scenario != VectorReindexScenario.Omitted;
            bool includeOrdinaryResource = scenario != VectorReindexScenario.VectorResourceAbsentFromOrdinaryInput;

            // The ordinary reindex input always describes the current version. A surrogate id identifies
            // exactly one version, so the only staleness a caller can actually reach is a surrogate id
            // that has since become history, which dbo.Resource.IsHistory already rejects.
            const int ordinaryVersion = 1;

            // The vector block additionally re-checks ResourceId/Version, so a vector input naming a
            // non-current version must leave stored vectors untouched.
            int vectorVersion = scenario == VectorReindexScenario.VectorInputVersionMismatch ? 0 : 1;
            string vectorDeclarations = includeVectorParameters
                ? $"""
                    DECLARE @VectorSearchResources dbo.ResourceList;
                    DECLARE @VectorSearchParams dbo.VectorSearchParamList;

                    INSERT INTO @VectorSearchResources
                        (ResourceTypeId, ResourceSurrogateId, ResourceId, Version, HasVersionToCompare, IsDeleted, IsHistory, KeepHistory, RawResource, IsRawResourceMetaSet, RequestMethod, SearchParamHash)
                    SELECT ResourceTypeId, @ResourceSurrogateId, @ResourceId, {vectorVersion}, 1, 0, 0, 1, 0x01, 1, 'PUT', @SearchParamHash
                    FROM dbo.ResourceType
                    WHERE Name = 'Observation';
                    """
                : string.Empty;
            string vectorRows = scenario is VectorReindexScenario.EvaluatedWithVector
                or VectorReindexScenario.VectorInputVersionMismatch
                or VectorReindexScenario.VectorResourceAbsentFromOrdinaryInput
                or VectorReindexScenario.DuplicateVector
                or VectorReindexScenario.InvalidVectorDimensions
                ? $"""
                    INSERT INTO @VectorSearchParams
                        (ResourceTypeId, ResourceSurrogateId, SearchParamId, ChunkOrdinal, EmbeddingModelId, SourceTextHash, SourceTextCompressed, Embedding)
                    SELECT RT.ResourceTypeId, @ResourceSurrogateId, 1, 0, M.EmbeddingModelId, @SourceTextHash, @SourceTextCompressed, @Embedding
                    FROM dbo.ResourceType RT
                    CROSS JOIN dbo.EmbeddingModel M
                    WHERE RT.Name = 'Observation'
                      AND M.ModelName = 'integration-test-model'
                      AND M.ModelVersion = '1';
                    {(scenario == VectorReindexScenario.DuplicateVector ? """

                    INSERT INTO @VectorSearchParams
                        (ResourceTypeId, ResourceSurrogateId, SearchParamId, ChunkOrdinal, EmbeddingModelId, SourceTextHash, SourceTextCompressed, Embedding)
                    SELECT RT.ResourceTypeId, @ResourceSurrogateId, 1, 0, M.EmbeddingModelId, @SourceTextHash, @SourceTextCompressed, @Embedding
                    FROM dbo.ResourceType RT
                    CROSS JOIN dbo.EmbeddingModel M
                    WHERE RT.Name = 'Observation'
                      AND M.ModelName = 'integration-test-model'
                      AND M.ModelVersion = '1';
                    """ : string.Empty)}
                    """
                : string.Empty;

            if (scenario == VectorReindexScenario.DuplicateVector)
            {
                return $"""
                    DECLARE @VectorSearchParams dbo.VectorSearchParamList;
                    {vectorRows}
                    """;
            }

            string vectorArguments = includeVectorParameters
                ? """
                    ,@VectorSearchResources = @VectorSearchResources
                    ,@VectorSearchParams = @VectorSearchParams
                    """
                : string.Empty;

            return $$"""
                DECLARE @FailedResources int;
                DECLARE @Resources dbo.ResourceList;
                DECLARE @ResourceWriteClaims dbo.ResourceWriteClaimList;
                DECLARE @ReferenceSearchParams dbo.ReferenceSearchParamList;
                DECLARE @TokenSearchParams dbo.TokenSearchParamList;
                DECLARE @TokenTexts dbo.TokenTextList;
                DECLARE @StringSearchParams dbo.StringSearchParamList;
                DECLARE @UriSearchParams dbo.UriSearchParamList;
                DECLARE @NumberSearchParams dbo.NumberSearchParamList;
                DECLARE @QuantitySearchParams dbo.QuantitySearchParamList;
                DECLARE @DateTimeSearchParams dbo.DateTimeSearchParamList;
                DECLARE @ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList;
                DECLARE @TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList;
                DECLARE @TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList;
                DECLARE @TokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamList;
                DECLARE @TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList;
                DECLARE @TokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamList;

                INSERT INTO @Resources
                    (ResourceTypeId, ResourceSurrogateId, ResourceId, Version, HasVersionToCompare, IsDeleted, IsHistory, KeepHistory, RawResource, IsRawResourceMetaSet, RequestMethod, SearchParamHash)
                SELECT ResourceTypeId, @ResourceSurrogateId, @ResourceId, {{ordinaryVersion}}, 1, 0, 0, 1, 0x01, 1, 'PUT', @SearchParamHash
                FROM dbo.ResourceType
                WHERE Name = 'Observation'
                  AND {{(includeOrdinaryResource ? "1" : "0")}} = 1;

                {{vectorDeclarations}}
                {{vectorRows}}

                EXECUTE dbo.UpdateResourceSearchParams
                    @FailedResources = @FailedResources OUT
                   ,@Resources = @Resources
                   ,@ResourceWriteClaims = @ResourceWriteClaims
                   ,@ReferenceSearchParams = @ReferenceSearchParams
                   ,@TokenSearchParams = @TokenSearchParams
                   ,@TokenTexts = @TokenTexts
                   ,@StringSearchParams = @StringSearchParams
                   ,@UriSearchParams = @UriSearchParams
                   ,@NumberSearchParams = @NumberSearchParams
                   ,@QuantitySearchParams = @QuantitySearchParams
                   ,@DateTimeSearchParams = @DateTimeSearchParams
                   ,@ReferenceTokenCompositeSearchParams = @ReferenceTokenCompositeSearchParams
                   ,@TokenTokenCompositeSearchParams = @TokenTokenCompositeSearchParams
                   ,@TokenDateTimeCompositeSearchParams = @TokenDateTimeCompositeSearchParams
                   ,@TokenQuantityCompositeSearchParams = @TokenQuantityCompositeSearchParams
                   ,@TokenStringCompositeSearchParams = @TokenStringCompositeSearchParams
                   ,@TokenNumberNumberCompositeSearchParams = @TokenNumberNumberCompositeSearchParams
                   {{vectorArguments}};

                SELECT @FailedResources;
                """;
        }
    }
}
