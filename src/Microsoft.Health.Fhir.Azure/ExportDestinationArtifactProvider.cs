// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;

namespace Microsoft.Health.Fhir.Azure
{
    public class ExportDestinationArtifactProvider : IArtifactProvider
    {
        private const string AnonymizationContainer = "anonymization";

        private IExportClientInitializer<BlobServiceClient> _exportClientInitializer;
        private ExportJobConfiguration _exportJobConfiguration;
        private BlobServiceClient _blobClient;

        public ExportDestinationArtifactProvider(
            IExportClientInitializer<BlobServiceClient> exportClientInitializer,
            IOptions<ExportJobConfiguration> exportJobConfiguration)
        {
            EnsureArg.IsNotNull(exportClientInitializer, nameof(exportClientInitializer));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));

            _exportClientInitializer = exportClientInitializer;
            _exportJobConfiguration = exportJobConfiguration.Value;
        }

        public async Task FetchAsync(string blobNameWithETag, Stream targetStream, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrEmpty(blobNameWithETag, nameof(blobNameWithETag));
            EnsureArg.IsNotNull(targetStream, nameof(targetStream));

            string[] blobLocation = blobNameWithETag.Split(':', StringSplitOptions.RemoveEmptyEntries);
            string blobName = blobLocation[0];
            string eTag = blobLocation.Count() > 1 ? blobLocation[1] : null;
            eTag = AddDoubleQuotesIfMissing(eTag);

            BlobServiceClient blobClient = await ConnectAsync(cancellationToken);
            BlobContainerClient container = blobClient.GetBlobContainerClient(AnonymizationContainer);
            if (!await container.ExistsAsync(cancellationToken))
            {
                throw new FileNotFoundException(message: Resources.AnonymizationContainerNotFound);
            }

            BlobClient blob = container.GetBlobClient(blobName);
            if (await blob.ExistsAsync(cancellationToken))
            {
                if (await CheckConfigurationIsTooLarge(blob))
                {
                    throw new AnonymizationConfigurationFetchException(Resources.AnonymizationConfigurationTooLarge);
                }

                if (string.IsNullOrEmpty(eTag))
                {
                    await blob.DownloadToAsync(targetStream, cancellationToken);
                }
                else
                {
                    var condition = new BlobRequestConditions() { IfMatch = new ETag(eTag) };
                    try
                    {
                        await blob.DownloadToAsync(targetStream, conditions: condition, cancellationToken: cancellationToken);
                    }
                    catch (RequestFailedException ex)
                    {
                        throw new AnonymizationConfigurationFetchException(ex.Message, ex);
                    }
                }
            }
            else
            {
                throw new FileNotFoundException(message: string.Format(CultureInfo.InvariantCulture, Resources.AnonymizationConfigurationNotFound, blobName));
            }
        }

        private string AddDoubleQuotesIfMissing(string eTag)
        {
            if (string.IsNullOrWhiteSpace(eTag) || eTag.StartsWith('\"'))
            {
                return eTag;
            }

            return $"\"{eTag}\"";
        }

        private async Task<BlobServiceClient> ConnectAsync(CancellationToken cancellationToken)
        {
            if (_blobClient == null)
            {
                _blobClient = await _exportClientInitializer.GetAuthorizedClientAsync(_exportJobConfiguration, cancellationToken);
            }

            return _blobClient;
        }

        private async Task<bool> CheckConfigurationIsTooLarge(BlobClient blob)
        {
            var blobProperties = (await blob.GetPropertiesAsync()).Value;
            return blobProperties.ContentLength > 1 * 1024 * 1024; // Max content length is 1 MB
        }
    }
}
