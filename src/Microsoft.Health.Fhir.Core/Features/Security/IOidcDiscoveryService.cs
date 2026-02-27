// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    /// <summary>
    /// Discovers OAuth2 authorization and token endpoints from an OIDC authority.
    /// Results are cached per authority to avoid repeated HTTP calls.
    /// </summary>
    public interface IOidcDiscoveryService
    {
        /// <summary>
        /// Resolves the authorization and token endpoints for the given OIDC authority.
        /// Fetches the OpenID Connect discovery document at {authority}/.well-known/openid-configuration.
        /// If discovery fails, falls back to Entra ID URL pattern ({authority}/oauth2/v2.0/authorize and /token).
        /// </summary>
        /// <param name="authority">The OIDC authority URL.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A tuple containing the authorization and token endpoint URIs.</returns>
        Task<(Uri AuthorizationEndpoint, Uri TokenEndpoint)> ResolveEndpointsAsync(string authority, CancellationToken cancellationToken = default);
    }
}
