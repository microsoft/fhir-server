// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using EnsureThat;

namespace FhirSchemaManager.Exceptions
{
    public class SchemaManagerException : Exception
    {
        public SchemaManagerException(HttpStatusCode statusCode, string message)
            : base(message)
        {
            EnsureArg.IsNotNullOrWhiteSpace(message, nameof(message));

            StatusCode = statusCode;
        }

        public HttpStatusCode StatusCode { get; }
    }
}
