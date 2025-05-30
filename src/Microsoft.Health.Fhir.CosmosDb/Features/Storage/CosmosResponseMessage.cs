﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.CodeAnalysis;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public sealed class CosmosResponseMessage
    {
        public CosmosResponseMessage(HttpStatusCode statusCode, bool isSuccessStatusCode, Headers headers, string errorMessage, string continuationToken, CosmosDiagnostics diagnostics)
        {
            StatusCode = statusCode;
            IsSuccessStatusCode = isSuccessStatusCode;
            Headers = headers;
            ErrorMessage = errorMessage;
            ContinuationToken = continuationToken;
            Diagnostics = diagnostics;
        }

        public HttpStatusCode StatusCode { get; private set; }

        public bool IsSuccessStatusCode { get; private set; }

        public Headers Headers { get; private set; }

        public string ErrorMessage { get; private set; }

        public string ContinuationToken { get; private set; }

        public CosmosDiagnostics Diagnostics { get; private set; }

        public static CosmosResponseMessage Create(ResponseMessage response)
        {
            return new CosmosResponseMessage(response.StatusCode, response.IsSuccessStatusCode, response.Headers, response.ErrorMessage, response.ContinuationToken, response.Diagnostics);
        }
    }
}
