// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.CodeAnalysis;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// Abstraction over Cosmos DB response headers that allows:
    /// * Easier testing.
    /// * Decoupling from the Cosmos SDK.
    /// * Concurrent access to header values (the Cosmos SDK's Headers class is not thread-safe for concurrent reads).
    /// </summary>
    public sealed class CosmosResponseHeaders
    {
        private readonly IDictionary<string, string> _headers;

        private CosmosResponseHeaders(IDictionary<string, string> headers)
        {
            if (headers == null)
            {
                headers = new Dictionary<string, string>();
            }
            else
            {
                _headers = headers;
            }
        }

        public string ActivityId { get; private set; }

        public int? SubStatusValue { get; private set; }

        public string ContentLength { get; private set; }

        public string ContentType { get; private set; }

        public string ContinuationToken { get; private set; }

        public string ETag { get; private set; }

        public string Location { get; private set; }

        public double RequestCharge { get; private set; }

        public string Session { get; private set; }

        public string this[string headerName]
        {
            get
            {
                if (_headers.TryGetValue(headerName, out string value))
                {
                    return value;
                }

                return null;
            }

            set
            {
                _headers[headerName] = value;
            }
        }

        public static CosmosResponseHeaders Create(Headers headers)
        {
            string[] array = headers.AllKeys();
            Dictionary<string, string> headersClone = new Dictionary<string, string>();
            foreach (string headerName in array)
            {
                headersClone.Add(headerName, headers.Get(headerName));
            }

            CosmosResponseHeaders cosmosResponseHeaders = new CosmosResponseHeaders(headersClone)
            {
                ActivityId = headers.ActivityId,
                SubStatusValue = headers.GetSubStatusValue(),
                ContentLength = headers.ContentLength,
                ContentType = headers.ContentType,
                ContinuationToken = headers.ContinuationToken,
                ETag = headers.ETag,
                Location = headers.Location,
                RequestCharge = headers.RequestCharge,
                Session = headers.Session,
            };

            return cosmosResponseHeaders;
        }
    }
}
