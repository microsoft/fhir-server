// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    public interface IIntegrationDataStoreClientInitilizer<TBlobServiceClient, TBlockBlobClient, TBlobClient>
    {
        /// <summary>
        /// Used to get a client that is authorized to talk to the integration data store.
        /// </summary>
        /// <returns>A client of type T</returns>
        Task<TBlobServiceClient> GetAuthorizedClientAsync();

        /// <summary>
        /// Used to get a client that is authorized to talk to the integration data store.
        /// </summary>
        /// <param name="integrationDataStoreConfiguration">Integration dataStore configuration</param>
        /// <returns>A client of type T</returns>
        Task<TBlobServiceClient> GetAuthorizedClientAsync(IntegrationDataStoreConfiguration integrationDataStoreConfiguration);

        /// <summary>
        /// Used to get a block blob client that is authorized to talk to the integration data store.
        /// </summary>
        /// <param name="blobUri">A blob uri.</param>
        /// <returns>A block blob client of type TBlobClient</returns>
        Task<TBlockBlobClient> GetAuthorizedBlockBlobClientAsync(Uri blobUri);

        /// <summary>
        /// Used to get a block blob client that is authorized to talk to the integration data store.
        /// </summary>
        /// <param name="blobUri">A blob uri.</param>
        /// <param name="integrationDataStoreConfiguration">Integration dataStore configuration</param>
        /// <returns>A block blob client of type TBlobClient</returns>
        Task<TBlockBlobClient> GetAuthorizedBlockBlobClientAsync(Uri blobUri, IntegrationDataStoreConfiguration integrationDataStoreConfiguration);

        /// <summary>
        /// Used to get a blob client that is authorized to talk to the integration data store.
        /// </summary>
        /// <param name="blobUri">A blob uri.</param>
        /// <returns>A blob client of type TBlobClient</returns>
        Task<TBlobClient> GetAuthorizedBlobClientAsync(Uri blobUri);

        /// <summary>
        /// Used to get a blob client that is authorized to talk to the integration data store.
        /// </summary>
        /// <param name="blobUri">A blob uri.</param>
        /// <param name="integrationDataStoreConfiguration">Integration dataStore configuration</param>
        /// <returns>A blob client of type TBlobClient</returns>
        Task<TBlobClient> GetAuthorizedBlobClientAsync(Uri blobUri, IntegrationDataStoreConfiguration integrationDataStoreConfiguration);
    }
}
