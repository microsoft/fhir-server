// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Api.Features.Exceptions
{
    /// <summary>
    /// Exception for OAuth2 bad request errors (RFC 6749).
    /// </summary>
    public class OAuth2BadRequestException : Exception
    {
        public OAuth2BadRequestException(string error, string errorDescription)
            : base($"{error}: {errorDescription}")
        {
            Error = error;
            ErrorDescription = errorDescription;
        }

        public string Error { get; }

        public string ErrorDescription { get; }
    }
}
