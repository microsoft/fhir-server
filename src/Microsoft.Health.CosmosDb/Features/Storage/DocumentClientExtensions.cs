// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    public static class DocumentClientExtensions
    {
        /// <summary>
        /// IDocumentClient does not extend IDisposable, but DocumentClient is IDisposable.
        /// </summary>
        /// <param name="documentClient">The document client to dispose.</param>
        public static void Dispose(this CosmosClient documentClient)
        {
            (documentClient as IDisposable)?.Dispose();
        }

        /// <summary>
        /// Creates a Database if it does not exist. This functionality is defined in DocumentClient, but not IDocumentClient
        /// </summary>
        /// <param name="documentClient">The document client</param>
        /// <param name="databaseId">The database to create</param>
        /// <returns>The result</returns>
        public static async Task<DatabaseResponse> CreateDatabaseIfNotExistsAsync(
            this CosmosClient documentClient,
            string databaseId)
        {
            return await documentClient.CreateDatabaseIfNotExistsAsync(databaseId);
        }

        public static async Task<ContainerResponse> TryGetDocumentCollectionAsync(
            this Database documentClient,
            string collectionId)
        {
            try
            {
                return await documentClient.GetContainer(collectionId).ReadContainerAsync();
            }
            catch (CosmosException readException) when (readException.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        private static async Task<T> CreateIfNotExists<T>(Func<Task<T>> read, Func<Task<T>> create)
        {
            try
            {
                return await read();
            }
            catch (CosmosException readException) when (readException.StatusCode == HttpStatusCode.NotFound)
            {
                try
                {
                    return await create();
                }
                catch (CosmosException createException) when (createException.StatusCode == HttpStatusCode.Conflict)
                {
                    return await read();
                }
            }
        }
    }
}
