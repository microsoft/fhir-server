// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.AccessTokenProvider
{
    public interface IAccessTokenProvider
    {
        /// <summary>
        /// Gets the supported destination type.
        /// </summary>
        string AccessTokenProviderType { get; }

        Task<string> GetAccessTokenForResourceAsync(Uri resourceUri, CancellationToken cancellationToken);
    }
}
