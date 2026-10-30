// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Messages.Delete;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    /// <summary>
    /// Provides operations for deleting individual resources or collections of resources.
    /// </summary>
    public interface IDeletionService
    {
        /// <summary>
        /// Deletes a single resource.
        /// </summary>
        /// <param name="request">The delete request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The key of the deleted resource.</returns>
        public Task<ResourceKey> DeleteAsync(DeleteResourceRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Deletes resources matching a conditional delete request.
        /// </summary>
        /// <param name="request">The conditional delete request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="excludedResourceTypes">Optional resource types to exclude from deletion.</param>
        /// <param name="onProfileResourceSkipped">Optional callback invoked with each skipped profile resource type and count.</param>
        /// <returns>Deleted resource counts grouped by resource type.</returns>
        public Task<IDictionary<string, long>> DeleteMultipleAsync(ConditionalDeleteResourceRequest request, CancellationToken cancellationToken, IList<string> excludedResourceTypes = null, Action<string, long> onProfileResourceSkipped = null);
    }
}
