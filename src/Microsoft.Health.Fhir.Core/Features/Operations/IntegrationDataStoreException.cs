// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Net;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    public class IntegrationDataStoreException : Exception
    {
        public IntegrationDataStoreException(string message, HttpStatusCode statusCode)
            : base(message)
        {
            Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty.");

            StatusCode = statusCode;
        }

        public IntegrationDataStoreException(Exception innerException, HttpStatusCode statusCode)
            : base(innerException?.Message, innerException)
        {
            Debug.Assert(innerException != null, "Exception should not be null.");

            StatusCode = statusCode;
        }

        public HttpStatusCode StatusCode { get; }
    }
}
