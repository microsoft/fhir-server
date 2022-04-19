// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
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

        private Dictionary<string, StringBuilder> _dataBuffers = new Dictionary<string, StringBuilder>();

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

        public void WriteFilePart(string fileName, string data)
        {
            EnsureArg.IsNotNull(fileName, nameof(fileName));
            EnsureArg.IsNotNull(data, nameof(data));
            CheckIfClientIsConnected();

            StringBuilder dataBuffer;
            if (!_dataBuffers.TryGetValue(fileName, out dataBuffer))
            {
                dataBuffer = new StringBuilder();
                _dataBuffers.Add(fileName, dataBuffer);
            }

            dataBuffer.AppendLine(data);
        }

        public IDictionary<string, Uri> Commit()
        {
            Dictionary<string, Uri> blobUris = new Dictionary<string, Uri>();

            foreach (string fileName in _dataBuffers.Keys)
            {
                BlockBlobClient blockBlob = _blobContainer.GetBlockBlobClient(fileName);

                using var stream = blockBlob.OpenWrite(true);
                using var writer = new StreamWriter(stream);

                var data = _dataBuffers[fileName];
                writer.WriteLine(data.ToString());
                _dataBuffers.Remove(fileName);

                blobUris.Add(fileName, blockBlob.Uri);
            }

            return blobUris;
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
