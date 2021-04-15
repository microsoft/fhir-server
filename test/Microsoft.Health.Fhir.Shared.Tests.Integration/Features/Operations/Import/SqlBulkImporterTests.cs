// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import.DataGenerator;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Features.Operations.Import
{
    public class SqlBulkImporterTests
    {
        [Fact]
        public async Task GivenSqlBulkImporter_WhenImportData_AllDataShouldBeImported()
        {
            await VerifyBulkImporterBehaviourAsync(4321, 1234, 17);
        }

        [Fact]
        public async Task GivenSqlBulkImporter_WhenImportDataCountEqualsBatchCount_AllDataShouldBeImported()
        {
            await VerifyBulkImporterBehaviourAsync(10, 1234, 10);
        }

        [Fact]
        public async Task GivenSqlBulkImporter_WhenImportDataCountLessThanBatchCount_AllDataShouldBeImported()
        {
            await VerifyBulkImporterBehaviourAsync(1, 1234, 10);
        }

        private static async Task VerifyBulkImporterBehaviourAsync(int resourceCount, long startSurrogatedId, int maxResourceCountInBatch)
        {
            DataTable table1 = new DataTable();
            DataTable table2 = new DataTable();
            IFhirDataBulkOperation testFhirDataBulkOperation = Substitute.For<IFhirDataBulkOperation>();
            testFhirDataBulkOperation
                .When(t => t.BulkCopyDataAsync(Arg.Any<DataTable>(), Arg.Any<CancellationToken>()))
                .Do(call =>
                {
                    DataTable table = (DataTable)call[0];
                    if (table.TableName.Equals("Table1"))
                    {
                        table1.Merge(table);
                    }
                    else
                    {
                        table2.Merge(table);
                    }
                });

            ISqlBulkCopyDataWrapperFactory dataWrapperFactory = Substitute.For<ISqlBulkCopyDataWrapperFactory>();
            dataWrapperFactory.CreateSqlBulkCopyDataWrapper(Arg.Any<BulkImportResourceWrapper>())
                .Returns((callInfo) =>
                {
                    BulkImportResourceWrapper resource = (BulkImportResourceWrapper)callInfo[0];
                    return new SqlBulkCopyDataWrapper()
                    {
                        ResourceSurrogateId = resource.ResourceSurrogateId,
                    };
                });

            List<TableBulkCopyDataGenerator<SqlBulkCopyDataWrapper>> generators = new List<TableBulkCopyDataGenerator<SqlBulkCopyDataWrapper>>()
            {
                new TestDataGenerator("Table1", 1),
                new TestDataGenerator("Table2", 2),
            };
            SqlBulkImporter importer = new SqlBulkImporter(testFhirDataBulkOperation, dataWrapperFactory, generators);
            importer.MaxResourceCountInBatch = maxResourceCountInBatch;

            Channel<BulkImportResourceWrapper> inputs = Channel.CreateUnbounded<BulkImportResourceWrapper>();

            for (int i = 0; i < resourceCount; ++i)
            {
                await inputs.Writer.WriteAsync(new BulkImportResourceWrapper(startSurrogatedId + i, 0, null, null));
            }

            Dictionary<string, long> result = new Dictionary<string, long>();
            Progress<(string tableName, long endSurrogateId)> progress =
                new Progress<(string tableName, long endSurrogateId)>();
            Task importTask = importer.ImportResourceAsync(inputs, progress, CancellationToken.None);

            await Task.Delay(TimeSpan.FromSeconds(10));
            inputs.Writer.Complete();
            await importTask;

            Assert.Equal(resourceCount, table1.Rows.Count);
            Assert.Equal(resourceCount * 2, table2.Rows.Count);

            for (int i = 0; i < resourceCount; ++i)
            {
                Assert.Equal(startSurrogatedId + i, table1.Rows[i]["ResourceSurrogateId"]);
                Assert.Equal(startSurrogatedId + i, table2.Rows[i * 2]["ResourceSurrogateId"]);
                Assert.Equal(startSurrogatedId + i, table2.Rows[(i * 2) + 1]["ResourceSurrogateId"]);
            }
        }

        private class TestDataGenerator : TableBulkCopyDataGenerator<SqlBulkCopyDataWrapper>
        {
            private string _tableName;
            private int _subResourceCount;

            public TestDataGenerator(string tableName, int subResourceCount = 1)
            {
                _tableName = tableName;
                _subResourceCount = subResourceCount;
            }

            internal override string TableName => _tableName;

            internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
            {
                for (int i = 0; i < _subResourceCount; ++i)
                {
                    DataRow newRow = table.NewRow();

                    FillColumn(newRow, "ResourceSurrogateId", input.ResourceSurrogateId);
                    FillColumn(newRow, "Id", Guid.NewGuid().ToString("N"));

                    table.Rows.Add(newRow);
                }
            }

            internal override void FillSchema(DataTable table)
            {
                table.Columns.Add(new DataColumn("ResourceSurrogateId", typeof(long)));
                table.Columns.Add(new DataColumn("Id", typeof(string)));
            }
        }
    }
}
