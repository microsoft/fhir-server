// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Api.Features.Security
{
    /// <summary>
    /// Service for performing token introspection per RFC 7662.
    /// </summary>
    public interface ITokenIntrospectionService
    {
        /// <summary>
        /// Introspects a token and returns the introspection response.
        /// </summary>
        /// <param name="token">The token to introspect.</param>
        /// <returns>
        /// Dictionary containing introspection response with 'active' key and optional claims.
        /// Returns {"active": false} for invalid tokens.
        /// </returns>
        Dictionary<string, object> IntrospectToken(string token);
    }
}
