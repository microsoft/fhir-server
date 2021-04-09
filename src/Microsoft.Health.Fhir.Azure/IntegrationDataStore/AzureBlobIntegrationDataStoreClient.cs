// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Azure.IntegrationDataStore
{
    public class AzureBlobIntegrationDataStoreClient : IIntegrationDataStoreClient
    {
        private IIntegrationDataStoreClientInitilizer<CloudBlobClient> _integrationDataStoreClientInitializer;
        private ILogger<AzureBlobIntegrationDataStoreClient> _logger;

        public AzureBlobIntegrationDataStoreClient(
            IIntegrationDataStoreClientInitilizer<CloudBlobClient> integrationDataStoreClientInitializer,
            ILogger<AzureBlobIntegrationDataStoreClient> logger)
        {
            _integrationDataStoreClientInitializer = integrationDataStoreClientInitializer;
            _logger = logger;
        }

        public Stream DownloadResource(Uri blobUri, long startOffset, CancellationToken cancellationToken)
        {
            return new AzureBlobSourceStream(async () => await GetCloudBlobClientAsync(blobUri, cancellationToken), startOffset, _logger);
        }

        private async Task<ICloudBlob> GetCloudBlobClientAsync(Uri blobUri, CancellationToken cancellationToken)
        {
            CloudBlobClient cloudBlobClient = await _integrationDataStoreClientInitializer.GetAuthorizedClientAsync(cancellationToken);
            return await cloudBlobClient.GetBlobReferenceFromServerAsync(blobUri);
        }
    }
}
