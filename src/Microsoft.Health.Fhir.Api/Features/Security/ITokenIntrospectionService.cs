// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>
        /// Dictionary containing introspection response with 'active' key and optional claims.
        /// Returns {"active": false} for invalid tokens.
        /// </returns>
        Task<Dictionary<string, object>> IntrospectTokenAsync(string token, CancellationToken cancellationToken = default);
    }
}
