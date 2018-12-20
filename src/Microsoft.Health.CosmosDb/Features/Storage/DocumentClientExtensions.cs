// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    public static class DocumentClientExtensions
    {
        /// <summary>
        /// IDocumentClient does not extend IDisposable, but DocumentClient is IDisposable.
        /// </summary>
        /// <param name="documentClient">The document client to dispose.</param>
        public static void Dispose(this IDocumentClient documentClient)
        {
            (documentClient as IDisposable)?.Dispose();
        }

        /// <summary>
        /// Creates a Database if it does not exist. This functionality is defined in DocumentClient, but not IDocumentClient
        /// </summary>
        /// <param name="documentClient">The document client</param>
        /// <param name="database">The database to create</param>
        /// <param name="options">The request options</param>
        /// <returns>The result</returns>
        public static async Task<ResourceResponse<Database>> CreateDatabaseIfNotExistsAsync(this IDocumentClient documentClient, Database database, RequestOptions options = null)
        {
            var databaseUri = UriFactory.CreateDatabaseUri(database.Id);
            return await CreateIfNotExists(
                () => documentClient.ReadDatabaseAsync(databaseUri, options),
                () => documentClient.CreateDatabaseAsync(database, options));
        }

        /// <summary>
        /// Creates a collection if it does not exist. This functionality is defined in DocumentClient, but not IDocumentClient.
        /// </summary>
        /// <param name="documentClient">The document client</param>
        /// <param name="databaseUri">The database URI</param>
        /// <param name="collectionUri">The collection URI</param>
        /// <param name="documentCollection">The collection to create</param>
        /// <param name="options">The request options</param>
        /// <returns>The result</returns>
        public static async Task<ResourceResponse<DocumentCollection>> CreateDocumentCollectionIfNotExistsAsync(
            this IDocumentClient documentClient,
            Uri databaseUri,
            Uri collectionUri,
            DocumentCollection documentCollection,
            RequestOptions options = null)
        {
            return await CreateIfNotExists(
                async () =>
                {
                    DocumentCollection existingDocumentCollection = await documentClient.ReadDocumentCollectionAsync(collectionUri, options);

                    existingDocumentCollection.IndexingPolicy = documentCollection.IndexingPolicy;
                    existingDocumentCollection.DefaultTimeToLive = documentCollection.DefaultTimeToLive;

                    return await documentClient.ReplaceDocumentCollectionAsync(existingDocumentCollection, options);
                },
                () => documentClient.CreateDocumentCollectionAsync(databaseUri, documentCollection, options));
        }

        public static async Task<DocumentCollection> TryGetDocumentCollectionAsync(
            this IDocumentClient documentClient,
            Uri collectionUri,
            RequestOptions options = null)
        {
            try
            {
                return await documentClient.ReadDocumentCollectionAsync(collectionUri, options);
            }
            catch (DocumentClientException readException) when (readException.StatusCode == HttpStatusCode.NotFound)
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
            catch (DocumentClientException readException) when (readException.StatusCode == HttpStatusCode.NotFound)
            {
                try
                {
                    return await create();
                }
                catch (DocumentClientException createException) when (createException.StatusCode == HttpStatusCode.Conflict)
                {
                    return await read();
                }
            }
        }
    }
}
