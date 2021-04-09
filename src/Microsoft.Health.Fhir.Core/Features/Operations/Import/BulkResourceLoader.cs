// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class BulkResourceLoader
    {
        private IIntegrationDataStoreClient _integrationDataStoreClient;
        private ILogger<BulkResourceLoader> _logger;

        public BulkResourceLoader(IIntegrationDataStoreClient integrationDataStoreClient, ILogger<BulkResourceLoader> logger)
        {
            _integrationDataStoreClient = integrationDataStoreClient;
            _logger = logger;
        }

        public async Task LoadToChannelAsync(Channel<string> outputChannel, Uri resourceUri, long startLineOffset, CancellationToken cancellationToken)
        {
            using Stream inputDataStream = _integrationDataStoreClient.DownloadResource(resourceUri, 0, cancellationToken);
            using StreamReader inputDataReader = new StreamReader(inputDataStream);

            string content = null;
            long loadedLines = 0;
            long currentLine = 0;
            while (!cancellationToken.IsCancellationRequested && !string.IsNullOrEmpty(content = await inputDataReader.ReadLineAsync()))
            {
                // TODO: improve to load from offset in file
                if (currentLine++ < startLineOffset)
                {
                    continue;
                }

                await outputChannel.Writer.WriteAsync(content);
                Interlocked.Increment(ref loadedLines);
            }

            outputChannel.Writer.Complete();
            _logger.LogInformation($"{loadedLines} lines loaded.");
        }
    }
}
