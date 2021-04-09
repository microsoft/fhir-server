// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class BulkRawResourceProcessor
    {
        internal const int MaxBatchSize = 1000;
        internal static readonly int MaxConcurrentCount = Environment.ProcessorCount * 2;

        private IBulkImportDataExtractor _bulkImportDataExtractor;

        public BulkRawResourceProcessor(IBulkImportDataExtractor bulkImportDataExtractor)
        {
            _bulkImportDataExtractor = bulkImportDataExtractor;
        }

        public async Task ProcessingDataAsync(Channel<string> inputChannel, Channel<BulkImportResourceWrapper> outputChannel, CancellationToken cancellationToken)
        {
            List<string> buffer = new List<string>();
            Queue<Task<IEnumerable<BulkImportResourceWrapper>>> processingTasks = new Queue<Task<IEnumerable<BulkImportResourceWrapper>>>();

            while (await inputChannel.Reader.WaitToReadAsync() && !cancellationToken.IsCancellationRequested)
            {
                await foreach (string content in inputChannel.Reader.ReadAllAsync())
                {
                    buffer.Add(content);
                    if (buffer.Count < MaxBatchSize)
                    {
                        continue;
                    }

                    while (processingTasks.Count >= MaxConcurrentCount)
                    {
                        IEnumerable<BulkImportResourceWrapper> headTaskResults = await processingTasks.Dequeue();
                        foreach (BulkImportResourceWrapper resourceWrapper in headTaskResults)
                        {
                            await outputChannel.Writer.WriteAsync(resourceWrapper);
                        }
                    }

                    string[] rawResources = buffer.ToArray();
                    buffer.Clear();
                    processingTasks.Enqueue(ProcessRawResources(rawResources));
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    processingTasks.Enqueue(ProcessRawResources(buffer.ToArray()));
                    while (processingTasks.Count > 0)
                    {
                        IEnumerable<BulkImportResourceWrapper> headTaskResults = await processingTasks.Dequeue();
                        foreach (BulkImportResourceWrapper resourceWrapper in headTaskResults)
                        {
                            await outputChannel.Writer.WriteAsync(resourceWrapper);
                        }
                    }
                }

                outputChannel.Writer.Complete();
            }
        }

        private async Task<IEnumerable<BulkImportResourceWrapper>> ProcessRawResources(string[] rawResources)
        {
            return await Task.Run(() =>
            {
                List<BulkImportResourceWrapper> result = new List<BulkImportResourceWrapper>();

                foreach (string content in rawResources)
                {
                    result.Add(_bulkImportDataExtractor.GetBulkImportResourceWrapper(content));
                }

                return result;
            });
        }
    }
}
