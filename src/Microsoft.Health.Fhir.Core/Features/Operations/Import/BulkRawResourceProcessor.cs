// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class BulkRawResourceProcessor : IBulkRawResourceProcessor
    {
        internal const int DefaultMaxBatchSize = 1000;
        internal static readonly int MaxConcurrentCount = Environment.ProcessorCount * 2;

        private IBulkImportDataExtractor _bulkImportDataExtractor;
        private ILogger<BulkRawResourceProcessor> _logger;

        public BulkRawResourceProcessor(IBulkImportDataExtractor bulkImportDataExtractor, ILogger<BulkRawResourceProcessor> logger)
        {
            _bulkImportDataExtractor = bulkImportDataExtractor;
            _logger = logger;
        }

        public int MaxBatchSize { get; set; } = DefaultMaxBatchSize;

        public async Task<long> ProcessingDataAsync(Channel<string> inputChannel, Channel<BulkImportResourceWrapper> outputChannel, Channel<BatchProcessErrorRecord> errorChannel, long startSurrogateId, CancellationToken cancellationToken)
        {
            long result = 0;
            List<string> buffer = new List<string>();
            Queue<Task<(IEnumerable<BulkImportResourceWrapper> result, IEnumerable<ProcessError> errors, long endSurrogateId)>> processingTasks = new Queue<Task<(IEnumerable<BulkImportResourceWrapper> result, IEnumerable<ProcessError> errors, long endSurrogateId)>>();

            long lineNumber = 0;
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
                        (IEnumerable<BulkImportResourceWrapper> resultResources, IEnumerable<ProcessError> errors, long endSurrogateId) = await processingTasks.Dequeue();
                        foreach (BulkImportResourceWrapper resourceWrapper in resultResources)
                        {
                            Interlocked.Increment(ref result);
                            await outputChannel.Writer.WriteAsync(resourceWrapper);
                        }

                        await errorChannel.Writer.WriteAsync(new BatchProcessErrorRecord(errors, endSurrogateId));
                    }

                    string[] rawResources = buffer.ToArray();
                    buffer.Clear();
                    processingTasks.Enqueue(ProcessRawResources(rawResources, startSurrogateId + lineNumber, lineNumber));
                    lineNumber += rawResources.Length;
                }
            }
            while (await inputChannel.Reader.WaitToReadAsync() && !cancellationToken.IsCancellationRequested);

            if (!cancellationToken.IsCancellationRequested)
            {
                processingTasks.Enqueue(ProcessRawResources(buffer.ToArray(), startSurrogateId + lineNumber, lineNumber));
                while (processingTasks.Count > 0)
                {
                    (IEnumerable<BulkImportResourceWrapper> resultResources, IEnumerable<ProcessError> errors, long endSurrogateId) = await processingTasks.Dequeue();
                    foreach (BulkImportResourceWrapper resourceWrapper in resultResources)
                    {
                        Interlocked.Increment(ref result);
                        await outputChannel.Writer.WriteAsync(resourceWrapper);
                    }

                    await errorChannel.Writer.WriteAsync(new BatchProcessErrorRecord(errors, endSurrogateId));
                }
            }

            outputChannel.Writer.Complete();
            errorChannel.Writer.Complete();
            return result;
        }

        private async Task<(IEnumerable<BulkImportResourceWrapper> resultResources, IEnumerable<ProcessError> errors, long endSurrogateId)> ProcessRawResources(string[] rawResources, long startSurrogateId, long lineNumber)
        {
            return await Task.Run(() =>
            {
                List<BulkImportResourceWrapper> result = new List<BulkImportResourceWrapper>();
                List<ProcessError> errors = new List<ProcessError>();

                long currentSurrogateId = startSurrogateId;
                foreach (string rawData in rawResources)
                {
                    try
                    {
                        BulkImportResourceWrapper resourceWrapper = _bulkImportDataExtractor.GetBulkImportResourceWrapper(rawData);
                        resourceWrapper.ResourceSurrogateId = currentSurrogateId;
                        result.Add(resourceWrapper);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new ProcessError(lineNumber, ex.Message));
                        _logger.LogDebug(ex, "failed to process resource at Line {0}", lineNumber);
                    }
                    finally
                    {
                        currentSurrogateId++;
                        lineNumber++;
                    }
                }

                return (result, errors, currentSurrogateId);
            });
        }
    }
}
