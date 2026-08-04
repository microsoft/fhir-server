// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Builds a dependency injection container for a tenant from the root composition root.
    /// </summary>
    public interface ITenantContainerFactory
    {
        /// <summary>
        /// Creates and initializes a container for the supplied tenant.
        /// </summary>
        /// <param name="tenant">The tenant to build a container for.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The created container. The caller owns its disposal.</returns>
        ValueTask<ITenantContainer> CreateAsync(TenantDescriptor tenant, CancellationToken cancellationToken);
    }
}
