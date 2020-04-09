// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;

namespace FhirSchemaManager.Exceptions
{
    public class SchemaOperationFailedException : SchemaManagerException
    {
        public SchemaOperationFailedException(HttpStatusCode statusCode, string message)
            : base(statusCode, message)
        {
            EnsureArg.IsNotNullOrWhiteSpace(message, nameof(message));
        }
    }
}
