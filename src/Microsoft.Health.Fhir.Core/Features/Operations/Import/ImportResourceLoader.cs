// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportResourceLoader : IImportResourceLoader
    {
        private const int DefaultChannelMaxCapacity = 500;
        private const int DefaultMaxBatchSize = 100;
        private static readonly int MaxConcurrentCount = Environment.ProcessorCount * 2;

        private IIntegrationDataStoreClient _integrationDataStoreClient;
        private IImportResourceParser _importResourceParser;
        private IImportErrorSerializer _importErrorSerializer;
        private ILogger<ImportResourceLoader> _logger;

        public ImportResourceLoader(
            IIntegrationDataStoreClient integrationDataStoreClient,
            IImportResourceParser importResourceParser,
            IImportErrorSerializer importErrorSerializer,
            ILogger<ImportResourceLoader> logger)
        {
            EnsureArg.IsNotNull(integrationDataStoreClient, nameof(integrationDataStoreClient));
            EnsureArg.IsNotNull(importResourceParser, nameof(importResourceParser));
            EnsureArg.IsNotNull(importErrorSerializer, nameof(importErrorSerializer));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _integrationDataStoreClient = integrationDataStoreClient;
            _importResourceParser = importResourceParser;
            _importErrorSerializer = importErrorSerializer;
            _logger = logger;
        }

        public int MaxBatchSize { get; set; } = DefaultMaxBatchSize;

        public int ChannelMaxCapacity { get; set; } = DefaultChannelMaxCapacity;

        public (Channel<ImportResource> resourceChannel, Task loadTask) LoadResources(string resourceLocation, long startIndex, string resourceType, Func<long, long> sequenceIdGenerator, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotEmptyOrWhiteSpace(resourceLocation, nameof(resourceLocation));

            Channel<ImportResource> outputChannel = Channel.CreateBounded<ImportResource>(ChannelMaxCapacity);

            Task loadTask = Task.Run(async () => await LoadResourcesInternalAsync(outputChannel, resourceLocation, startIndex, resourceType, sequenceIdGenerator, cancellationToken), cancellationToken);

            return (outputChannel, loadTask);
        }

        private async Task LoadResourcesInternalAsync(Channel<ImportResource> outputChannel, string resourceLocation, long startIndex, string resourceType, Func<long, long> sequenceIdGenerator, CancellationToken cancellationToken)
        {
            string leaseId = null;
            try
            {
                _logger.LogInformation("Start to load resource from store.");

                // Try to acquire lease to block change on the blob.
                leaseId = await _integrationDataStoreClient.TryAcquireLeaseAsync(new Uri(resourceLocation), Guid.NewGuid().ToString("N"), cancellationToken);

                using Stream inputDataStream = _integrationDataStoreClient.DownloadResource(new Uri(resourceLocation), 0, cancellationToken);
                using StreamReader inputDataReader = new StreamReader(inputDataStream);

                string content = null;
                long currentIndex = 0;
                List<(string content, long index)> buffer = new List<(string content, long index)>();
                Queue<Task<IEnumerable<ImportResource>>> processingTasks = new Queue<Task<IEnumerable<ImportResource>>>();

                while (!string.IsNullOrEmpty(content = await inputDataReader.ReadLineAsync()))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException();
                    }

                    // TODO: improve to load from offset in file
                    if (currentIndex < startIndex)
                    {
                        currentIndex++;
                        continue;
                    }

                    buffer.Add((content, currentIndex));
                    currentIndex++;

                    if (buffer.Count < MaxBatchSize)
                    {
                        continue;
                    }

                    while (processingTasks.Count >= MaxConcurrentCount)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException();
                        }

                        IEnumerable<ImportResource> importResources = await processingTasks.Dequeue();
                        foreach (ImportResource importResource in importResources)
                        {
                            await outputChannel.Writer.WriteAsync(importResource, cancellationToken);
                        }
                    }

                    processingTasks.Enqueue(ParseImportRawContentAsync(resourceType, buffer.ToArray(), sequenceIdGenerator));
                    buffer.Clear();
                }

                processingTasks.Enqueue(ParseImportRawContentAsync(resourceType, buffer.ToArray(), sequenceIdGenerator));
                while (processingTasks.Count > 0)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException();
                    }

                    IEnumerable<ImportResource> importResources = await processingTasks.Dequeue();
                    foreach (ImportResource importResource in importResources)
                    {
                        await outputChannel.Writer.WriteAsync(importResource, cancellationToken);
                    }
                }

                _logger.LogInformation("{CurrentIndex} lines loaded.", currentIndex);
            }
            finally
            {
                outputChannel.Writer.Complete();

                if (!string.IsNullOrEmpty(leaseId))
                {
                    await _integrationDataStoreClient.TryReleaseLeaseAsync(new Uri(resourceLocation), leaseId, cancellationToken);
                }

                _logger.LogInformation("Load resource from store complete.");
            }
        }

        private async Task<IEnumerable<ImportResource>> ParseImportRawContentAsync(string resourceType, (string content, long index)[] rawContents, Func<long, long> idGenerator)
        {
            return await Task.Run(() =>
            {
                List<ImportResource> result = new List<ImportResource>();

                foreach ((string content, long index) in rawContents)
                {
                    long id = idGenerator(index);

                    try
                    {
                        ImportResource importResource = _importResourceParser.Parse(id, index, content);

                        if (!string.IsNullOrEmpty(resourceType) && !resourceType.Equals(importResource.Resource?.ResourceTypeName, StringComparison.Ordinal))
                        {
                            throw new FormatException("Resource type not match.");
                        }

                        result.Add(importResource);
                    }
                    catch (Exception ex)
                    {
                        // May contains customer's data, no error logs here.
                        result.Add(new ImportResource(id, index, _importErrorSerializer.Serialize(index, ex)));
                    }
                }

                return result;
            });
        }
    }
}
