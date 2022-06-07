// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.IO;
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

        private readonly IExportClientInitializer<BlobServiceClient> _exportClientInitializer;
        private readonly ExportJobConfiguration _exportJobConfiguration;
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

        public async Task FetchAsync(string location, Stream targetStream, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrEmpty(location, nameof(location));
            EnsureArg.IsNotNull(targetStream, nameof(targetStream));

            string[] blobLocation = location.Split(':', StringSplitOptions.RemoveEmptyEntries);
            string blobName = blobLocation[0];
            string eTag = blobLocation.Length > 1 ? blobLocation[1] : null;
            eTag = AddDoubleQuotesIfMissing(eTag);

            BlobServiceClient blobClient = Connect();
            BlobContainerClient container = blobClient.GetBlobContainerClient(AnonymizationContainer);
            if (!await container.ExistsAsync(cancellationToken))
            {
                throw new FileNotFoundException(message: Resources.AnonymizationContainerNotFound);
            }

            BlobClient blob = container.GetBlobClient(blobName);
            if (await blob.ExistsAsync(cancellationToken))
            {
                if (CheckConfigurationIsTooLarge(blob))
                {
                    throw new AnonymizationConfigurationFetchException(Resources.AnonymizationConfigurationTooLarge);
                }

                if (string.IsNullOrEmpty(eTag))
                {
                    await blob.DownloadToAsync(targetStream, cancellationToken);
                }
                else
                {
                    var blobDownloadToOptions = new BlobDownloadToOptions();
                    blobDownloadToOptions.Conditions = new BlobRequestConditions();
                    blobDownloadToOptions.Conditions.IfMatch = new ETag(eTag);
                    try
                    {
                        await blob.DownloadToAsync(targetStream, blobDownloadToOptions, cancellationToken);
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

        private static string AddDoubleQuotesIfMissing(string eTag)
        {
            if (string.IsNullOrWhiteSpace(eTag) || eTag.StartsWith('\"'))
            {
                return eTag;
            }

            return $"\"{eTag}\"";
        }

        private BlobServiceClient Connect()
        {
            if (_blobClient == null)
            {
                _blobClient = _exportClientInitializer.GetAuthorizedClient(_exportJobConfiguration);
            }

            return _blobClient;
        }

        private static bool CheckConfigurationIsTooLarge(BlobClient blob) =>
            blob.GetProperties().Value.ContentLength > 1 * 1024 * 1024; // Max content length is 1 MB
    }
}
