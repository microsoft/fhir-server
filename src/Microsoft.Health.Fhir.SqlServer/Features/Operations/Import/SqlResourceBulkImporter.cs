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
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import.DataGenerator;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    internal class SqlResourceBulkImporter : IResourceBulkImporter
    {
        private const int DefaultCheckpointBatchResourceCount = 30000;
        private const int DefaultMaxResourceCountInBatch = 10000;
        private const int DefaultMaxConcurrentCount = 3;

        private List<TableBulkCopyDataGenerator<SqlBulkCopyDataWrapper>> _generators = new List<TableBulkCopyDataGenerator<SqlBulkCopyDataWrapper>>();
        private ISqlBulkCopyDataWrapperFactory _sqlBulkCopyDataWrapperFactory;
        private IFhirDataBulkOperation _fhirDataBulkOperation;

        public SqlResourceBulkImporter(
            IFhirDataBulkOperation fhirDataBulkOperation,
            ISqlBulkCopyDataWrapperFactory sqlBulkCopyDataWrapperFactory,
            List<TableBulkCopyDataGenerator<SqlBulkCopyDataWrapper>> generators)
        {
            EnsureArg.IsNotNull(fhirDataBulkOperation, nameof(fhirDataBulkOperation));
            EnsureArg.IsNotNull(sqlBulkCopyDataWrapperFactory, nameof(sqlBulkCopyDataWrapperFactory));
            EnsureArg.IsNotNull(generators, nameof(generators));

            _fhirDataBulkOperation = fhirDataBulkOperation;
            _sqlBulkCopyDataWrapperFactory = sqlBulkCopyDataWrapperFactory;
            _generators = generators;
        }

        public SqlResourceBulkImporter(
            IFhirDataBulkOperation fhirDataBulkOperation,
            ISqlBulkCopyDataWrapperFactory sqlBulkCopyDataWrapperFactory,
            ResourceTableBulkCopyDataGenerator resourceTableBulkCopyDataGenerator,
            CompartmentAssignmentTableBulkCopyDataGenerator compartmentAssignmentTableBulkCopyDataGenerator,
            ResourceWriteClaimTableBulkCopyDataGenerator resourceWriteClaimTableBulkCopyDataGenerator,
            DateTimeSearchParamsTableBulkCopyDataGenerator dateTimeSearchParamsTableBulkCopyDataGenerator,
            NumberSearchParamsTableBulkCopyDataGenerator numberSearchParamsTableBulkCopyDataGenerator,
            QuantitySearchParamsTableBulkCopyDataGenerator quantitySearchParamsTableBulkCopyDataGenerator,
            ReferenceSearchParamsTableBulkCopyDataGenerator referenceSearchParamsTableBulkCopyDataGenerator,
            ReferenceTokenCompositeSearchParamsTableBulkCopyDataGenerator referenceTokenCompositeSearchParamsTableBulkCopyDataGenerator,
            StringSearchParamsTableBulkCopyDataGenerator stringSearchParamsTableBulkCopyDataGenerator,
            TokenDateTimeCompositeSearchParamsTableBulkCopyDataGenerator tokenDateTimeCompositeSearchParamsTableBulkCopyDataGenerator,
            TokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator tokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator,
            TokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator tokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator,
            TokenSearchParamsTableBulkCopyDataGenerator tokenSearchParamsTableBulkCopyDataGenerator,
            TokenStringCompositeSearchParamsTableBulkCopyDataGenerator tokenStringCompositeSearchParamsTableBulkCopyDataGenerator,
            TokenTextSearchParamsTableBulkCopyDataGenerator tokenTextSearchParamsTableBulkCopyDataGenerator,
            TokenTokenCompositeSearchParamsTableBulkCopyDataGenerator tokenTokenCompositeSearchParamsTableBulkCopyDataGenerator,
            UriSearchParamsTableBulkCopyDataGenerator uriSearchParamsTableBulkCopyDataGenerator)
        {
            EnsureArg.IsNotNull(fhirDataBulkOperation, nameof(fhirDataBulkOperation));
            EnsureArg.IsNotNull(sqlBulkCopyDataWrapperFactory, nameof(sqlBulkCopyDataWrapperFactory));
            EnsureArg.IsNotNull(resourceTableBulkCopyDataGenerator, nameof(resourceTableBulkCopyDataGenerator));
            EnsureArg.IsNotNull(compartmentAssignmentTableBulkCopyDataGenerator, nameof(compartmentAssignmentTableBulkCopyDataGenerator));
            EnsureArg.IsNotNull(resourceWriteClaimTableBulkCopyDataGenerator, nameof(resourceWriteClaimTableBulkCopyDataGenerator));
            EnsureArg.IsNotNull(dateTimeSearchParamsTableBulkCopyDataGenerator, nameof(dateTimeSearchParamsTableBulkCopyDataGenerator));
            EnsureArg.IsNotNull(numberSearchParamsTableBulkCopyDataGenerator, nameof(numberSearchParamsTableBulkCopyDataGenerator));
            EnsureArg.IsNotNull(quantitySearchParamsTableBulkCopyDataGenerator, nameof(quantitySearchParamsTableBulkCopyDataGenerator));
            EnsureArg.IsNotNull(referenceSearchParamsTableBulkCopyDataGenerator, nameof(referenceSearchParamsTableBulkCopyDataGenerator));
            EnsureArg.IsNotNull(referenceTokenCompositeSearchParamsTableBulkCopyDataGenerator, nameof(referenceTokenCompositeSearchParamsTableBulkCopyDataGenerator));
            EnsureArg.IsNotNull(stringSearchParamsTableBulkCopyDataGenerator, nameof(stringSearchParamsTableBulkCopyDataGenerator));
            EnsureArg.IsNotNull(tokenDateTimeCompositeSearchParamsTableBulkCopyDataGenerator, nameof(tokenDateTimeCompositeSearchParamsTableBulkCopyDataGenerator));
            EnsureArg.IsNotNull(tokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator, nameof(tokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator));
            EnsureArg.IsNotNull(tokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator, nameof(tokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator));
            EnsureArg.IsNotNull(tokenSearchParamsTableBulkCopyDataGenerator, nameof(tokenSearchParamsTableBulkCopyDataGenerator));
            EnsureArg.IsNotNull(tokenStringCompositeSearchParamsTableBulkCopyDataGenerator, nameof(tokenStringCompositeSearchParamsTableBulkCopyDataGenerator));
            EnsureArg.IsNotNull(tokenTextSearchParamsTableBulkCopyDataGenerator, nameof(tokenTextSearchParamsTableBulkCopyDataGenerator));
            EnsureArg.IsNotNull(tokenTokenCompositeSearchParamsTableBulkCopyDataGenerator, nameof(tokenTokenCompositeSearchParamsTableBulkCopyDataGenerator));
            EnsureArg.IsNotNull(uriSearchParamsTableBulkCopyDataGenerator, nameof(uriSearchParamsTableBulkCopyDataGenerator));

            _fhirDataBulkOperation = fhirDataBulkOperation;
            _sqlBulkCopyDataWrapperFactory = sqlBulkCopyDataWrapperFactory;

            _generators.Add(resourceTableBulkCopyDataGenerator);
            _generators.Add(compartmentAssignmentTableBulkCopyDataGenerator);
            _generators.Add(resourceWriteClaimTableBulkCopyDataGenerator);
            _generators.Add(dateTimeSearchParamsTableBulkCopyDataGenerator);
            _generators.Add(numberSearchParamsTableBulkCopyDataGenerator);
            _generators.Add(quantitySearchParamsTableBulkCopyDataGenerator);
            _generators.Add(referenceSearchParamsTableBulkCopyDataGenerator);
            _generators.Add(referenceTokenCompositeSearchParamsTableBulkCopyDataGenerator);
            _generators.Add(stringSearchParamsTableBulkCopyDataGenerator);
            _generators.Add(tokenDateTimeCompositeSearchParamsTableBulkCopyDataGenerator);
            _generators.Add(tokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator);
            _generators.Add(tokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator);
            _generators.Add(tokenSearchParamsTableBulkCopyDataGenerator);
            _generators.Add(tokenStringCompositeSearchParamsTableBulkCopyDataGenerator);
            _generators.Add(tokenTextSearchParamsTableBulkCopyDataGenerator);
            _generators.Add(tokenTokenCompositeSearchParamsTableBulkCopyDataGenerator);
            _generators.Add(uriSearchParamsTableBulkCopyDataGenerator);
        }

        public int MaxResourceCountInBatch { get; set; } = DefaultMaxResourceCountInBatch;

        public int MaxConcurrentCount { get; set; } = DefaultMaxConcurrentCount;

        public int CheckpointBatchResourceCount { get; set; } = DefaultCheckpointBatchResourceCount;

        public Channel<ImportProgress> Import(Channel<ImportResource> inputChannel, IImportErrorStore importErrorStore, CancellationToken cancellationToken)
        {
            Channel<ImportProgress> outputChannel = Channel.CreateUnbounded<ImportProgress>();

            Task.Run(async () =>
            {
                await ImportInternalAsync(inputChannel, outputChannel, importErrorStore, cancellationToken);
            });

            return outputChannel;
        }

        public async Task ImportInternalAsync(Channel<ImportResource> inputChannel, Channel<ImportProgress> outputChannel, IImportErrorStore importErrorStore, CancellationToken cancellationToken)
        {
            Task checkpointTask = Task.CompletedTask;

            long succeedCount = 0;
            long failedCount = 0;
            long lastCheckpointIndex = -1;
            long currentIndex = -1;
            Dictionary<string, DataTable> resourceBuffer = new Dictionary<string, DataTable>();
            List<string> importErrorBuffer = new List<string>();
            Queue<Task<ImportProgress>> importTasks = new Queue<Task<ImportProgress>>();

            await foreach (ImportResource resource in inputChannel.Reader.ReadAllAsync())
            {
                currentIndex = resource.Index;

                if (resource.ImportError != null)
                {
                    importErrorBuffer.Add(resource.ImportError);
                    failedCount++;
                }
                else
                {
                    SqlBulkCopyDataWrapper dataWrapper = _sqlBulkCopyDataWrapperFactory.CreateSqlBulkCopyDataWrapper(resource);

                    foreach (TableBulkCopyDataGenerator<SqlBulkCopyDataWrapper> generator in _generators)
                    {
                        if (!resourceBuffer.ContainsKey(generator.TableName))
                        {
                            resourceBuffer[generator.TableName] = generator.GenerateDataTable();
                        }

                        generator.FillDataTable(resourceBuffer[generator.TableName], dataWrapper);
                    }

                    succeedCount++;
                }

                bool shouldCreateCheckpoint = resource.Index - lastCheckpointIndex >= CheckpointBatchResourceCount;
                if (shouldCreateCheckpoint)
                {
                    string[] tableNameNeedImport = resourceBuffer.Keys.ToArray();

                    foreach (string tableName in tableNameNeedImport)
                    {
                        DataTable dataTable = resourceBuffer[tableName];
                        resourceBuffer.Remove(tableName);
                        await EnqueueTaskAsync(importTasks, () => ImportDataTableAsync(dataTable, cancellationToken), outputChannel);
                    }

                    string[] importErrors = importErrorBuffer.ToArray();
                    importErrorBuffer.Clear();
                    await EnqueueTaskAsync(importTasks, () => UploadImportErrorsAsync(importErrorStore, succeedCount, failedCount, importErrors, currentIndex, cancellationToken), outputChannel);
                }
                else
                {
                    string[] tableNameNeedImport =
                                resourceBuffer.Where(r => r.Value.Rows.Count >= MaxResourceCountInBatch).Select(r => r.Key).ToArray();

                    foreach (string tableName in tableNameNeedImport)
                    {
                        DataTable dataTable = resourceBuffer[tableName];
                        resourceBuffer.Remove(tableName);
                        await EnqueueTaskAsync(importTasks, () => ImportDataTableAsync(dataTable, cancellationToken), outputChannel);
                    }
                }
            }

            foreach (string tableName in resourceBuffer.Keys.ToArray())
            {
                DataTable dataTable = resourceBuffer[tableName];
                await EnqueueTaskAsync(importTasks, () => ImportDataTableAsync(dataTable, cancellationToken), outputChannel);
            }

            while (importTasks.Count > 0)
            {
                await importTasks.Dequeue();
            }

            ImportProgress progress = await UploadImportErrorsAsync(importErrorStore, succeedCount, failedCount, importErrorBuffer.ToArray(), currentIndex, cancellationToken);
            await outputChannel.Writer.WriteAsync(progress);
            outputChannel.Writer.Complete();
        }

        private async Task<ImportProgress> UploadImportErrorsAsync(IImportErrorStore importErrorStore, long succeedCount, long failedCount, string[] importErrors, long lastIndex, CancellationToken cancellationToken)
        {
            await importErrorStore.UploadErrorsAsync(importErrors, cancellationToken);
            ImportProgress progress = new ImportProgress();
            progress.SucceedImportCount = succeedCount;
            progress.FailedImportCount = failedCount;
            progress.EndIndex = lastIndex + 1;

            return progress;
        }

        private async Task<ImportProgress> ImportDataTableAsync(DataTable table, CancellationToken cancellationToken)
        {
            await _fhirDataBulkOperation.BulkCopyDataAsync(table, cancellationToken);

            return null;
        }

        private async Task EnqueueTaskAsync(Queue<Task<ImportProgress>> importTasks, Func<Task<ImportProgress>> newTaskFactory, Channel<ImportProgress> progressChannel)
        {
            while (importTasks.Count >= MaxConcurrentCount)
            {
                ImportProgress progress = await importTasks.Dequeue();
                if (progress != null)
                {
                    await progressChannel.Writer.WriteAsync(progress);
                }
            }

            importTasks.Enqueue(newTaskFactory());
        }
    }
}
