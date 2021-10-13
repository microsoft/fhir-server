// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Import.Core;

namespace Microsoft.Health.Fhir.Import.DataStore.CosmosDb
{
    public class CosmosDbImporter : IResourceBulkImporter
    {
        private ImportTaskConfiguration _importTaskConfiguration;
        private IFhirDataStore _fhirDataStore;

        public CosmosDbImporter(
            IOptions<ImportTaskConfiguration> importTaskConfiguration,
            IFhirDataStore fhirDataStore)
        {
            _importTaskConfiguration = importTaskConfiguration.Value;
            _fhirDataStore = fhirDataStore;
        }

        public Task CleanResourceAsync(ImportProcessingTaskInputData inputData, ImportProcessingProgress progress, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public (Channel<ImportProcessingProgress> progressChannel, Task importTask) Import(Channel<ImportResource> inputChannel, IImportErrorStore importErrorStore, CancellationToken cancellationToken)
        {
            Channel<ImportProcessingProgress> outputChannel = Channel.CreateUnbounded<ImportProcessingProgress>();
            Task importTask = Task.Run(
                async () =>
                {
                    await ImportInternalAsync(inputChannel, outputChannel, cancellationToken);
                },
                cancellationToken);

            return (outputChannel, importTask);
        }

        private async Task ImportInternalAsync(Channel<ImportResource> inputChannel, Channel<ImportProcessingProgress> outputChannel, CancellationToken cancellationToken)
        {
            try
            {
                Queue<Task<(int succeedCount, int failedCount)>> importTasks = new Queue<Task<(int succeedCount, int failedCount)>>();
                List<ImportResource> resourceBuffer = new List<ImportResource>();

                int totalSucceedCount = 0;
                int totalFailedCount = 0;
                await foreach (ImportResource resource in inputChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException();
                    }

                    resourceBuffer.Add(resource);
                    if (resourceBuffer.Count < _importTaskConfiguration.ImporterBatchSize)
                    {
                        continue;
                    }

                    while (importTasks.Count >= _importTaskConfiguration.ImporterConcurrentCount)
                    {
                        (int succeedCount, int failedCount) = await importTasks.Dequeue();
                        totalSucceedCount += succeedCount;
                        totalFailedCount += failedCount;
                        await outputChannel.Writer.WriteAsync(new ImportProcessingProgress() { SucceedImportCount = totalSucceedCount, FailedImportCount = totalFailedCount }, cancellationToken);
                    }

                    importTasks.Enqueue(ImportBatchResourceAsync(resourceBuffer.ToArray(), cancellationToken));
                    resourceBuffer.Clear();
                }

                importTasks.Enqueue(ImportBatchResourceAsync(resourceBuffer.ToArray(), cancellationToken));

                while (importTasks.Count > 0)
                {
                    (int succeedCount, int failedCount) = await importTasks.Dequeue();
                    totalSucceedCount += succeedCount;
                    totalFailedCount += failedCount;
                    await outputChannel.Writer.WriteAsync(new ImportProcessingProgress() { SucceedImportCount = totalSucceedCount, FailedImportCount = totalFailedCount }, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _ = ex.Message.Trim();
            }
            finally
            {
                outputChannel.Writer.Complete();
            }
        }

        private async Task<(int succeedCount, int failedCount)> ImportBatchResourceAsync(ImportResource[] resources, CancellationToken cancellationToken)
        {
            int succeed = 0;
            int failed = 0;

            foreach (ImportResource resource in resources)
            {
                if (resource.ImportError == null)
                {
                    UpsertOutcome upsertOutcome = await _fhirDataStore.UpsertAsync(resource.Resource, null, true, true, cancellationToken);
                    if (upsertOutcome.OutcomeType == SaveOutcomeType.Created)
                    {
                        succeed++;
                    }
                    else
                    {
                        failed++;
                    }
                }
                else
                {
                    failed++;
                }
            }

            return (succeed, failed);
        }
    }
}
