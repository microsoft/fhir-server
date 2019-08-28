// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.SecretStore
{
    /// <summary>
    /// Class to be used for throwing exceptions when something goes wrong in <see cref="ISecretStore"/>.
    /// To be used as an abstraction layer to convert implementation specific errors to more generic errors.
    /// </summary>
    public class SecretStoreException : Exception
    {
        public SecretStoreException(string message, Exception innerException, HttpStatusCode statusCode)
            : base(message, innerException)
        {
            EnsureArg.IsNotNullOrWhiteSpace(message, nameof(message));

            ResponseStatusCode = statusCode;
        }

        public HttpStatusCode ResponseStatusCode { get; }
    }
}
