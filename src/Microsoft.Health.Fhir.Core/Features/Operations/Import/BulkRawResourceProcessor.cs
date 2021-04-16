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
    public class BulkRawResourceProcessor : IBulkRawResourceProcessor
    {
        internal const int DefaultMaxBatchSize = 1000;
        internal static readonly int MaxConcurrentCount = Environment.ProcessorCount * 2;

        private IBulkImportDataExtractor _bulkImportDataExtractor;

        public BulkRawResourceProcessor(IBulkImportDataExtractor bulkImportDataExtractor)
        {
            _bulkImportDataExtractor = bulkImportDataExtractor;
        }

        public int MaxBatchSize { get; set; } = DefaultMaxBatchSize;

        public async Task<long> ProcessingDataAsync(Channel<string> inputChannel, Channel<BulkImportResourceWrapper> outputChannel, long startSurrogateId, CancellationToken cancellationToken)
        {
            long result = 0;
            List<string> buffer = new List<string>();
            Queue<Task<IEnumerable<BulkImportResourceWrapper>>> processingTasks = new Queue<Task<IEnumerable<BulkImportResourceWrapper>>>();

            do
            {
                await foreach (string rawData in inputChannel.Reader.ReadAllAsync())
                {
                    buffer.Add(rawData);
                    if (buffer.Count < MaxBatchSize)
                    {
                        continue;
                    }

                    while (processingTasks.Count >= MaxConcurrentCount)
                    {
                        IEnumerable<BulkImportResourceWrapper> headTaskResults = await processingTasks.Dequeue();
                        foreach (BulkImportResourceWrapper resourceWrapper in headTaskResults)
                        {
                            Interlocked.Increment(ref result);
                            await outputChannel.Writer.WriteAsync(resourceWrapper);
                        }
                    }

                    string[] rawResources = buffer.ToArray();
                    buffer.Clear();
                    processingTasks.Enqueue(ProcessRawResources(rawResources, startSurrogateId));
                    startSurrogateId += rawResources.Length;
                }
            }
            while (await inputChannel.Reader.WaitToReadAsync() && !cancellationToken.IsCancellationRequested);

            if (!cancellationToken.IsCancellationRequested)
            {
                processingTasks.Enqueue(ProcessRawResources(buffer.ToArray(), startSurrogateId));
                while (processingTasks.Count > 0)
                {
                    IEnumerable<BulkImportResourceWrapper> headTaskResults = await processingTasks.Dequeue();
                    foreach (BulkImportResourceWrapper resourceWrapper in headTaskResults)
                    {
                        Interlocked.Increment(ref result);
                        await outputChannel.Writer.WriteAsync(resourceWrapper);
                    }
                }
            }

            outputChannel.Writer.Complete();
            return result;
        }

        private async Task<IEnumerable<BulkImportResourceWrapper>> ProcessRawResources(string[] rawResources, long startSurrogateId)
        {
            return await Task.Run(() =>
            {
                List<BulkImportResourceWrapper> result = new List<BulkImportResourceWrapper>();

                foreach (string rawData in rawResources)
                {
                    BulkImportResourceWrapper resourceWrapper = _bulkImportDataExtractor.GetBulkImportResourceWrapper(rawData);
                    resourceWrapper.ResourceSurrogateId = startSurrogateId++;
                    result.Add(resourceWrapper);
                }

                return result;
            });
        }
    }
}
