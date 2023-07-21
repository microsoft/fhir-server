// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Azure.IntegrationDataStore
{
    public interface IIntegrationDataStoreClientInitializer
    {
        /// <summary>
        /// Used to get a client that is authorized to talk to the integration data store.
        /// </summary>
        /// <returns>A BlobServiceClient object.</returns>
        Task<BlobServiceClient> GetAuthorizedClientAsync();

        /// <summary>
        /// Used to get a client that is authorized to talk to the integration data store.
        /// </summary>
        /// <param name="integrationDataStoreConfiguration">Integration dataStore configuration</param>
        /// <returns>A BlobServiceClient object.</returns>
        Task<BlobServiceClient> GetAuthorizedClientAsync(IntegrationDataStoreConfiguration integrationDataStoreConfiguration);

        /// <summary>
        /// Used to get a block blob client that is authorized to talk to the integration data store.
        /// </summary>
        /// <param name="blobUri">A blob uri.</param>
        /// <returns>A BlockBlobClient object.</returns>
        Task<BlockBlobClient> GetAuthorizedBlockBlobClientAsync(Uri blobUri);

        /// <summary>
        /// Used to get a block blob client that is authorized to talk to the integration data store.
        /// </summary>
        /// <param name="blobUri">A blob uri.</param>
        /// <param name="integrationDataStoreConfiguration">Integration dataStore configuration</param>
        /// <returns>A BlockBlobClient object.</returns>
        Task<BlockBlobClient> GetAuthorizedBlockBlobClientAsync(Uri blobUri, IntegrationDataStoreConfiguration integrationDataStoreConfiguration);

        /// <summary>
        /// Used to get a blob client that is authorized to talk to the integration data store.
        /// </summary>
        /// <param name="blobUri">A blob uri.</param>
        /// <returns>A BlobClient object.</returns>
        Task<BlobClient> GetAuthorizedBlobClientAsync(Uri blobUri);

        /// <summary>
        /// Used to get a blob client that is authorized to talk to the integration data store.
        /// </summary>
        /// <param name="blobUri">A blob uri.</param>
        /// <param name="integrationDataStoreConfiguration">Integration dataStore configuration</param>
        /// <returns>A BlobClient object.</returns>
        Task<BlobClient> GetAuthorizedBlobClientAsync(Uri blobUri, IntegrationDataStoreConfiguration integrationDataStoreConfiguration);
    }
}
