// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
        private const int DefaultMaxBatchSize = 1000;
        private static readonly int EndOfLineLength = Encoding.UTF8.GetByteCount(Environment.NewLine);

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
            _integrationDataStoreClient = EnsureArg.IsNotNull(integrationDataStoreClient, nameof(integrationDataStoreClient));
            _importResourceParser = EnsureArg.IsNotNull(importResourceParser, nameof(importResourceParser));
            _importErrorSerializer = EnsureArg.IsNotNull(importErrorSerializer, nameof(importErrorSerializer));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public int MaxBatchSize { get; set; } = DefaultMaxBatchSize;

        public int ChannelMaxCapacity { get; set; } = DefaultChannelMaxCapacity;

        public (Channel<ImportResource> resourceChannel, Task loadTask) LoadResources(string resourceLocation, long offset, int bytesToRead, string resourceType, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotEmptyOrWhiteSpace(resourceLocation, nameof(resourceLocation));

            var outputChannel = Channel.CreateBounded<ImportResource>(ChannelMaxCapacity);

            var loadTask = Task.Run(async () => await LoadResourcesInternalAsync(outputChannel, resourceLocation, offset, bytesToRead, resourceType, cancellationToken), cancellationToken);

            return (outputChannel, loadTask);
        }

        private async Task LoadResourcesInternalAsync(Channel<ImportResource> outputChannel, string resourceLocation, long offset, int bytesToRead, string resourceType, CancellationToken cancellationToken)
        {
            string leaseId = null;

            try
            {
                _logger.LogInformation("Start to load resource from store.");

                // Try to acquire lease to block change on the blob.
                leaseId = await _integrationDataStoreClient.TryAcquireLeaseAsync(new Uri(resourceLocation), Guid.NewGuid().ToString("N"), cancellationToken);

                using var stream = _integrationDataStoreClient.DownloadResource(new Uri(resourceLocation), offset, cancellationToken);
                using var reader = new StreamReader(stream);

                string content = null;
                long currentIndex = 0;
                long currentBytesRead = 0;
                var buffer = new List<(string content, long index, int length)>();

                var skipFirstLine = true;
#pragma warning disable CA2016
                while ((currentBytesRead <= bytesToRead) && !string.IsNullOrEmpty(content = await reader.ReadLineAsync()))
#pragma warning restore CA2016
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException();
                    }

                    if (offset > 0 && skipFirstLine) // skip first line
                    {
                        skipFirstLine = false;
                        continue;
                    }

                    var length = Encoding.UTF8.GetByteCount(content) + EndOfLineLength;
                    currentBytesRead += length;

                    currentIndex++;

                    buffer.Add((content, currentIndex, length));

                    if (buffer.Count < MaxBatchSize)
                    {
                        continue;
                    }

                    foreach (var importResource in await ParseImportRawContentAsync(resourceType, buffer, offset))
                    {
                        await outputChannel.Writer.WriteAsync(importResource, cancellationToken);
                    }
                }

                foreach (var importResource in await ParseImportRawContentAsync(resourceType, buffer, offset))
                {
                    await outputChannel.Writer.WriteAsync(importResource, cancellationToken);
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

        private async Task<IEnumerable<ImportResource>> ParseImportRawContentAsync(string resourceType, IList<(string content, long index, int length)> rawContents, long offset)
        {
            return await Task.Run(() =>
            {
                var result = new List<ImportResource>();

                foreach ((string content, long index, int length) in rawContents)
                {
                    try
                    {
                        ImportResource importResource = _importResourceParser.Parse(index, offset, length, content);

                        if (!string.IsNullOrEmpty(resourceType) && !resourceType.Equals(importResource.ResourceWrapper?.ResourceTypeName, StringComparison.Ordinal))
                        {
                            throw new FormatException("Resource type not match.");
                        }

                        result.Add(importResource);
                    }
                    catch (Exception ex)
                    {
                        // May contains customer's data, no error logs here.
                        result.Add(new ImportResource(index, offset, _importErrorSerializer.Serialize(index, ex, offset)));
                    }
                }

                rawContents.Clear();

                return result;
            });
        }
    }
}
