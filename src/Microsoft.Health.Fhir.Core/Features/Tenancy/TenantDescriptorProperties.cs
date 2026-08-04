// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Well-known keys for <see cref="TenantDescriptor.Properties"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="TenantDescriptor.Properties"/> is secret-free by contract. Never place connection
    /// strings, keys, or tokens here; use a dedicated provider abstraction instead.
    /// </remarks>
    public static class TenantDescriptorProperties
    {
        /// <summary>
        /// The OpenID Connect authority used to validate bearer tokens for the tenant.
        /// </summary>
        public const string Authority = "authority";

        /// <summary>
        /// The expected audience of bearer tokens for the tenant.
        /// </summary>
        public const string Audience = "audience";

        /// <summary>
        /// The identifier of the storage pool that backs the tenant.
        /// </summary>
        public const string Pool = "pool";
    }
}
