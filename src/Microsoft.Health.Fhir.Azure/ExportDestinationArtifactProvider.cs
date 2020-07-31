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
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;

namespace Microsoft.Health.Fhir.Azure
{
    public class ExportDestinationArtifactProvider : IArtifactProvider
    {
        private IExportClientInitializer<CloudBlobClient> _exportClientInitializer;
        private ExportJobConfiguration _exportJobConfiguration;
        private CloudBlobClient _blobClient;

        public ExportDestinationArtifactProvider(
            IExportClientInitializer<CloudBlobClient> exportClientInitializer,
            IOptions<ExportJobConfiguration> exportJobConfiguration)
        {
            _exportClientInitializer = exportClientInitializer;
            _exportJobConfiguration = exportJobConfiguration.Value;
        }

        public async Task FetchArtifactAsync(string blobUriString, Stream targetStream, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(blobUriString, nameof(blobUriString));

            CloudBlobClient blobClient = await ConnectAsync(cancellationToken);
            var blob = new CloudBlockBlob(new Uri(blobUriString), blobClient);
            if (await blob.ExistsAsync(cancellationToken))
            {
                await blob.DownloadToStreamAsync(targetStream, cancellationToken);
            }
            else
            {
                throw new FileNotFoundException(message: $"File not found on the destination storage. {blobUriString}");
            }
        }

        private async Task<CloudBlobClient> ConnectAsync(CancellationToken cancellationToken)
        {
            if (_blobClient == null)
            {
                _blobClient = await _exportClientInitializer.GetAuthorizedClientAsync(_exportJobConfiguration, cancellationToken);
            }

            return _blobClient;
        }
    }
}
