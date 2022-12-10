// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import.DataGenerator;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Features.Operations.Import
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class SqlBulkImporterTests
    {
        [Fact]
        public async Task GivenSqlBulkImporter_WhenImportData_ThenAllDataShouldBeImported()
        {
            long expectedSucceedCount = 4321;
            long expectedFailedCount = 0;
            long startIndex = 0;
            int maxResourceCountInBatch = 123;
            int checkpointBatchCount = 345;
            int maxConcurrentCount = 5;

            await VerifyBulkImporterBehaviourAsync(expectedSucceedCount, expectedFailedCount, startIndex, maxResourceCountInBatch, checkpointBatchCount, maxConcurrentCount);
        }

        [Fact]
        public async Task GivenSqlBulkImporter_WhenImportDataWithError_ThenAllDataAndErrorShouldBeImported()
        {
            long expectedSucceedCount = 2000;
            long expectedFailedCount = 123;
            long startIndex = 0;
            int maxResourceCountInBatch = 123;
            int checkpointBatchCount = 345;
            int maxConcurrentCount = 5;

            await VerifyBulkImporterBehaviourAsync(expectedSucceedCount, expectedFailedCount, startIndex, maxResourceCountInBatch, checkpointBatchCount, maxConcurrentCount);
        }

        [Fact]
        public async Task GivenSqlBulkImporter_WhenImportDataWithAllFailed_ThenAllErrorShouldBeImported()
        {
            long expectedSucceedCount = 0;
            long expectedFailedCount = 1234;
            long startIndex = 0;
            int maxResourceCountInBatch = 123;
            int checkpointBatchCount = 345;
            int maxConcurrentCount = 5;

            await VerifyBulkImporterBehaviourAsync(expectedSucceedCount, expectedFailedCount, startIndex, maxResourceCountInBatch, checkpointBatchCount, maxConcurrentCount);
        }

        [Fact]
        public async Task GivenSqlBulkImporter_WhenImportDataEqualsBatchCount_ThenAllDataAndErrorShouldBeImported()
        {
            long expectedSucceedCount = 10;
            long expectedFailedCount = 1;
            long startIndex = 0;
            int maxResourceCountInBatch = 11;
            int checkpointBatchCount = 11;
            int maxConcurrentCount = 5;

            await VerifyBulkImporterBehaviourAsync(expectedSucceedCount, expectedFailedCount, startIndex, maxResourceCountInBatch, checkpointBatchCount, maxConcurrentCount);
        }

        [Fact]
        public async Task GivenSqlBulkImporter_WhenImportDataLessThanBatchCount_ThenAllDataAndErrorShouldBeImported()
        {
            long expectedSucceedCount = 10;
            long expectedFailedCount = 1;
            long startIndex = 0;
            int maxResourceCountInBatch = 100;
            int checkpointBatchCount = 100;
            int maxConcurrentCount = 5;

            await VerifyBulkImporterBehaviourAsync(expectedSucceedCount, expectedFailedCount, startIndex, maxResourceCountInBatch, checkpointBatchCount, maxConcurrentCount);
        }

        [Fact]
        public async Task GivenSqlBulkImporter_WhenImportDataFromMiddle_ThenAllDataAndErrorShouldBeImported()
        {
            long expectedSucceedCount = 10;
            long expectedFailedCount = 1;
            long startIndex = 10;
            int maxResourceCountInBatch = 100;
            int checkpointBatchCount = 100;
            int maxConcurrentCount = 5;

            await VerifyBulkImporterBehaviourAsync(expectedSucceedCount, expectedFailedCount, startIndex, maxResourceCountInBatch, checkpointBatchCount, maxConcurrentCount);
        }

        [Fact]
        public async Task GivenSqlBulkImporter_WhenImportData_ThenProgressUpdateShouldInSequence()
        {
            long expectedSucceedCount = 1000;
            long expectedFailedCount = 100;
            long startIndex = 10;
            int maxResourceCountInBatch = 10;
            int checkpointBatchCount = 1;
            int maxConcurrentCount = 10;

            await VerifyBulkImporterBehaviourAsync(expectedSucceedCount, expectedFailedCount, startIndex, maxResourceCountInBatch, checkpointBatchCount, maxConcurrentCount);
        }

        [Fact]
        public async Task GivenSqlBulkImporter_WhenImportDataWithUnExceptedExceptionInBulkOpertation_ThenChannelShouldBeCompleteAndExceptionShouldThrow()
        {
            Channel<ImportResource> inputs = Channel.CreateUnbounded<ImportResource>();
            await inputs.Writer.WriteAsync(new ImportResource(0, 0, default(ResourceWrapper)));
            inputs.Writer.Complete();

            ISqlImportOperation testFhirDataBulkOperation = Substitute.For<ISqlImportOperation>();
            testFhirDataBulkOperation
                .BulkCopyDataAsync(Arg.Any<DataTable>(), Arg.Any<CancellationToken>())
                .Returns((callInfo) =>
                {
                    throw new InvalidOperationException();
                });
            testFhirDataBulkOperation
                .BulkMergeResourceAsync(Arg.Any<IEnumerable<SqlBulkCopyDataWrapper>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    IEnumerable<SqlBulkCopyDataWrapper> resources = (IEnumerable<SqlBulkCopyDataWrapper>)call[0];

                    return resources;
                });

            IImportErrorSerializer errorSerializer = Substitute.For<IImportErrorSerializer>();
            ISqlBulkCopyDataWrapperFactory dataWrapperFactory = Substitute.For<ISqlBulkCopyDataWrapperFactory>();
            dataWrapperFactory.CreateSqlBulkCopyDataWrapper(Arg.Any<ImportResource>())
                .Returns((callInfo) =>
                {
                    ImportResource resource = (ImportResource)callInfo[0];
                    return new SqlBulkCopyDataWrapper()
                    {
                        ResourceSurrogateId = resource.Id,
                    };
                });

            List<TableBulkCopyDataGenerator> generators = new List<TableBulkCopyDataGenerator>()
            {
                new TestDataGenerator("Table1", 1),
                new TestDataGenerator("Table2", 2),
            };

            IOptions<OperationsConfiguration> operationsConfiguration = Substitute.For<IOptions<OperationsConfiguration>>();
            operationsConfiguration.Value.Returns(new OperationsConfiguration());

            SqlResourceBulkImporter importer = new SqlResourceBulkImporter(testFhirDataBulkOperation, dataWrapperFactory, errorSerializer, generators, operationsConfiguration, NullLogger<SqlResourceBulkImporter>.Instance);

            List<string> errorLogs = new List<string>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            (Channel<ImportProcessingProgress> progressChannel, Task importTask) = importer.Import(inputs, importErrorStore, CancellationToken.None);

            await foreach (ImportProcessingProgress progress in progressChannel.Reader.ReadAllAsync())
            {
                // Do nothing...
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => importTask);
        }

        [Fact]
        public async Task GivenSqlBulkImporter_WhenImportDataWithUnExceptedExceptionInErrorLogUpload_ThenChannelShouldBeCompleteAndExceptionShouldThrow()
        {
            Channel<ImportResource> inputs = Channel.CreateUnbounded<ImportResource>();
            await inputs.Writer.WriteAsync(new ImportResource(0, 0, "Error message"));
            inputs.Writer.Complete();

            ISqlImportOperation testFhirDataBulkOperation = Substitute.For<ISqlImportOperation>();
            ISqlBulkCopyDataWrapperFactory dataWrapperFactory = Substitute.For<ISqlBulkCopyDataWrapperFactory>();
            IImportErrorSerializer errorSerializer = Substitute.For<IImportErrorSerializer>();
            List<TableBulkCopyDataGenerator> generators = new List<TableBulkCopyDataGenerator>();

            IOptions<OperationsConfiguration> operationsConfiguration = Substitute.For<IOptions<OperationsConfiguration>>();
            operationsConfiguration.Value.Returns(new OperationsConfiguration());

            SqlResourceBulkImporter importer = new SqlResourceBulkImporter(testFhirDataBulkOperation, dataWrapperFactory, errorSerializer, generators, operationsConfiguration, NullLogger<SqlResourceBulkImporter>.Instance);

            List<string> errorLogs = new List<string>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            importErrorStore.UploadErrorsAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns((_) => throw new InvalidOperationException());

            (Channel<ImportProcessingProgress> progressChannel, Task importTask) = importer.Import(inputs, importErrorStore, CancellationToken.None);

            await foreach (ImportProcessingProgress progress in progressChannel.Reader.ReadAllAsync())
            {
                // Do nothing...
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => importTask);
        }

        [Fact]
        public async Task GivenSqlBulkImporter_WhenImportDataWithUnExceptedExceptionInProcessResource_ThenChannelShouldBeCompleteAndExceptionShouldThrow()
        {
            Channel<ImportResource> inputs = Channel.CreateUnbounded<ImportResource>();
            await inputs.Writer.WriteAsync(new ImportResource(0, 0, default(ResourceWrapper)));
            inputs.Writer.Complete();

            ISqlImportOperation testFhirDataBulkOperation = Substitute.For<ISqlImportOperation>();
            IImportErrorSerializer errorSerializer = Substitute.For<IImportErrorSerializer>();
            ISqlBulkCopyDataWrapperFactory dataWrapperFactory = Substitute.For<ISqlBulkCopyDataWrapperFactory>();
            dataWrapperFactory.CreateSqlBulkCopyDataWrapper(Arg.Any<ImportResource>())
                .Returns((callInfo) =>
                {
                    throw new InvalidOperationException();
                });
            List<TableBulkCopyDataGenerator> generators = new List<TableBulkCopyDataGenerator>();

            IOptions<OperationsConfiguration> operationsConfiguration = Substitute.For<IOptions<OperationsConfiguration>>();
            operationsConfiguration.Value.Returns(new OperationsConfiguration());

            SqlResourceBulkImporter importer = new SqlResourceBulkImporter(testFhirDataBulkOperation, dataWrapperFactory, errorSerializer, generators, operationsConfiguration, NullLogger<SqlResourceBulkImporter>.Instance);

            List<string> errorLogs = new List<string>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();

            (Channel<ImportProcessingProgress> progressChannel, Task importTask) = importer.Import(inputs, importErrorStore, CancellationToken.None);

            await foreach (ImportProcessingProgress progress in progressChannel.Reader.ReadAllAsync())
            {
                // Do nothing...
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => importTask);
        }

        private static async Task VerifyBulkImporterBehaviourAsync(long expectedSucceedCount, long expectedFailedCount, long startIndex, int maxResourceCountInBatch, int checkpointBatchCount, int maxConcurrentCount)
        {
            Channel<ImportResource> inputs = Channel.CreateUnbounded<ImportResource>();
            _ = Task.Run(async () =>
            {
                long totalCount = expectedSucceedCount + expectedFailedCount;
                bool[] resourceFailedRecords = new bool[totalCount];
                for (long i = 0; i < expectedFailedCount; ++i)
                {
                    resourceFailedRecords[i] = true;
                }

                resourceFailedRecords = resourceFailedRecords.OrderBy(_ => Guid.NewGuid()).ToArray();
                for (long i = 0; i < totalCount; ++i)
                {
                    if (resourceFailedRecords[i])
                    {
                        await inputs.Writer.WriteAsync(new ImportResource(i, i + startIndex, "Error message"));
                    }
                    else
                    {
                        await inputs.Writer.WriteAsync(new ImportResource(i, i + startIndex, CreateResourceWrapper()));
                    }
                }

                inputs.Writer.Complete();
            });

            await VerifyBulkImporterBehaviourAsync(inputs, expectedSucceedCount, expectedFailedCount, startIndex + expectedSucceedCount + expectedFailedCount, maxResourceCountInBatch, checkpointBatchCount, maxConcurrentCount);
        }

        private static async Task VerifyBulkImporterBehaviourAsync(Channel<ImportResource> inputs, long expectedSucceedCount, long expectedFailedCount, long expectedEndIndex, int maxResourceCountInBatch, int checkpointBatchCount, int maxConcurrentCount)
        {
            DataTable table1 = new DataTable();
            DataTable table2 = new DataTable();
            List<SqlBulkCopyDataWrapper> importedResources = new List<SqlBulkCopyDataWrapper>();

            ISqlImportOperation testFhirDataBulkOperation = Substitute.For<ISqlImportOperation>();
            testFhirDataBulkOperation
                .When(t => t.BulkCopyDataAsync(Arg.Any<DataTable>(), Arg.Any<CancellationToken>()))
                .Do(call =>
                {
                    DataTable table = (DataTable)call[0];
                    if (table.TableName.Equals("Table1"))
                    {
                        table1.Merge(table);
                    }
                    else if (table.TableName.Equals("Table2"))
                    {
                        table2.Merge(table);
                    }
                });
            testFhirDataBulkOperation
                .BulkMergeResourceAsync(Arg.Any<IEnumerable<SqlBulkCopyDataWrapper>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    IEnumerable<SqlBulkCopyDataWrapper> resources = (IEnumerable<SqlBulkCopyDataWrapper>)call[0];
                    importedResources.AddRange(resources);

                    return resources;
                });

            IImportErrorSerializer errorSerializer = Substitute.For<IImportErrorSerializer>();
            ISqlBulkCopyDataWrapperFactory dataWrapperFactory = Substitute.For<ISqlBulkCopyDataWrapperFactory>();
            dataWrapperFactory.CreateSqlBulkCopyDataWrapper(Arg.Any<ImportResource>())
                .Returns((callInfo) =>
                {
                    ImportResource resource = (ImportResource)callInfo[0];
                    return new SqlBulkCopyDataWrapper()
                    {
                        ResourceSurrogateId = resource.Id,
                    };
                });

            List<TableBulkCopyDataGenerator> generators = new List<TableBulkCopyDataGenerator>()
            {
                new TestDataGenerator("Table1", 1),
                new TestDataGenerator("Table2", 2),
            };

            IOptions<OperationsConfiguration> operationsConfiguration = Substitute.For<IOptions<OperationsConfiguration>>();
            OperationsConfiguration operationsConfig = new OperationsConfiguration();
            operationsConfig.Import.SqlBatchSizeForImportResourceOperation = maxResourceCountInBatch;
            operationsConfig.Import.SqlMaxImportOperationConcurrentCount = maxConcurrentCount;
            operationsConfig.Import.SqlImportBatchSizeForCheckpoint = checkpointBatchCount;
            operationsConfiguration.Value.Returns(operationsConfig);

            SqlResourceBulkImporter importer = new SqlResourceBulkImporter(testFhirDataBulkOperation, dataWrapperFactory, errorSerializer, generators, operationsConfiguration, NullLogger<SqlResourceBulkImporter>.Instance);

            List<string> errorLogs = new List<string>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            importErrorStore.When(t => t.UploadErrorsAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>()))
                .Do(call =>
                {
                    string[] errors = (string[])call[0];
                    errorLogs.AddRange(errors);
                });
            (Channel<ImportProcessingProgress> progressChannel, Task importTask) = importer.Import(inputs, importErrorStore, CancellationToken.None);
            ImportProcessingProgress finalProgress = new ImportProcessingProgress();
            await foreach (ImportProcessingProgress progress in progressChannel.Reader.ReadAllAsync())
            {
                Assert.True(finalProgress.CurrentIndex <= progress.CurrentIndex);
                finalProgress = progress;
            }

            await importTask;

            Assert.Equal(expectedSucceedCount, finalProgress.SucceedImportCount);
            Assert.Equal(expectedFailedCount, finalProgress.FailedImportCount);
            Assert.Equal(expectedEndIndex, finalProgress.CurrentIndex);

            Assert.Equal(expectedSucceedCount, importedResources.Count);
            Assert.Equal(expectedSucceedCount, table1.Rows.Count);
            Assert.Equal(expectedSucceedCount * 2, table2.Rows.Count);
            Assert.Equal(expectedFailedCount, errorLogs.Count);
        }

        private static ResourceWrapper CreateResourceWrapper()
        {
            return new ResourceWrapper(
                        Guid.NewGuid().ToString(),
                        "0",
                        "Dummy",
                        new RawResource("Dummy", Fhir.Core.Models.FhirResourceFormat.Json, true),
                        new ResourceRequest("POST"),
                        DateTimeOffset.UtcNow,
                        false,
                        null,
                        null,
                        null,
                        "SearchParam");
        }

        private class TestDataGenerator : TableBulkCopyDataGenerator
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
