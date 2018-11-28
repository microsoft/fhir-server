// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Provides functionalities to search resources.
    /// </summary>
    public interface ISearchService
    {
        /// <summary>
        /// Searches the resources using the <paramref name="queryParameters"/>.
        /// </summary>
        /// <param name="resourceType">The resource type that should be searched.</param>
        /// <param name="queryParameters">The search queries.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A <see cref="Bundle"/> representing the result.</returns>
        Task<Bundle> SearchAsync(
            string resourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Searches resources based on the given compartment using the <paramref name="queryParameters"/>.
        /// </summary>
        /// <param name="compartmentType">The compartment type that needs to be searched.</param>
        /// <param name="compartmentId">The compartment id along with the compartment type that needs to be seached.</param>
        /// <param name="resourceType">The resource type that should be searched. If * is specified we search all resource types.</param>
        /// <param name="queryParameters">The search queries.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A <see cref="Bundle"/> representing the search result.</returns>
        Task<Bundle> SearchCompartmentAsync(string compartmentType, string compartmentId, string resourceType, IReadOnlyList<Tuple<string, string>> queryParameters, CancellationToken cancellationToken);

        Task<Bundle> SearchHistoryAsync(string resourceType, string resourceId, PartialDateTime at, PartialDateTime since, int? count, string continuationToken, CancellationToken cancellationToken);
    }
}
