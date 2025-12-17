// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Multitenant.Core;

/// <summary>
/// Interface for managing tenant instances in the multitenant FHIR server.
/// </summary>
public interface ITenantManager
{
    /// <summary>
    /// Gets the port number for a specific tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>The port number if the tenant exists; otherwise, null.</returns>
    int? GetTenantPort(string tenantId);

    /// <summary>
    /// Gets all tenant configurations.
    /// </summary>
    /// <returns>A read-only collection of tenant configurations.</returns>
    IReadOnlyCollection<TenantConfiguration> GetTenants();

    /// <summary>
    /// Checks if a tenant exists.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>True if the tenant exists; otherwise, false.</returns>
    bool TenantExists(string tenantId);
}
