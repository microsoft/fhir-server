// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests
{
    internal class CosmosDbMockingHelper
    {
        internal static DocumentClientException CreateDocumentClientException(string message, NameValueCollection responseHeaders, HttpStatusCode? statusCode)
        {
            return (DocumentClientException)CreateInstance( // internal DocumentClientException(string message, Exception innerException, INameValueCollection responseHeaders, HttpStatusCode? statusCode, Uri requestUri = null)
                typeof(DocumentClientException),
                message,
                null,
                CreateInstance( // public DictionaryNameValueCollection(NameValueCollection c)
                    typeof(IDocumentClient).Assembly.GetType("Microsoft.Azure.Documents.Collections.DictionaryNameValueCollection"),
                    responseHeaders),
                statusCode,
                null);
        }

        internal static ResourceResponse<T> CreateResourceResponse<T>(T resource, HttpStatusCode statusCode, NameValueCollection responseHeaders)
            where T : Resource, new()
        {
            return (ResourceResponse<T>)CreateInstance( // internal ResourceResponse(DocumentServiceResponse response, ITypeResolver<TResource> typeResolver = null)
                typeof(ResourceResponse<T>),
                CreateDocumentServiceResponse(resource, statusCode, responseHeaders),
                null);
        }

        internal static StoredProcedureResponse<T> CreateStoredProcedureResponse<T>(T resource, HttpStatusCode statusCode, NameValueCollection responseHeaders)
        {
            return (StoredProcedureResponse<T>)CreateInstance( // internal StoredProcedureResponse(DocumentServiceResponse response, JsonSerializerSettings serializerSettings = null)
                typeof(StoredProcedureResponse<T>),
                CreateDocumentServiceResponse(resource, statusCode, responseHeaders),
                null);
        }

        internal static object CreateDocumentServiceResponse<T>(T resource, HttpStatusCode statusCode, NameValueCollection responseHeaders)
        {
            var serializer = new JsonSerializer();
            var ms = new MemoryStream();
            var jsonTextWriter = new JsonTextWriter(new StreamWriter(ms));
            serializer.Serialize(jsonTextWriter, resource);
            jsonTextWriter.Flush();
            ms.Position = 0;

            return CreateInstance( // internal DocumentServiceResponse(Stream body, INameValueCollection headers, HttpStatusCode statusCode, JsonSerializerSettings serializerSettings = null)
                typeof(IDocumentClient).Assembly.GetType("Microsoft.Azure.Documents.DocumentServiceResponse"),
                ms,
                CreateInstance( // public DictionaryNameValueCollection(NameValueCollection c)
                    typeof(IDocumentClient).Assembly.GetType("Microsoft.Azure.Documents.Collections.DictionaryNameValueCollection"),
                    responseHeaders),
                statusCode,
                null);
        }

        private static object CreateInstance(Type type, params object[] args)
        {
            return Activator.CreateInstance(type, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public, null, args, CultureInfo.InvariantCulture);
        }
    }
}
