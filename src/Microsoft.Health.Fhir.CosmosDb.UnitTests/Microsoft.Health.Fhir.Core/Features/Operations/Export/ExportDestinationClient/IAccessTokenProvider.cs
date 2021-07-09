// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient
{
    public interface IAccessTokenProvider
    {
        /// <summary>
        /// Gets the access token for the resource.
        /// </summary>
        /// <param name="resourceUri">Uri pointing to the resource for which we need an access token.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Access token.</returns>
        /// <exception cref="AccessTokenProviderException">Thrown when unable to get access token.</exception>
        Task<string> GetAccessTokenForResourceAsync(Uri resourceUri, CancellationToken cancellationToken);
    }
}
