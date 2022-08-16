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
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import.DataGenerator;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Store.Sharding;
using Polly;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    internal class SqlResourceBulkImporter : IResourceBulkImporter
    {
        private List<TableBulkCopyDataGenerator> _generators = new List<TableBulkCopyDataGenerator>();
        private ISqlBulkCopyDataWrapperFactory _sqlBulkCopyDataWrapperFactory;
        private ISqlImportOperation _sqlImportOperation;
        private readonly ImportTaskConfiguration _importTaskConfiguration;
        private IImportErrorSerializer _importErrorSerializer;
        private ILogger<SqlResourceBulkImporter> _logger;

        public SqlResourceBulkImporter(
            ISqlImportOperation sqlImportOperation,
            ISqlBulkCopyDataWrapperFactory sqlBulkCopyDataWrapperFactory,
            IImportErrorSerializer importErrorSerializer,
            List<TableBulkCopyDataGenerator> generators,
            IOptions<OperationsConfiguration> operationsConfig,
            ILogger<SqlResourceBulkImporter> logger)
        {
            EnsureArg.IsNotNull(sqlImportOperation, nameof(sqlImportOperation));
            EnsureArg.IsNotNull(sqlBulkCopyDataWrapperFactory, nameof(sqlBulkCopyDataWrapperFactory));
            EnsureArg.IsNotNull(importErrorSerializer, nameof(importErrorSerializer));
            EnsureArg.IsNotNull(generators, nameof(generators));
            EnsureArg.IsNotNull(operationsConfig, nameof(operationsConfig));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlImportOperation = sqlImportOperation;
            _sqlBulkCopyDataWrapperFactory = sqlBulkCopyDataWrapperFactory;
            _importErrorSerializer = importErrorSerializer;
            _generators = generators;
            _importTaskConfiguration = operationsConfig.Value.Import;
            _logger = logger;
        }

        public SqlResourceBulkImporter(
            ISqlImportOperation sqlImportOperation,
            ISqlBulkCopyDataWrapperFactory sqlBulkCopyDataWrapperFactory,
            IImportErrorSerializer importErrorSerializer,
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
            UriSearchParamsTableBulkCopyDataGenerator uriSearchParamsTableBulkCopyDataGenerator,
            IOptions<OperationsConfiguration> operationsConfig,
            ILogger<SqlResourceBulkImporter> logger)
        {
            EnsureArg.IsNotNull(sqlImportOperation, nameof(sqlImportOperation));
            EnsureArg.IsNotNull(sqlBulkCopyDataWrapperFactory, nameof(sqlBulkCopyDataWrapperFactory));
            EnsureArg.IsNotNull(importErrorSerializer, nameof(importErrorSerializer));
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
            EnsureArg.IsNotNull(operationsConfig, nameof(operationsConfig));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlImportOperation = sqlImportOperation;
            _sqlBulkCopyDataWrapperFactory = sqlBulkCopyDataWrapperFactory;
            _importErrorSerializer = importErrorSerializer;

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

            _importTaskConfiguration = operationsConfig.Value.Import;
            _logger = logger;
        }

        public (Channel<ImportProcessingProgress> progressChannel, Task importTask) Import(Channel<ImportResource> inputChannel, IImportErrorStore importErrorStore, CancellationToken cancellationToken)
        {
            Channel<ImportProcessingProgress> outputChannel = Channel.CreateUnbounded<ImportProcessingProgress>();

            Task importTask = Task.Run(
                async () =>
                {
                    await ImportInternalAsync(inputChannel, outputChannel, importErrorStore, cancellationToken);
                },
                cancellationToken);

            return (outputChannel, importTask);
        }

        public async Task CleanResourceAsync(ImportProcessingJobInputData inputData, ImportProcessingJobResult result, CancellationToken cancellationToken)
        {
            long beginSequenceId = inputData.BeginSequenceId;
            long endSequenceId = inputData.EndSequenceId;
            long endIndex = result.CurrentIndex;

            try
            {
                await _sqlBulkCopyDataWrapperFactory.EnsureInitializedAsync();
                await _sqlImportOperation.CleanBatchResourceAsync(inputData.ResourceType, beginSequenceId + endIndex, endSequenceId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Failed to clean batch resource.");
                throw;
            }
        }

        private async Task ImportInternalAsync(Channel<ImportResource> inputChannel, Channel<ImportProcessingProgress> outputChannel, IImportErrorStore importErrorStore, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Start to import data to SQL data store.");

                long succeedCount = 0;
                long failedCount = 0;
                long currentIndex = -1;
                var importErrorBuffer = new List<string>();

                List<ImportResource> resourceBuffer = new List<ImportResource>();
                await _sqlBulkCopyDataWrapperFactory.EnsureInitializedAsync();
                await foreach (ImportResource resource in inputChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException();
                    }

                    currentIndex = resource.Index;

                    resourceBuffer.Add(resource);
                    if (resourceBuffer.Count < _importTaskConfiguration.SqlBatchSizeForImportResourceOperation)
                    {
                        continue;
                    }

                    try
                    {
                        var inputResources = resourceBuffer.Where(r => !r.ContainsError()).Select(r => _sqlBulkCopyDataWrapperFactory.CreateSqlBulkCopyDataWrapper(r)).ToList();
                        var mergedResources = ImportData(inputResources);

                        var resourcesWithError = resourceBuffer.Where(r => r.ContainsError());
                        importErrorBuffer.AddRange(resourcesWithError.Select(r => r.ImportError));
                        var duplicateResources = inputResources.Except(mergedResources);
                        AppendDuplicatedResouceErrorToBuffer(duplicateResources, importErrorBuffer);
                        succeedCount += mergedResources.Count();
                        failedCount += resourcesWithError.Count() + duplicateResources.Count();
                    }
                    finally
                    {
                        foreach (ImportResource importResource in resourceBuffer)
                        {
                            var stream = importResource?.CompressedStream;
                            if (stream != null)
                            {
                                await stream.DisposeAsync();
                            }
                        }

                        resourceBuffer.Clear();
                    }
                }

                try
                {
                    var inputResources = resourceBuffer.Where(r => !r.ContainsError()).Select(r => _sqlBulkCopyDataWrapperFactory.CreateSqlBulkCopyDataWrapper(r)).ToList();
                    var mergedResources = ImportDataSharded(inputResources);

                    var resourcesWithError = resourceBuffer.Where(r => r.ContainsError());
                    importErrorBuffer.AddRange(resourcesWithError.Select(r => r.ImportError));
                    var duplicateResources = inputResources.Except(mergedResources);
                    AppendDuplicatedResouceErrorToBuffer(duplicateResources, importErrorBuffer);
                    succeedCount += mergedResources.Count();
                    failedCount += resourcesWithError.Count() + duplicateResources.Count();
                }
                finally
                {
                    foreach (ImportResource importResource in resourceBuffer)
                    {
                        var stream = importResource?.CompressedStream;
                        if (stream != null)
                        {
                            await stream.DisposeAsync();
                        }
                    }

                    resourceBuffer.Clear();
                }

                // Upload remain error logs
                ImportProcessingProgress progress = await UploadImportErrorsAsync(importErrorStore, succeedCount, failedCount, importErrorBuffer.ToArray(), currentIndex, cancellationToken);
                await outputChannel.Writer.WriteAsync(progress, cancellationToken);
            }
            finally
            {
                outputChannel.Writer.Complete();
                _logger.LogInformation("Import data to SQL data store complete.");
            }
        }

        private IEnumerable<SqlBulkCopyDataWrapper> ImportData(IList<SqlBulkCopyDataWrapper> inputResources)
        {
            var mergedResources = _sqlImportOperation.BulkMergeResourceAsync(inputResources, CancellationToken.None).Result;
            var paramsTables = FillParamsDataTables(mergedResources.ToArray());
            ImportParamsDataTables(paramsTables);
            return mergedResources;
        }

        private List<DataTable> FillParamsDataTables(IList<SqlBulkCopyDataWrapper> mergedResources)
        {
            var tables = new List<DataTable>();
            foreach (var generator in _generators)
            {
                using var table = generator.GenerateDataTable();
                foreach (var resourceWrapper in mergedResources)
                {
                    generator.FillDataTable(table, resourceWrapper);
                }

                if (table.Rows.Count > 0)
                {
                    tables.Add(table);
                }
            }

            return tables;
        }

        private void AppendDuplicatedResouceErrorToBuffer(IEnumerable<SqlBulkCopyDataWrapper> mergedResources, List<string> importErrorBuffer)
        {
            foreach (SqlBulkCopyDataWrapper resourceWrapper in mergedResources)
            {
                importErrorBuffer.Add(_importErrorSerializer.Serialize(resourceWrapper.Index, string.Format(Resources.FailedToImportForDuplicatedResource, resourceWrapper.Resource.ResourceId, resourceWrapper.Index)));
            }
        }

        private async Task<ImportProcessingProgress> UploadImportErrorsAsync(IImportErrorStore importErrorStore, long succeedCount, long failedCount, string[] importErrors, long lastIndex, CancellationToken cancellationToken)
        {
            try
            {
                await importErrorStore.UploadErrorsAsync(importErrors, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Failed to upload error logs.");
                throw;
            }

            ImportProcessingProgress progress = new ImportProcessingProgress();
            progress.SucceedImportCount = succeedCount;
            progress.FailedImportCount = failedCount;
            progress.CurrentIndex = lastIndex + 1;

            // Return progress for checkpoint progress
            return progress;
        }

        private void ImportParamsDataTables(List<DataTable> tables)
        {
            foreach (var table in tables)
            {
                try
                {
                    _sqlImportOperation.BulkCopyDataAsync(table, CancellationToken.None).Wait();
                }
                catch (Exception ex)
                {
                    _logger.LogInformation(ex, "Failed to import table: {Table}", table.TableName);
                    throw;
                }
            }
        }

        private async Task<ImportProcessingProgress> ImportDataTableAsync(DataTable table, CancellationToken cancellationToken)
        {
            try
            {
                await Policy.Handle<SqlException>()
                    .WaitAndRetryAsync(
                        retryCount: 10,
                        sleepDurationProvider: (retryCount) => TimeSpan.FromSeconds(5 * (retryCount - 1)))
                    .ExecuteAsync(async () =>
                    {
                        await _sqlImportOperation.BulkCopyDataAsync(table, cancellationToken);
                    });

                // Return null for non checkpoint progress
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Failed to import table: {Table}", table.TableName);

                throw;
            }
        }

        private async Task<Task<ImportProcessingProgress>> EnqueueTaskAsync(Queue<Task<ImportProcessingProgress>> importTasks, Func<Task<ImportProcessingProgress>> newTaskFactory, Channel<ImportProcessingProgress> progressChannel)
        {
            while (importTasks.Count >= _importTaskConfiguration.SqlMaxImportOperationConcurrentCount)
            {
                ImportProcessingProgress progress = await importTasks.Dequeue();
                if (progress != null)
                {
                    await progressChannel.Writer.WriteAsync(progress);
                }
            }

            Task<ImportProcessingProgress> newTask = newTaskFactory();
            importTasks.Enqueue(newTask);

            return newTask;
        }

        private static Resource GetShardedResource(TransactionId transactionId, ShardletId shardletId, short sequence, SqlBulkCopyDataWrapper input)
        {
            var output = new Resource();
            output.ResourceTypeId = input.ResourceTypeId;
            output.ResourceId = input.Resource.ResourceId;
            output.Version = int.Parse(input.Resource.Version);
            output.IsHistory = input.Resource.IsHistory;
            output.TransactionId = transactionId;
            output.ShardletId = shardletId;
            output.Sequence = sequence;
            output.IsDeleted = input.Resource.IsDeleted;
            output.RequestMethod = input.Resource.Request.Method;
            output.SearchParamHash = input.Resource.SearchParameterHash;
            return output;
        }

        private static List<T> Convert<T>(TransactionId transactionId, Dictionary<long, (ShardletId ShardletId, short Sequence)> surrIdMap, DataTable dataTable, Func<TransactionId, ShardletId, short, DataRow, T> create)
        {
            List<T> searchParams = null;
            if (dataTable?.Rows.Count > 0)
            {
                searchParams = new List<T>();
                foreach (var row in dataTable.Rows)
                {
                    var dataRow = (DataRow)row;
                    (var shardletId, var sequence) = surrIdMap[(long)dataRow["ResourceSurrogateId"]];
                    searchParams.Add(create(transactionId, shardletId, sequence, dataRow));
                }
            }

            return searchParams;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1107:Code should not contain multiple statements on one line", Justification = "Short")]
        private IEnumerable<SqlBulkCopyDataWrapper> ImportDataSharded(IList<SqlBulkCopyDataWrapper> inputResources)
        {
            var paramsTables = FillParamsDataTables(inputResources);

            var transactionId = SqlImportOperation.SqlService.BeginTransaction();

            var shardletSequence = new Dictionary<ShardletId, short>();
            var surrIdMap = new Dictionary<long, (ShardletId ShardletId, short Sequence)>(); // map from surr id to shardlet resource index

            foreach (var resource in inputResources)
            {
                var shardletId = ShardletId.GetHashedShardletId(resource.Resource.ResourceId);
                if (!shardletSequence.TryGetValue(shardletId, out var sequence))
                {
                    shardletSequence.Add(shardletId, 0);
                }
                else
                {
                    sequence++;
                    shardletSequence[shardletId] = sequence;
                }

                if (!surrIdMap.ContainsKey(resource.ResourceSurrogateId))
                {
                    surrIdMap.Add(resource.ResourceSurrogateId, (shardletId, sequence));
                }
            }

            var resources = inputResources.Select(_ => { (var shardletId, var sequence) = surrIdMap[_.ResourceSurrogateId]; return GetShardedResource(transactionId, shardletId, sequence, _); }).ToList();
            var referenceSearchParams = Convert(transactionId, surrIdMap, paramsTables.FirstOrDefault(_ => _.TableName == "dbo.ReferenceSearchParam"), (transactionId, shardletId, sequence, dataRow) => new ReferenceSearchParam(transactionId, shardletId, sequence, dataRow));
            var tokenSearchParams = Convert(transactionId, surrIdMap, paramsTables.FirstOrDefault(_ => _.TableName == "dbo.TokenSearchParam"), (transactionId, shardletId, sequence, dataRow) => new TokenSearchParam(transactionId, shardletId, sequence, dataRow));
            var compartmentAssignments = Convert(transactionId, surrIdMap, paramsTables.FirstOrDefault(_ => _.TableName == "dbo.CompartmentAssignment"), (transactionId, shardletId, sequence, dataRow) => new CompartmentAssignment(transactionId, shardletId, sequence, dataRow));
            var tokenTexts = Convert(transactionId, surrIdMap, paramsTables.FirstOrDefault(_ => _.TableName == "dbo.TokenText"), (transactionId, shardletId, sequence, dataRow) => new TokenText(transactionId, shardletId, sequence, dataRow));
            var dateTimeSearchParams = Convert(transactionId, surrIdMap, paramsTables.FirstOrDefault(_ => _.TableName == "dbo.DateTimeSearchParam"), (transactionId, shardletId, sequence, dataRow) => new DateTimeSearchParam(transactionId, shardletId, sequence, dataRow));
            var tokenQuantityCompositeSearchParams = Convert(transactionId, surrIdMap, paramsTables.FirstOrDefault(_ => _.TableName == "dbo.TokenQuantityCompositeSearchParam"), (transactionId, shardletId, sequence, dataRow) => new TokenQuantityCompositeSearchParam(transactionId, shardletId, sequence, dataRow));
            var quantitySearchParams = Convert(transactionId, surrIdMap, paramsTables.FirstOrDefault(_ => _.TableName == "dbo.QuantitySearchParam"), (transactionId, shardletId, sequence, dataRow) => new QuantitySearchParam(transactionId, shardletId, sequence, dataRow));
            var stringSearchParams = Convert(transactionId, surrIdMap, paramsTables.FirstOrDefault(_ => _.TableName == "dbo.StringSearchParam"), (transactionId, shardletId, sequence, dataRow) => new StringSearchParam(transactionId, shardletId, sequence, dataRow));
            var tokenTokenCompositeSearchParams = Convert(transactionId, surrIdMap, paramsTables.FirstOrDefault(_ => _.TableName == "dbo.TokenTokenCompositeSearchParam"), (transactionId, shardletId, sequence, dataRow) => new TokenTokenCompositeSearchParam(transactionId, shardletId, sequence, dataRow));
            var tokenStringCompositeSearchParams = Convert(transactionId, surrIdMap, paramsTables.FirstOrDefault(_ => _.TableName == "dbo.TokenStringCompositeSearchParam"), (transactionId, shardletId, sequence, dataRow) => new TokenStringCompositeSearchParam(transactionId, shardletId, sequence, dataRow));

            var rows = SqlImportOperation.SqlService.MergeResources(transactionId, resources, referenceSearchParams, tokenSearchParams, compartmentAssignments, tokenTexts, dateTimeSearchParams, tokenQuantityCompositeSearchParams, quantitySearchParams, stringSearchParams, tokenTokenCompositeSearchParams, tokenStringCompositeSearchParams);

            SqlImportOperation.SqlService.CommitTransaction(transactionId);

            return inputResources;
        }
    }
}
