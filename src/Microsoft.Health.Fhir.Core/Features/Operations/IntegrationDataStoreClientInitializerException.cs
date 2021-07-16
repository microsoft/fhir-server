// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Net;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    public class IntegrationDataStoreClientInitializerException : Exception
    {
        public IntegrationDataStoreClientInitializerException(string message, HttpStatusCode statusCode)
            : base(message)
        {
            Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty.");

            StatusCode = statusCode;
        }

        public HttpStatusCode StatusCode { get; }
    }
}
