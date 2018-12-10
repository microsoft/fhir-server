// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Routing
{
    /// <summary>
    /// Provides functionalities to resolve URLs.
    /// </summary>
    public interface IUrlResolver
    {
        /// <summary>
        /// Resolves the URL for the server metadata.
        /// </summary>
        /// <param name="includeSystemQueryString">A indicator if the system query string parameter should be included</param>
        /// <returns>The URL for the metadata route.</returns>
        Uri ResolveMetadataUrl(bool includeSystemQueryString);

        /// <summary>
        /// Resolves the URL for the given <paramref name="resource"/>.
        /// </summary>
        /// <param name="resource">The resource whose URL should be resolved for.</param>
        /// <param name="includeVersion">Includes the version in the URL.</param>
        /// <returns>The URL for the given <paramref name="resource"/>.</returns>
        Uri ResolveResourceUrl(Resource resource, bool includeVersion = false);

        /// <summary>
        /// Resolves the URL for the specified route
        /// </summary>
        /// <param name="unsupportedSearchParams">A list of unsupported search parameters.</param>
        /// <param name="continuationToken">The continuation token.</param>
        /// <returns>The URL.</returns>
        Uri ResolveRouteUrl(IEnumerable<Tuple<string, string>> unsupportedSearchParams = null, string continuationToken = null);
    }
}
