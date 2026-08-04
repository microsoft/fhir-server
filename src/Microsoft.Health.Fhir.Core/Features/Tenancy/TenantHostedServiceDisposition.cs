// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Describes how a hosted service registered in the root composition root should be treated when
    /// building a tenant container.
    /// </summary>
    public enum TenantHostedServiceDisposition
    {
        /// <summary>
        /// The service runs once in the root container and is not started per tenant.
        /// </summary>
        Shared = 0,

        /// <summary>
        /// The service does not run in the request-serving tier under tenancy. It is expected to run in a
        /// separate worker process that owns background work for the pool.
        /// </summary>
        Relocated = 1,

        /// <summary>
        /// The service runs once per tenant container, as part of tenant container construction.
        /// </summary>
        PerTenantInitializer = 2,
    }
}
