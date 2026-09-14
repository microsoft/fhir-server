// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    /// <summary>
    /// Provides a mechanism to create a <see cref="ResourceWrapper"/>.
    /// </summary>
    public interface IResourceWrapperFactory
    {
        /// <summary>
        /// Creates an instance of <see cref="ResourceWrapper"/> with ordinary search indices only.
        /// Vector indices must be completed asynchronously after resource versions are finalized.
        /// </summary>
        /// <param name="resource">The resource to be wrapped.</param>
        /// <param name="deleted">A flag indicating whether the resource is deleted or not.</param>
        /// <param name="keepMeta">A flag indicating whether to keep the metadata section or clear it.</param>
        /// <param name="keepVersion">A flag indicating whether to keep the versionb or set it to 1.</param>
        /// <returns>An instance of <see cref="ResourceWrapper"/>.</returns>
        ResourceWrapper Create(ResourceElement resource, bool deleted, bool keepMeta, bool keepVersion = false);

        /// <summary>
        /// Updates ordinary search indices on <see cref="ResourceWrapper"/> without evaluating vector indices.
        /// </summary>
        /// <param name="resourceWrapper">An instance of <see cref="ResourceWrapper"/></param>
        void Update(ResourceWrapper resourceWrapper);

        /// <summary>
        /// Refreshes ordinary search indices for every resource, then updates vector indices in one batch when enabled.
        /// Complete this operation before entering persistence retries.
        /// </summary>
        /// <param name="resources">The resources whose indices should be refreshed.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing completion of all indexing.</returns>
        Task UpdateAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken);

        /// <summary>
        /// Completes vector indexing in one batch when enabled, using already extracted ordinary indices.
        /// Call after resource versions are finalized, within any transaction heartbeat and before persistence retries.
        /// When vector indexing is disabled, existing vector evaluation state is preserved.
        /// </summary>
        /// <param name="resources">The prepared resources to index.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing completion of vector indexing.</returns>
        Task UpdateVectorSearchIndicesAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken);
    }
}
