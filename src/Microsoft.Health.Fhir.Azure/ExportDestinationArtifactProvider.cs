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
using EnsureThat;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;

namespace Microsoft.Health.Fhir.Azure
{
    public class ExportDestinationArtifactProvider : IArtifactProvider
    {
        private const string AnonymizationContainer = "anonymization";

        private IExportClientInitializer<CloudBlobClient> _exportClientInitializer;
        private ExportJobConfiguration _exportJobConfiguration;
        private CloudBlobClient _blobClient;

        public ExportDestinationArtifactProvider(
            IExportClientInitializer<CloudBlobClient> exportClientInitializer,
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

            CloudBlobClient blobClient = await ConnectAsync(cancellationToken);
            CloudBlobContainer container = blobClient.GetContainerReference(AnonymizationContainer);
            if (!await container.ExistsAsync(cancellationToken))
            {
                throw new FileNotFoundException(message: Resources.AnonymizationContainerNotFound);
            }

            CloudBlob blob = container.GetBlobReference(blobName);
            if (await blob.ExistsAsync(cancellationToken))
            {
                if (CheckConfigurationIsTooLarge(blob))
                {
                    throw new AnonymizationConfigurationFetchException(Resources.AnonymizationConfigurationTooLarge);
                }

                if (string.IsNullOrEmpty(eTag))
                {
                    await blob.DownloadToStreamAsync(targetStream, cancellationToken);
                }
                else
                {
                    AccessCondition condition = AccessCondition.GenerateIfMatchCondition(eTag);
                    var blobRequestOptions = new BlobRequestOptions();
                    var operationContext = new OperationContext();
                    try
                    {
                        await blob.DownloadToStreamAsync(targetStream, accessCondition: condition, blobRequestOptions, operationContext, cancellationToken);
                    }
                    catch (StorageException ex)
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

        private async Task<CloudBlobClient> ConnectAsync(CancellationToken cancellationToken)
        {
            if (_blobClient == null)
            {
                _blobClient = await _exportClientInitializer.GetAuthorizedClientAsync(_exportJobConfiguration, cancellationToken);
            }

            return _blobClient;
        }

        private bool CheckConfigurationIsTooLarge(CloudBlob blob)
        {
            return blob.Properties.Length > 1 * 1024 * 1024; // Max content length is 1 MB
        }
    }
}
