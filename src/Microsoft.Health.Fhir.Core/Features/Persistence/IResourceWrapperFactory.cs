// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    /// <summary>
    /// Provides a mechanism to create a <see cref="ResourceWrapper"/>.
    /// </summary>
    public interface IResourceWrapperFactory
    {
        /// <summary>
        /// Creates an instance of <see cref="ResourceWrapper"/>.
        /// </summary>
        /// <param name="resource">The resource to be wrapped.</param>
        /// <param name="deleted">A flag indicating whether the resource is deleted or not.</param>
        /// <param name="keepMeta">A flag indicating whether to keep the metadata section or clear it.</param>
        /// <param name="keepVersion">A flag indicating whether to keep the versionb or set it to 1.</param>
        /// <returns>An instance of <see cref="ResourceWrapper"/>.</returns>
        ResourceWrapper Create(ResourceElement resource, bool deleted, bool keepMeta, bool keepVersion = false);

        /// <summary>
        /// Updates the search index on <see cref="ResourceWrapper"/>.
        /// </summary>
        /// <param name="resourceWrapper">An instance of <see cref="ResourceWrapper"/></param>
        void Update(ResourceWrapper resourceWrapper);
    }
}
