// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Index = Microsoft.Health.SqlServer.Features.Schema.Model.Index;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Features.Operations.Import
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class SqlServerFhirDataBulkOperationTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private SqlServerFhirStorageTestsFixture _fixture;
        private SqlImportOperation _sqlServerFhirDataBulkOperation;
        private SchemaInformation _schemaInformation;
        private static string _rawResourceTestValue = "{\"resourceType\": \"Parameters\",\"parameter\": []}";

        public SqlServerFhirDataBulkOperationTests(
            SqlServerFhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            var operationsConfiguration = Substitute.For<IOptions<OperationsConfiguration>>();
            operationsConfiguration.Value.Returns(new OperationsConfiguration());

            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
            _sqlServerFhirDataBulkOperation = new SqlImportOperation(_fixture.SqlConnectionWrapperFactory, _fixture.SqlServerFhirDataStore, _fixture.SqlServerFhirModel, operationsConfiguration, _fixture.SchemaInformation, NullLogger<SqlImportOperation>.Instance);
        }

        [Fact]
        public async Task GivenDuplicateResources_WhenBulkMergeToStore_ThenOnlyDistinctResourcesImported()
        {
            long startSurrogateId = ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(DateTime.Now);
            int count = 100;
            string resourceId = Guid.NewGuid().ToString();

            var resources = new List<ImportResource>();
            for (int i = 0; i < count; ++i)
            {
                resources.Add(CreateTestResource(resourceId, startSurrogateId + i));
            }

            ImportResource[] result = (await _sqlServerFhirDataBulkOperation.MergeResourcesAsync(resources, CancellationToken.None)).ToArray();
            int rCount = await GetResourceCountAsync("Resource", startSurrogateId, startSurrogateId + count);
            Assert.Single(result);
            Assert.Equal(1, rCount);
        }

        [Fact]
        public async Task GivenBatchInValidResources_WhenBulkCopy_ThenExceptionShouldBeThrow()
        {
            long startSurrogateId = ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(DateTime.Now);
            short typeId = _fixture.SqlServerFhirModel.GetResourceTypeId("Patient");
            int count = 1;

            DataTable inputTable = TestBulkDataProvider.GenerateInValidUriSearchParamsTable(count, startSurrogateId, typeId);
            await Assert.ThrowsAnyAsync<Exception>(async () => await _sqlServerFhirDataBulkOperation.BulkCopyDataAsync(inputTable, CancellationToken.None));

            inputTable = TestBulkDataProvider.GenerateInvalidDataTokenQuantityCompositeSearchParamsTable(count, startSurrogateId, typeId);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await _sqlServerFhirDataBulkOperation.BulkCopyDataAsync(inputTable, CancellationToken.None));
        }

        [Fact]
        public async Task GivenListOfResources_WhenImport_ThenAllResourcesShouldBeImported()
        {
            var startSurrogateId = await GetCurrentSurrogateId();
            var resources = new[] { CreateTestResource(Guid.NewGuid().ToString(), 1), CreateTestResource(Guid.NewGuid().ToString(), 2) };
            var result = (await _sqlServerFhirDataBulkOperation.MergeResourcesAsync(resources, CancellationToken.None)).ToList();
            var endSurrogateId = await GetCurrentSurrogateId();
            var importedCount = await GetResourceCountAsync("Resource", startSurrogateId, endSurrogateId);
            Assert.Equal(2, result.Count);
            Assert.Equal(2, importedCount);
        }

        [Fact]
        public async Task GivenListOfResources_WhenImportTwice_ThenOnlyDistinctResourcesShouldBeImported()
        {
            var startSurrogateId = await GetCurrentSurrogateId();
            var resources = new[] { CreateTestResource(Guid.NewGuid().ToString(), 1), CreateTestResource(Guid.NewGuid().ToString(), 2) };
            var result = (await _sqlServerFhirDataBulkOperation.MergeResourcesAsync(resources, CancellationToken.None)).ToList();
            var endSurrogateId = await GetCurrentSurrogateId();
            var importedCount = await GetResourceCountAsync("Resource", startSurrogateId, endSurrogateId);
            Assert.Equal(2, result.Count);
            Assert.Equal(2, importedCount);

            result = (await _sqlServerFhirDataBulkOperation.MergeResourcesAsync(resources, CancellationToken.None)).ToList();
            endSurrogateId = await GetCurrentSurrogateId();
            importedCount = await GetResourceCountAsync("Resource", startSurrogateId, endSurrogateId);
            Assert.Equal(2, result.Count);
            Assert.Equal(2, importedCount);
        }

        [Fact]
        public async Task GivenListOfResourcesWithDupResourceId_WhenBulkMergeToStore_ThenDistinctResourceShouldBeImported()
        {
            var startSurrogateId = await GetCurrentSurrogateId();
            var resourceId = Guid.NewGuid().ToString();
            var resources = new[] { CreateTestResource(resourceId, 1), CreateTestResource(resourceId, 2) };
            var result = (await _sqlServerFhirDataBulkOperation.MergeResourcesAsync(resources, CancellationToken.None)).ToList();
            var endSurrogateId = await GetCurrentSurrogateId();
            var importedCount = await GetResourceCountAsync("Resource", startSurrogateId, endSurrogateId);
            Assert.Single(result);
            Assert.Equal(1, importedCount);
        }

        [Fact]
        public async Task GivenUnclusteredIndexes_WhenDisableIndexes_ThenIndexShouldBeChanged()
        {
            List<(Table table, Index index, bool pageCompression)> indexeRecords = new List<(Table table, Index index, bool pageCompression)>();
            indexeRecords.AddRange(_sqlServerFhirDataBulkOperation.IndexesList());
            indexeRecords.AddRange(_sqlServerFhirDataBulkOperation.UniqueIndexesList());
            (string tableName, string indexName, bool pageCompression)[] indexes = indexeRecords.Select(indexRecord => (indexRecord.table.TableName, indexRecord.index.IndexName, indexRecord.pageCompression)).ToArray();
            foreach (var index in indexes)
            {
                string compressionBefore = await GetIndexCompression(index.indexName, index.tableName);
                bool isDisabled = await GetIndexDisableStatus(index.indexName);
                Assert.False(isDisabled);
                await DisableIndex(index.tableName, index.indexName);
                isDisabled = await GetIndexDisableStatus(index.indexName);
                Assert.True(isDisabled);

                // Can disable twice
                await DisableIndex(index.tableName, index.indexName);
                isDisabled = await GetIndexDisableStatus(index.indexName);
                Assert.True(isDisabled);
            }
        }

        private static ImportResource CreateTestResource(string resourceId, long surrogateId)
        {
            var wrapper = new ResourceWrapper(
                    resourceId,
                    "1",
                    "Patient",
                    new RawResource(_rawResourceTestValue, Fhir.Core.Models.FhirResourceFormat.Json, true),
                    new ResourceRequest("PUT"),
                    DateTimeOffset.UtcNow,
                    false,
                    null,
                    null,
                    null,
                    "SearchParam");
            return new ImportResource(surrogateId, surrogateId, wrapper);
        }

        private async Task<bool> GetIndexDisableStatus(string indexName)
        {
            SqlConnectionWrapperFactory factory = _fixture.SqlConnectionWrapperFactory;
            using (SqlConnectionWrapper connection = await factory.ObtainSqlConnectionWrapperAsync(CancellationToken.None))
            using (SqlCommandWrapper command = connection.CreateRetrySqlCommand())
            {
                command.CommandText = $"select is_disabled from sys.indexes where name = '{indexName}'";

                return (bool)(await command.ExecuteScalarAsync(CancellationToken.None));
            }
        }

        private async Task<string> GetIndexCompression(string indexName, string tableName)
        {
            string query = @$"SELECT TOP 1 [p].[data_compression_desc] AS [Compression]
                            FROM[sys].[partitions] AS[p]
                            INNER JOIN sys.tables AS[t] ON[t].[object_id] = [p].[object_id]
                            INNER JOIN sys.indexes AS[i] ON[i].[object_id] = [p].[object_id] AND[i].[index_id] = [p].[index_id]
                            WHERE[p].[index_id] > 1 and [i].[name] = '{indexName}' and [t].[name] = '{tableName.Replace("dbo.", string.Empty)}'";
            SqlConnectionWrapperFactory factory = _fixture.SqlConnectionWrapperFactory;
            using (SqlConnectionWrapper connection = await factory.ObtainSqlConnectionWrapperAsync(CancellationToken.None))
            using (SqlCommandWrapper command = connection.CreateRetrySqlCommand())
            {
                command.CommandText = query;

                return (string)(await command.ExecuteScalarAsync(CancellationToken.None));
            }
        }

        private async Task<bool> DisableIndex(string tableName, string indexName)
        {
            SqlConnectionWrapperFactory factory = _fixture.SqlConnectionWrapperFactory;
            using (SqlConnectionWrapper sqlConnectionWrapper = await factory.ObtainSqlConnectionWrapperAsync(CancellationToken.None))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.DisableIndex.PopulateCommand(sqlCommandWrapper, tableName, indexName);
                var returnParameter = sqlCommandWrapper.Parameters.Add("@ReturnVal", SqlDbType.Int);
                returnParameter.Direction = ParameterDirection.ReturnValue;

                await sqlCommandWrapper.ExecuteNonQueryAsync(CancellationToken.None);
                bool isExecuted = Convert.ToBoolean(returnParameter.Value);

                return isExecuted;
            }
        }

        private async Task<int> GetResourceCountAsync(string tableName, long startSurrogateId, long endSurrogateId)
        {
            var factory = _fixture.SqlConnectionWrapperFactory;
            using var connection = await factory.ObtainSqlConnectionWrapperAsync(CancellationToken.None);
            using var command = connection.CreateRetrySqlCommand();
            command.CommandText = $"SELECT count(*) FROM dbo.{tableName} WHERE ResourceSurrogateId >= {startSurrogateId} AND ResourceSurrogateId < {endSurrogateId}";
            return (int)(await command.ExecuteScalarAsync(CancellationToken.None));
        }

        private async Task<long> GetCurrentSurrogateId()
        {
            var factory = _fixture.SqlConnectionWrapperFactory;
            using var connection = await factory.ObtainSqlConnectionWrapperAsync(CancellationToken.None);
            using var command = connection.CreateRetrySqlCommand();
            command.CommandText = $"SELECT getUTCdate()";
            var time = (DateTime)await command.ExecuteScalarAsync(CancellationToken.None);
            return ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(time);
        }
    }
}
