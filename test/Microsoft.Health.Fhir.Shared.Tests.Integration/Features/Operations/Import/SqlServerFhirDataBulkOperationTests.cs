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
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.SqlServer.Features.Client;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Features.Operations.Import
{
    public class SqlServerFhirDataBulkOperationTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private SqlServerFhirStorageTestsFixture _fixture;

        public SqlServerFhirDataBulkOperationTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task GivenBatchResources_WhenBulkCopy_RecordsShouldBeAdded()
        {
            SqlServerFhirDataBulkOperation sqlServerFhirDataBulkOperation = new SqlServerFhirDataBulkOperation(_fixture.SqlConnectionWrapperFactory, new TestSqlServerTransientFaultRetryPolicyFactory(), NullLogger<SqlServerFhirDataBulkOperation>.Instance);
            long startSurrogateId = ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(DateTime.Now);
            int count = 1001;

            await VerifyDataForBulkImport(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateResourceTable);
            await VerifyDataForBulkImport(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateDateTimeSearchParamsTable);
            await VerifyDataForBulkImport(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateNumberSearchParamsTable);
            await VerifyDataForBulkImport(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateQuantitySearchParamsTable);
            await VerifyDataForBulkImport(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateReferenceSearchParamsTable);
            await VerifyDataForBulkImport(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateReferenceTokenCompositeSearchParamsTable);
            await VerifyDataForBulkImport(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateStringSearchParamsTable);
            await VerifyDataForBulkImport(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateTokenDateTimeCompositeSearchParamsTable);
            await VerifyDataForBulkImport(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateTokenNumberNumberCompositeSearchParamsTable);
            await VerifyDataForBulkImport(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateTokenQuantityCompositeSearchParamsTable);
            await VerifyDataForBulkImport(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateTokenSearchParamsTable);
            await VerifyDataForBulkImport(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateTokenStringCompositeSearchParamsTable);
            await VerifyDataForBulkImport(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateTokenTextSearchParamsTable);
            await VerifyDataForBulkImport(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateTokenTokenCompositeSearchParamsTable);
            await VerifyDataForBulkImport(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateUriSearchParamsTable);
            await VerifyDataForBulkImport(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateCompartmentAssignmentTable);
            await VerifyDataForBulkImport(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateResourceWriteClaimTable);
        }

        [Fact]
        public async Task GivenImportedBatchResources_WhenCleanData_RecordsShouldBeDeleted()
        {
            SqlServerFhirDataBulkOperation sqlServerFhirDataBulkOperation = new SqlServerFhirDataBulkOperation(_fixture.SqlConnectionWrapperFactory, new TestSqlServerTransientFaultRetryPolicyFactory(), NullLogger<SqlServerFhirDataBulkOperation>.Instance);
            long startSurrogateId = ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(DateTime.Now);
            int count = 1001;

            List<string> tableNames = new List<string>();

            tableNames.Add(await ImportDataAsync(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateResourceTable));
            tableNames.Add(await ImportDataAsync(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateDateTimeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateNumberSearchParamsTable));
            tableNames.Add(await ImportDataAsync(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateQuantitySearchParamsTable));
            tableNames.Add(await ImportDataAsync(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateReferenceSearchParamsTable));
            tableNames.Add(await ImportDataAsync(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateReferenceTokenCompositeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateStringSearchParamsTable));
            tableNames.Add(await ImportDataAsync(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateTokenDateTimeCompositeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateTokenNumberNumberCompositeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateTokenQuantityCompositeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateTokenSearchParamsTable));
            tableNames.Add(await ImportDataAsync(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateTokenStringCompositeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateTokenTextSearchParamsTable));
            tableNames.Add(await ImportDataAsync(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateTokenTokenCompositeSearchParamsTable));
            tableNames.Add(await ImportDataAsync(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateUriSearchParamsTable));
            tableNames.Add(await ImportDataAsync(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateCompartmentAssignmentTable));
            tableNames.Add(await ImportDataAsync(sqlServerFhirDataBulkOperation, startSurrogateId, count, TestBulkDataProvider.GenerateResourceWriteClaimTable));

            await sqlServerFhirDataBulkOperation.CleanBatchResourceAsync(startSurrogateId, startSurrogateId + count - 1, CancellationToken.None);

            foreach (string tableName in tableNames)
            {
                int rCount = await GetResourceCountAsync(tableName, startSurrogateId, startSurrogateId + count);
                Assert.Equal(1, rCount);
            }
        }

        [Fact]
        public async Task GivenBatchInValidResources_WhenBulkCopy_ExceptionShouldBeThrow()
        {
            SqlServerFhirDataBulkOperation sqlServerFhirDataBulkOperation = new SqlServerFhirDataBulkOperation(_fixture.SqlConnectionWrapperFactory, new TestSqlServerTransientFaultRetryPolicyFactory(), NullLogger<SqlServerFhirDataBulkOperation>.Instance);
            long startSurrogateId = ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(DateTime.Now);
            int count = 1001;

            DataTable inputTable = TestBulkDataProvider.GenerateInValidUriSearchParamsTable(count, startSurrogateId);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await sqlServerFhirDataBulkOperation.BulkCopyDataAsync(inputTable, CancellationToken.None));
        }

        private async Task VerifyDataForBulkImport(SqlServerFhirDataBulkOperation sqlServerFhirDataBulkOperation, long startSurrogateId, int count, Func<int, long, DataTable> tableGenerator)
        {
            DataTable inputTable = tableGenerator(count, startSurrogateId);
            await sqlServerFhirDataBulkOperation.BulkCopyDataAsync(inputTable, CancellationToken.None);
            await CheckTableDataAsync(inputTable, startSurrogateId, startSurrogateId + count);
        }

        private async Task<string> ImportDataAsync(SqlServerFhirDataBulkOperation sqlServerFhirDataBulkOperation, long startSurrogateId, int count, Func<int, long, DataTable> tableGenerator)
        {
            DataTable inputTable = tableGenerator(count, startSurrogateId);
            await sqlServerFhirDataBulkOperation.BulkCopyDataAsync(inputTable, CancellationToken.None);

            return inputTable.TableName;
        }

        private async Task<int> GetResourceCountAsync(string tableName, long startSurrogateId, long endSurrogateId)
        {
            SqlConnectionWrapperFactory factory = _fixture.SqlConnectionWrapperFactory;
            using SqlConnectionWrapper connection = await factory.ObtainSqlConnectionWrapperAsync(CancellationToken.None);
            using SqlCommandWrapper command = connection.CreateSqlCommand();
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
