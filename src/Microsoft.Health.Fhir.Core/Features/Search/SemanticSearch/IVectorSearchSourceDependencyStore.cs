// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Finds current resources whose vector search indices depend on another resource.
    /// </summary>
    public interface IVectorSearchSourceDependencyStore
    {
        /// <summary>
        /// Gets current resource keys with vector search provenance pointing to the specified source.
        /// </summary>
        /// <param name="sourceResourceType">The source resource type.</param>
        /// <param name="sourceResourceId">The source resource identifier.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The distinct dependent resource keys.</returns>
        Task<IReadOnlyCollection<ResourceKey>> GetDependentResourceKeysAsync(
            string sourceResourceType,
            string sourceResourceId,
            CancellationToken cancellationToken);
    }
}
