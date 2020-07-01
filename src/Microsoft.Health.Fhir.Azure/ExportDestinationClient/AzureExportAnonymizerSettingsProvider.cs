// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;

namespace Microsoft.Health.Fhir.Azure.ExportDestinationClient
{
    public class AzureExportAnonymizerSettingsProvider : IAnonymizerSettingsProvider
    {
        private readonly IExportClientInitializer<CloudBlobClient> _exportClientInitializer;
        private readonly ILogger<AzureExportDestinationClient> _logger;

        public AzureExportAnonymizerSettingsProvider(
            IExportClientInitializer<CloudBlobClient> exportClientInitializer,
            ILogger<AzureExportDestinationClient> logger)
        {
            EnsureArg.IsNotNull(exportClientInitializer, nameof(exportClientInitializer));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _exportClientInitializer = exportClientInitializer;
            _logger = logger;
        }

        public async Task<string> GetAnonymizerSettingsAsync()
        {
            var blobClient = await _exportClientInitializer.GetAuthorizedClientAsync(CancellationToken.None);
            var container = blobClient.GetContainerReference("anonymization");
            var blob = container.GetBlobReference("config.json");

            if (await blob.ExistsAsync())
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    await blob.DownloadToStreamAsync(stream);
                    stream.Position = 0;
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        return await reader.ReadToEndAsync();
                    }
                }
            }
            else
            {
                _logger.LogInformation("Anonymization config not found.");
                return null;
            }
        }
    }
}
