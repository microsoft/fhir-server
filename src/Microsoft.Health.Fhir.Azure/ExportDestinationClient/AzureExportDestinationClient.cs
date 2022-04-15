// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;

namespace Microsoft.Health.Fhir.Azure.ExportDestinationClient
{
    public class AzureExportDestinationClient : IExportDestinationClient
    {
        private BlobServiceClient _blobClient = null;
        private BlobContainerClient _blobContainer = null;

        private Dictionary<Uri, BlockBlobClient> _uriToBlobMapping = new Dictionary<Uri, BlockBlobClient>();

        private readonly IExportClientInitializer<BlobServiceClient> _exportClientInitializer;
        private readonly ExportJobConfiguration _exportJobConfiguration;
        private readonly ILogger _logger;

        public AzureExportDestinationClient(
            IExportClientInitializer<BlobServiceClient> exportClientInitializer,
            IOptions<ExportJobConfiguration> exportJobConfiguration,
            ILogger<AzureExportDestinationClient> logger)
        {
            EnsureArg.IsNotNull(exportClientInitializer, nameof(exportClientInitializer));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _exportClientInitializer = exportClientInitializer;
            _exportJobConfiguration = exportJobConfiguration.Value;
            _logger = logger;
        }

        public async Task ConnectAsync(CancellationToken cancellationToken, string containerId = null)
        {
            await ConnectAsync(_exportJobConfiguration, cancellationToken, containerId);
        }

        public async Task ConnectAsync(ExportJobConfiguration exportJobConfiguration, CancellationToken cancellationToken, string containerId = null)
        {
            try
            {
                _blobClient = await _exportClientInitializer.GetAuthorizedClientAsync(exportJobConfiguration, cancellationToken);
            }
            catch (ExportClientInitializerException ece)
            {
                _logger.LogError(ece, "Unable to initialize export client");

                throw new DestinationConnectionException(ece.Message, ece.StatusCode);
            }

            await CreateContainerAsync(_blobClient, containerId);
        }

        private async Task CreateContainerAsync(BlobServiceClient blobClient, string containerId)
        {
            _blobContainer = blobClient.GetBlobContainerClient(containerId);

            try
            {
                await _blobContainer.CreateIfNotExistsAsync();
            }
            catch (Exception se)
            {
                _logger.LogWarning(se, se.Message);

                // placeholder
                HttpStatusCode responseCode = HttpStatusCode.InternalServerError;
                throw new DestinationConnectionException(se.Message, responseCode);
            }
        }

        public Task<Uri> CreateFileAsync(string fileName, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(fileName, nameof(fileName));
            CheckIfClientIsConnected();

            BlockBlobClient blockBlob = _blobContainer.GetBlockBlobClient(fileName);
            if (!_uriToBlobMapping.ContainsKey(blockBlob.Uri))
            {
                var newProperties = new BlobHttpHeaders();
                newProperties.ContentType = "application/fhir+ndjson";
                blockBlob.SetHttpHeaders(newProperties, null, cancellationToken);

                _uriToBlobMapping.Add(blockBlob.Uri, blockBlob);
            }

            return Task.FromResult(blockBlob.Uri);
        }

        public void WriteFilePartAsync(Uri fileUri, string data, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(fileUri, nameof(fileUri));
            EnsureArg.IsNotNull(data, nameof(data));
            CheckIfClientIsConnected();

            if (_uriToBlobMapping.TryGetValue(fileUri, out var blob))
            {
                using var stream = blob.OpenWrite(false, null, cancellationToken);
                using var writer = new StreamWriter(stream);
                writer.WriteLine(data);
            }
        }

        public void OpenFileAsync(Uri fileUri)
        {
            EnsureArg.IsNotNull(fileUri, nameof(fileUri));
            CheckIfClientIsConnected();

            if (_uriToBlobMapping.ContainsKey(fileUri))
            {
                _logger.LogInformation("Trying to open a file that the client already knows about.");
                return;
            }

            BlockBlobClient blockBlob = new BlockBlobClient(fileUri);

            _uriToBlobMapping.Add(fileUri, blockBlob);
        }

        private void CheckIfClientIsConnected()
        {
            if (_blobClient == null || _blobContainer == null)
            {
                throw new DestinationConnectionException(Resources.DestinationClientNotConnected, HttpStatusCode.InternalServerError);
            }
        }
    }
}
