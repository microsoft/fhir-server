// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using System.Threading.Channels;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import;

namespace Microsoft.Health.Fhir.PostgresQL.Import
{
    public class PostgresQLResourceBulkImporter : IResourceBulkImporter
    {
        private ISqlBulkCopyDataWrapperFactory _sqlBulkCopyDataWrapperFactory;
        private PostgresQLImportOperation _postgresQLImportOperation;
        private readonly ImportTaskConfiguration _importTaskConfiguration;
        private IImportErrorSerializer _importErrorSerializer;
        private ILogger<PostgresQLResourceBulkImporter> _logger;

        public PostgresQLResourceBulkImporter(
            PostgresQLImportOperation postgresQLImportOperation,
            ISqlBulkCopyDataWrapperFactory sqlBulkCopyDataWrapperFactory,
            IImportErrorSerializer importErrorSerializer,
            IOptions<OperationsConfiguration> operationsConfig,
            ILogger<PostgresQLResourceBulkImporter> logger)
        {
            EnsureArg.IsNotNull(postgresQLImportOperation, nameof(postgresQLImportOperation));
            EnsureArg.IsNotNull(sqlBulkCopyDataWrapperFactory, nameof(sqlBulkCopyDataWrapperFactory));
            EnsureArg.IsNotNull(importErrorSerializer, nameof(importErrorSerializer));
            EnsureArg.IsNotNull(operationsConfig, nameof(operationsConfig));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _postgresQLImportOperation = postgresQLImportOperation;
            _sqlBulkCopyDataWrapperFactory = sqlBulkCopyDataWrapperFactory;
            _importErrorSerializer = importErrorSerializer;
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
                await _postgresQLImportOperation.CleanBatchResourceAsync(inputData.ResourceType, beginSequenceId + endIndex, endSequenceId, cancellationToken);
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

                Task<ImportProcessingProgress> checkpointTask = Task.FromResult<ImportProcessingProgress>(new ImportProcessingProgress());
                IEnumerable<SqlBulkCopyDataWrapper> mergedResources;
                long succeedCount = 0;
                long failedCount = 0;
                long? lastCheckpointIndex = null;
                long currentIndex = -1;
                Dictionary<string, DataTable> resourceParamsBuffer = new Dictionary<string, DataTable>();
                List<string> importErrorBuffer = new List<string>();
                Queue<Task<ImportProcessingProgress>> importTasks = new Queue<Task<ImportProcessingProgress>>();

                List<ImportResource> resourceBuffer = new List<ImportResource>();
                await _sqlBulkCopyDataWrapperFactory.EnsureInitializedAsync();
                await foreach (ImportResource resource in inputChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException();
                    }

                    lastCheckpointIndex = lastCheckpointIndex ?? resource.Index - 1;
                    currentIndex = resource.Index;

                    resourceBuffer.Add(resource);
                    if (resourceBuffer.Count < _importTaskConfiguration.SqlBatchSizeForImportResourceOperation)
                    {
                        continue;
                    }

                    try
                    {
                        // Handle resources in buffer
                        IEnumerable<ImportResource> resourcesWithError = resourceBuffer.Where(r => r.ContainsError());
                        IEnumerable<SqlBulkCopyDataWrapper> inputResources = resourceBuffer.Where(r => !r.ContainsError()).Select(r => _sqlBulkCopyDataWrapperFactory.CreateSqlBulkCopyDataWrapper(r));
                        mergedResources = await _postgresQLImportOperation.BulkMergeResourceAsync(inputResources, cancellationToken);

                        succeedCount += mergedResources.Count();
                        failedCount += resourcesWithError.Count();

                        _ = await EnqueueTaskAsync(
                                                    importTasks,
                                                    async () =>
                                                    {
                                                        await _postgresQLImportOperation.BulkCopyDataAsync(mergedResources, cancellationToken);

                                                        ImportProcessingProgress progress = new ImportProcessingProgress();
                                                        progress.SucceedImportCount = succeedCount;
                                                        progress.FailedImportCount = failedCount;
                                                        progress.CurrentIndex = currentIndex + 1;

                                                        // Return progress for checkpoint progress
                                                        return progress;
                                                    },
                                                    progressChannel: outputChannel);
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
                    // Handle resources in buffer
                    IEnumerable<ImportResource> resourcesWithError = resourceBuffer.Where(r => r.ContainsError());
                    IEnumerable<SqlBulkCopyDataWrapper> inputResources = resourceBuffer.Where(r => !r.ContainsError()).Select(r => _sqlBulkCopyDataWrapperFactory.CreateSqlBulkCopyDataWrapper(r));
                    mergedResources = await _postgresQLImportOperation.BulkMergeResourceAsync(inputResources, cancellationToken);

                    succeedCount += mergedResources.Count();
                    failedCount += resourcesWithError.Count();

                    _ = await EnqueueTaskAsync(
                                                    importTasks,
                                                    async () =>
                                                    {
                                                        await _postgresQLImportOperation.BulkCopyDataAsync(mergedResources, cancellationToken);

                                                        ImportProcessingProgress progress = new ImportProcessingProgress();
                                                        progress.SucceedImportCount = succeedCount;
                                                        progress.FailedImportCount = failedCount;
                                                        progress.CurrentIndex = currentIndex + 1;

                                                        // Return progress for checkpoint progress
                                                        return progress;
                                                    },
                                                    progressChannel: outputChannel);
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

                // Wait all table import task complete
                while (importTasks.Count > 0)
                {
                    await importTasks.Dequeue();
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
    }
}
