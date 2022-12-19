// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import;
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
            _sqlServerFhirDataBulkOperation = new SqlImportOperation(_fixture.SqlConnectionWrapperFactory, _fixture.SqlServerFhirModel, operationsConfiguration, _fixture.SchemaInformation, NullLogger<SqlImportOperation>.Instance);
        }

        [Fact]
        public async Task GivenBatchResources_WhenBulkCopy_ThenRecordsShouldBeAdded()
        {
            long startSurrogateId = ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(DateTime.Now);
            int count = 1001;
            short typeId = _fixture.SqlServerFhirModel.GetResourceTypeId("Patient");

            await VerifyDataForBulkImport(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateDateTimeSearchParamsTable);
            await VerifyDataForBulkImport(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateNumberSearchParamsTable);
            await VerifyDataForBulkImport(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateQuantitySearchParamsTable);
            await VerifyDataForBulkImport(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateReferenceSearchParamsTable);
            await VerifyDataForBulkImport(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateReferenceTokenCompositeSearchParamsTable);
            await VerifyDataForBulkImport(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateStringSearchParamsTable);
            await VerifyDataForBulkImport(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenDateTimeCompositeSearchParamsTable);
            await VerifyDataForBulkImport(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenNumberNumberCompositeSearchParamsTable);
            await VerifyDataForBulkImport(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenQuantityCompositeSearchParamsTable);
            await VerifyDataForBulkImport(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenSearchParamsTable);
            await VerifyDataForBulkImport(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenStringCompositeSearchParamsTable);
            await VerifyDataForBulkImport(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenTextSearchParamsTable);
            await VerifyDataForBulkImport(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenTokenCompositeSearchParamsTable);
            await VerifyDataForBulkImport(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateUriSearchParamsTable);
            await VerifyDataForBulkImport(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateCompartmentAssignmentTable);
            await VerifyDataForBulkImport(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateResourceWriteClaimTable);
        }

        [Fact]
        public async Task GivenImportedBatchResources_WhenCleanData_ThenRecordsShouldBeDeleted()
        {
            long startSurrogateId = ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(DateTime.Now);
            int count = 1001;
            short typeId = _fixture.SqlServerFhirModel.GetResourceTypeId("Patient");

            List<string> tableNames = new List<string>();

            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateResourceTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateDateTimeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateNumberSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateQuantitySearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateReferenceSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateReferenceTokenCompositeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateStringSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenDateTimeCompositeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenNumberNumberCompositeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenQuantityCompositeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenStringCompositeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenTextSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenTokenCompositeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateUriSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateCompartmentAssignmentTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateResourceWriteClaimTable));

            await _sqlServerFhirDataBulkOperation.CleanBatchResourceAsync("Patient", startSurrogateId, startSurrogateId + count - 1, CancellationToken.None);

            foreach (string tableName in tableNames)
            {
                int rCount = await GetResourceCountAsync(tableName, startSurrogateId, startSurrogateId + count);
                Assert.Equal(1, rCount);
            }
        }

        [Fact]
        public async Task GivenImportedBatchResources_WhenCleanDataWithWrongType_ThenRecordsShouldNotBeDeleted()
        {
            long startSurrogateId = ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(DateTime.Now);
            int count = 1001;
            short typeId = _fixture.SqlServerFhirModel.GetResourceTypeId("Patient");

            List<string> tableNames = new List<string>();

            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateResourceTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateDateTimeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateNumberSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateQuantitySearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateReferenceSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateReferenceTokenCompositeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateStringSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenDateTimeCompositeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenNumberNumberCompositeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenQuantityCompositeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenStringCompositeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenTextSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateTokenTokenCompositeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateUriSearchParamsTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateCompartmentAssignmentTable));
            tableNames.Add(await ImportDataAsync(_sqlServerFhirDataBulkOperation, startSurrogateId, count, typeId, TestBulkDataProvider.GenerateResourceWriteClaimTable));

            await _sqlServerFhirDataBulkOperation.CleanBatchResourceAsync("Observation", startSurrogateId, startSurrogateId + count - 1, CancellationToken.None);

            foreach (string tableName in tableNames)
            {
                if (VLatest.ResourceWriteClaim.TableName.Equals(tableName))
                {
                    // ResourceWriteClaim do not have resource type.
                    continue;
                }

                int rCount = await GetResourceCountAsync(tableName, startSurrogateId, startSurrogateId + count);
                Assert.Equal(count, rCount);
            }
        }

        [Fact]
        public async Task GivenDuplicateResources_WhenBulkMergeToStore_ThenOnlyDistinctResourcesImported()
        {
            long startSurrogateId = ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(DateTime.Now);
            int count = 100;
            string resourceId = Guid.NewGuid().ToString();

            List<SqlBulkCopyDataWrapper> resources = new List<SqlBulkCopyDataWrapper>();
            for (int i = 0; i < count; ++i)
            {
                resources.Add(CreateTestResource(resourceId, startSurrogateId + i));
            }

            SqlBulkCopyDataWrapper[] result = (await _sqlServerFhirDataBulkOperation.BulkMergeResourceAsync(resources, CancellationToken.None)).ToArray();
            int rCount = await GetResourceCountAsync("Resource", startSurrogateId, startSurrogateId + count);
            Assert.Single(result);
            Assert.Equal(1, rCount);
        }

        [Fact]
        public async Task GivenBatchInValidResources_WhenBulkCopy_ThenExceptionShouldBeThrow()
        {
            long startSurrogateId = ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(DateTime.Now);
            int count = 1001;

            DataTable inputTable = TestBulkDataProvider.GenerateInValidUriSearchParamsTable(count, startSurrogateId, 0);
            await Assert.ThrowsAnyAsync<Exception>(async () => await _sqlServerFhirDataBulkOperation.BulkCopyDataAsync(inputTable, CancellationToken.None));
        }

        [Fact]
        public async Task GivenListOfResources_WhenBulkMergeToStore_ThenAllResourcesShouldBeImported()
        {
            List<SqlBulkCopyDataWrapper> resources = new List<SqlBulkCopyDataWrapper>();
            long startSurrogateId = ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(DateTime.Now);

            SqlBulkCopyDataWrapper resource1 = CreateTestResource(Guid.NewGuid().ToString(), startSurrogateId);
            SqlBulkCopyDataWrapper resource2 = CreateTestResource(Guid.NewGuid().ToString(), startSurrogateId + 1);

            resources.Add(resource1);
            resources.Add(resource2);

            SqlBulkCopyDataWrapper[] result = (await _sqlServerFhirDataBulkOperation.BulkMergeResourceAsync(resources, CancellationToken.None)).ToArray();
            int rCount = await GetResourceCountAsync("Resource", startSurrogateId, startSurrogateId + 2);
            Assert.Equal(2, result.Length);
            Assert.Equal(2, rCount);
        }

        [Fact]
        public async Task GivenListOfResourcesWithDupResourceId_WhenBulkMergeToStore_ThenDistinctResourceShouldBeImported()
        {
            List<SqlBulkCopyDataWrapper> resources = new List<SqlBulkCopyDataWrapper>();
            long startSurrogateId = ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(DateTime.Now);

            SqlBulkCopyDataWrapper resource1 = CreateTestResource(Guid.NewGuid().ToString(), startSurrogateId);
            SqlBulkCopyDataWrapper resource2 = CreateTestResource(Guid.NewGuid().ToString(), startSurrogateId + 1);

            resources.Add(resource1);
            resources.Add(resource2);

            SqlBulkCopyDataWrapper[] result = (await _sqlServerFhirDataBulkOperation.BulkMergeResourceAsync(resources, CancellationToken.None)).ToArray();
            int rCount = await GetResourceCountAsync("Resource", startSurrogateId, startSurrogateId + 2);
            Assert.Equal(2, result.Count());
            Assert.Equal(2, rCount);

            resource1.ResourceSurrogateId = startSurrogateId + 2;
            resource1.ResourceSurrogateId = startSurrogateId + 3;
            result = (await _sqlServerFhirDataBulkOperation.BulkMergeResourceAsync(resources, CancellationToken.None)).ToArray();
            rCount = await GetResourceCountAsync("Resource", startSurrogateId, startSurrogateId + 4);
            Assert.Empty(result);
            Assert.Equal(2, rCount);
        }

        [Fact]
        public async Task GivenListOfResources_WhenBulkMergeToStoreTwice_ThenSecondMergeShouldFail()
        {
            List<SqlBulkCopyDataWrapper> resources = new List<SqlBulkCopyDataWrapper>();
            long startSurrogateId = ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(DateTime.Now);

            string resourceId = Guid.NewGuid().ToString();
            SqlBulkCopyDataWrapper resource1 = CreateTestResource(resourceId, startSurrogateId);
            SqlBulkCopyDataWrapper resource2 = CreateTestResource(resourceId, startSurrogateId + 1);

            resources.Add(resource1);
            resources.Add(resource2);

            SqlBulkCopyDataWrapper[] result = (await _sqlServerFhirDataBulkOperation.BulkMergeResourceAsync(resources, CancellationToken.None)).ToArray();
            int rCount = await GetResourceCountAsync("Resource", startSurrogateId, startSurrogateId + 2);
            Assert.Single(result);
            Assert.Equal(1, rCount);
        }

        [Fact]
        public async Task GivenUnclusteredIndexes_WhenRebuildDisableIndexes_ThenIndexShouldBeChanged()
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

                ////await RebuildIndex(index.tableName, index.indexName, index.pageCompression);
                ////isDisabled = await GetIndexDisableStatus(index.indexName);
                ////Assert.False(isDisabled);

                ////// Can rebuild twice
                ////await RebuildIndex(index.tableName, index.indexName, index.pageCompression);
                ////isDisabled = await GetIndexDisableStatus(index.indexName);
                ////Assert.False(isDisabled);

                ////string compressionAfter = await GetIndexCompression(index.indexName, index.tableName);
                ////Assert.Equal($"{index.indexName}_{compressionBefore}", $"{index.indexName}_{compressionAfter}");
            }
        }

        private static SqlBulkCopyDataWrapper CreateTestResource(string resourceId, long surrogateId)
        {
            SqlBulkCopyDataWrapper resource = new SqlBulkCopyDataWrapper();
            resource.Resource =
                new ResourceWrapper(
                    resourceId,
                    "0",
                    "Dummy",
                    new RawResource(_rawResourceTestValue, Fhir.Core.Models.FhirResourceFormat.Json, true),
                    new ResourceRequest("PUT"),
                    DateTimeOffset.UtcNow,
                    false,
                    null,
                    null,
                    null,
                    "SearchParam");
            resource.ResourceSurrogateId = surrogateId;
            resource.ResourceTypeId = 0;
            resource.BulkImportResource = new BulkImportResourceTypeV1Row(0, resourceId, 0, false, surrogateId, false, "POST", new MemoryStream(Encoding.UTF8.GetBytes("Test")), true, "Test");
            return resource;
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

        private async Task VerifyDataForBulkImport(SqlImportOperation sqlServerFhirDataBulkOperation, long startSurrogateId, int count, short resourceTypeId, Func<int, long, short, string, DataTable> tableGenerator, string resourceId = null)
        {
            DataTable inputTable = tableGenerator(count, startSurrogateId, resourceTypeId, resourceId);
            await sqlServerFhirDataBulkOperation.BulkCopyDataAsync(inputTable, CancellationToken.None);
            await CheckTableDataAsync(inputTable, startSurrogateId, startSurrogateId + count);
        }

        private async Task<string> ImportDataAsync(SqlImportOperation sqlServerFhirDataBulkOperation, long startSurrogateId, int count, short resourceTypeId, Func<int, long, short, string, DataTable> tableGenerator, string resourceId = null)
        {
            DataTable inputTable = tableGenerator(count, startSurrogateId, resourceTypeId, resourceId);
            await sqlServerFhirDataBulkOperation.BulkCopyDataAsync(inputTable, CancellationToken.None);

            return inputTable.TableName;
        }

        private async Task<int> GetResourceCountAsync(string tableName, long startSurrogateId, long endSurrogateId)
        {
            SqlConnectionWrapperFactory factory = _fixture.SqlConnectionWrapperFactory;
            using SqlConnectionWrapper connection = await factory.ObtainSqlConnectionWrapperAsync(CancellationToken.None);
            using SqlCommandWrapper command = connection.CreateRetrySqlCommand();
            command.CommandText = $"select count(*) from {tableName} where ResourceSurrogateId >= {startSurrogateId} and ResourceSurrogateId < {endSurrogateId}";

            return (int)(await command.ExecuteScalarAsync(CancellationToken.None));
        }

        private async Task CheckTableDataAsync(DataTable table, long startSurrogateId, long endSurrogateId)
        {
            SqlConnectionWrapperFactory factory = _fixture.SqlConnectionWrapperFactory;
            using SqlConnectionWrapper connection = await factory.ObtainSqlConnectionWrapperAsync(CancellationToken.None);
            using SqlDataAdapter adapter = new SqlDataAdapter();

            DataColumn[] columns = new DataColumn[table.Columns.Count];
            table.Columns.CopyTo(columns, 0);
            string columnsString = string.Join(',', columns.Select(c => c.ColumnName));
            string queryText = $"select {columnsString} from {table.TableName} where ResourceSurrogateId >= {startSurrogateId} and ResourceSurrogateId < {endSurrogateId}";
            adapter.SelectCommand = new SqlCommand(queryText, connection.SqlConnection);

            DataSet result = new DataSet();
            adapter.Fill(result);

            Assert.Equal(columns.Length, result.Tables[0].Columns.Count);
            Assert.Equal(table.Rows.Count, result.Tables[0].Rows.Count);
        }
    }
}
