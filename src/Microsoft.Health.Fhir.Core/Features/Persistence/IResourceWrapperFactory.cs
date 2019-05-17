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
        /// <returns>An instance of <see cref="ResourceWrapper"/>.</returns>
        ResourceWrapper Create(ResourceElement resource, bool deleted);
    }
}
