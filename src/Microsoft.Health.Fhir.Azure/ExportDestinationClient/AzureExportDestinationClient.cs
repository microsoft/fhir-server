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

        private readonly Dictionary<string, BlobStreamWriter> _blobStreams = new Dictionary<string, BlobStreamWriter>();

        private readonly IExportClientInitializer<BlobServiceClient> _exportClientInitializer;
        private readonly ExportJobConfiguration _exportJobConfiguration;
        private readonly ILogger _logger;

        private const int RetryDelaySeconds = 3;

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

                // If storage account is not found
                if (ex.InnerExceptions[0].Message.Contains("No such host is known", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(ex, "The Storage account not found");
                    throw new DestinationConnectionException(Resources.StorageAccountNotFound, HttpStatusCode.NotFound);
                }
                else
                {
                    _logger.LogWarning(innerException, "{Error}", innerException.Message);
                    throw new DestinationConnectionException(innerException.Message, (HttpStatusCode)innerException.Status);
                }
            }
            catch (AccessTokenProviderException ex)
            {
                // Can't get an access token, likely an error with setup
                _logger.LogWarning(ex, "Failed to get access token for export");
                throw new DestinationConnectionException(Resources.CannotGetAccessToken, HttpStatusCode.Forbidden);
            }
            catch (ArgumentNullException ex) when (ex.Message.Contains("credentialBundleName", StringComparison.OrdinalIgnoreCase))
            {
                // This indicates that Managed Identity isn't setup
                _logger.LogWarning(ex, "Failed to get access token for export");
                throw new DestinationConnectionException(Resources.CannotGetAccessToken, HttpStatusCode.Forbidden);
            }
        }

        public void WriteFilePart(string fileName, string data)
        {
            EnsureArg.IsNotNull(fileName, nameof(fileName));
            EnsureArg.IsNotNull(data, nameof(data));
            CheckIfClientIsConnected();

            BlobStreamWriter blobStream;
            if (!_blobStreams.TryGetValue(fileName, out blobStream))
            {
                blobStream = new BlobStreamWriter(_blobContainer.GetBlockBlobClient(fileName));
                _blobStreams.Add(fileName, blobStream);
            }

            blobStream.StreamWriter.WriteLine(data);
        }

        public IDictionary<string, Uri> Commit()
        {
            Dictionary<string, Uri> blobUris = new Dictionary<string, Uri>();

            foreach (string fileName in _blobStreams.Keys)
            {
                var blobUri = CommitFile(fileName);
                blobUris.Add(fileName, blobUri);
            }

            return blobUris;
        }

        public Uri CommitFile(string fileName)
        {
            Uri uri = null;
            if (_blobStreams.ContainsKey(fileName))
            {
                try
                {
                    uri = CommitFileRetry(fileName);
                }
                catch (RequestFailedException ex)
                {
                    _logger.LogError(ex, "Failed to write export file");

                    // Add a small delay before retrying in case of race condition
                    Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds)).Wait();

                    try
                    {
                        uri = CommitFileRetry(fileName);
                    }
                    catch (ObjectDisposedException odEx)
                    {
                        _logger.LogError(odEx, "Failed to write export file due to ObjectDisposedException");
                        throw new DestinationConnectionException(ex.Message, (HttpStatusCode)ex.Status);
                    }
                    catch (RequestFailedException ex2)
                    {
                        _logger.LogError(ex2, "Failed to write export file on retry");
                        throw new DestinationConnectionException(ex2.Message, (HttpStatusCode)ex2.Status);
                    }
                }
            }
            else
            {
                throw new ArgumentException($"Cannot commit non-existant file {fileName}");
            }

            _blobStreams[fileName].Dispose();
            _blobStreams.Remove(fileName);
            return uri;
        }

        private Uri CommitFileRetry(string fileName)
        {
            var blobWriter = _blobStreams[fileName];

            blobWriter.StreamWriter.Flush();
            blobWriter.StreamWriter.Close();

            return blobWriter.BlobUri;
        }

        private void CheckIfClientIsConnected()
        {
            if (_blobClient == null || _blobContainer == null)
            {
                throw new DestinationConnectionException(Resources.DestinationClientNotConnected, HttpStatusCode.InternalServerError);
            }
        }

        private class BlobStreamWriter : IDisposable
        {
            public BlobStreamWriter(BlockBlobClient blockBlob)
            {
                BlobUri = blockBlob.Uri;
                Stream = blockBlob.OpenWrite(true);
                StreamWriter = new StreamWriter(Stream);
            }

            public Stream Stream { get; private set;  }

            public StreamWriter StreamWriter { get; private set; }

            public Uri BlobUri { get; private set; }

            public void Dispose()
            {
                StreamWriter?.Dispose();
                Stream?.Dispose();
            }
        }
    }
}
