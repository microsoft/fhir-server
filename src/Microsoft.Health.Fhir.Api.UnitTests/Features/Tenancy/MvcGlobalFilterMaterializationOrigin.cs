// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Tenancy
{
    /// <summary>
    /// Identifies which service provider materialized the MVC global filter instance whose identity a
    /// response reported.
    /// </summary>
    internal enum MvcGlobalFilterMaterializationOrigin
    {
        /// <summary>
        /// The executing filter instance was materialized by the root service provider.
        /// </summary>
        Root,

        /// <summary>
        /// The executing filter instance was materialized by the request's tenant service provider.
        /// </summary>
        Tenant,
    }
}
