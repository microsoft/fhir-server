// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.AccessTokenProvider
{
    public interface IAccessTokenProviderFactory
    {
        /// <summary>
        /// Creates an instance of <see cref="IAccessTokenProvider"/> based on <paramref name="accessTokenProviderType"/>.
        /// </summary>
        /// <param name="accessTokenProviderType">The requested access token provider type.</param>
        /// <returns>An instance of <see cref="IAccessTokenProvider"/>.</returns>
        /// <exception cref="UnsupportedAccessTokenProviderException">Thrown when the <paramref name="accessTokenProviderType"/> is not supported.</exception>
        IAccessTokenProvider Create(string accessTokenProviderType);

        /// <summary>
        /// Checks whether the <paramref name="accessTokenProviderType"/> is supported or not.
        /// </summary>
        /// <param name="accessTokenProviderType">The requested access token provider type.</param>
        /// <returns><c>true</c> if the <paramref name="accessTokenProviderType"/> is supported; otherwise, <c>false</c>.</returns>
        bool IsSupportedAccessTokenProviderType(string accessTokenProviderType);
    }
}
