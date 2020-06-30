// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
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
        private readonly Lazy<CloudBlobClient> _blobClient;

        public AzureExportAnonymizerSettingsProvider(
            IExportClientInitializer<CloudBlobClient> exportClientInitializer,
            ILogger<AzureExportDestinationClient> logger)
        {
            EnsureArg.IsNotNull(exportClientInitializer, nameof(exportClientInitializer));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _exportClientInitializer = exportClientInitializer;
            _logger = logger;

            _blobClient = new Lazy<CloudBlobClient>(() => _exportClientInitializer.GetAuthorizedClientAsync(CancellationToken.None).Result);
        }

        public string GetAnonymizerSettings()
        {
            var container = _blobClient.Value.GetContainerReference("fhiranonymizer");
            var blob = container.GetBlobReference("config.json");
            using (MemoryStream stream = new MemoryStream())
            {
                blob.DownloadToStream(stream);
                stream.Position = 0;
                using (StreamReader reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
