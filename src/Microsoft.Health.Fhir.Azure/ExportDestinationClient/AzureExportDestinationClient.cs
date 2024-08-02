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
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;

namespace Microsoft.Health.Fhir.Azure.ExportDestinationClient
{
    public class AzureExportDestinationClient : IExportDestinationClient
    {
        private BlobServiceClient _blobClient = null;
        private BlobContainerClient _blobContainer = null;

        private readonly Dictionary<string, List<string>> _dataBuffers = new Dictionary<string, List<string>>();

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
                _blobClient = _exportClientInitializer.GetAuthorizedClient(exportJobConfiguration);
            }
            catch (ExportClientInitializerException ece)
            {
                _logger.LogError(ece, "Unable to initialize export client");

                throw new DestinationConnectionException(ece.Message, ece.StatusCode);
            }

            await CreateContainerAsync(_blobClient, containerId, cancellationToken);
        }

        private async Task CreateContainerAsync(BlobServiceClient blobClient, string containerId, CancellationToken cancellationToken)
        {
            _blobContainer = blobClient.GetBlobContainerClient(containerId);

            try
            {
                await _blobContainer.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            }
            catch (RequestFailedException se)
            {
                _logger.LogWarning(se, "{Error}", se.Message);

                throw new DestinationConnectionException(se.Message, (HttpStatusCode)se.Status);
            }
            catch (AggregateException ex) when (ex.InnerExceptions[0] is RequestFailedException)
            {
                // The blob container has added a 6 attempt retry that creates an aggregate exception if it can't find the blob.
                var innerException = (RequestFailedException)ex.InnerExceptions[0];
                _logger.LogWarning(innerException, "{Error}", innerException.Message);
                throw new DestinationConnectionException(innerException.Message, (HttpStatusCode)innerException.Status);
            }
            catch (AccessTokenProviderException ex)
            {
                // Can't get an access token, likely an error with setup
                _logger.LogWarning(ex, "Failed to get access token for export");
                throw new DestinationConnectionException(Resources.CannotGetAccessToken, HttpStatusCode.Forbidden);
            }
        }

        public void WriteFilePart(string fileName, string data)
        {
            EnsureArg.IsNotNull(fileName, nameof(fileName));
            EnsureArg.IsNotNull(data, nameof(data));
            CheckIfClientIsConnected();

            List<string> dataBuffer;
            if (!_dataBuffers.TryGetValue(fileName, out dataBuffer))
            {
                dataBuffer = new List<string>();
                _dataBuffers.Add(fileName, dataBuffer);
            }

            dataBuffer.Add(data);
        }

        public IDictionary<string, Uri> Commit()
        {
            Dictionary<string, Uri> blobUris = new Dictionary<string, Uri>();

            foreach (string fileName in _dataBuffers.Keys)
            {
                var blobUri = CommitFile(fileName);
                blobUris.Add(fileName, blobUri);
            }

            return blobUris;
        }

        public Uri CommitFile(string fileName)
        {
            Uri uri;
            if (_dataBuffers.ContainsKey(fileName))
            {
                try
                {
                    uri = CommitFileRetry(fileName);
                }
                catch (RequestFailedException)
                {
                    try
                    {
                        uri = CommitFileRetry(fileName);
                    }
                    catch (RequestFailedException ex)
                    {
                        _logger.LogError(ex, "Failed to write export file");
                        throw new DestinationConnectionException(ex.Message, (HttpStatusCode)ex.Status);
                    }
                }
            }
            else
            {
                throw new ArgumentException($"Cannot commit non-existant file {fileName}");
            }

            _dataBuffers.Remove(fileName);
            return uri;
        }

        private Uri CommitFileRetry(string fileName)
        {
            BlockBlobClient blockBlob = _blobContainer.GetBlockBlobClient(fileName);

            using var stream = blockBlob.OpenWrite(true);
            using var writer = new StreamWriter(stream);

            var dataLines = _dataBuffers[fileName];
            foreach (var line in dataLines)
            {
                writer.WriteLine(line);
            }

            return blockBlob.Uri;
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
