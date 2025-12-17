// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Multitenant.Core;

/// <summary>
/// Default implementation of <see cref="ITenantManager"/>.
/// </summary>
public class TenantManager : ITenantManager
{
    private readonly Dictionary<string, int> _tenantPorts;
    private readonly List<TenantConfiguration> _tenants;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantManager"/> class.
    /// </summary>
    /// <param name="settings">The multitenant settings.</param>
    public TenantManager(MultitenantSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _tenants = settings.Tenants;
        _tenantPorts = settings.Tenants.ToDictionary(
            t => t.TenantId,
            t => t.Port,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public int? GetTenantPort(string tenantId)
    {
        return _tenantPorts.TryGetValue(tenantId, out var port) ? port : null;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<TenantConfiguration> GetTenants()
    {
        return _tenants.AsReadOnly();
    }

    /// <inheritdoc/>
    public bool TenantExists(string tenantId)
    {
        return _tenantPorts.ContainsKey(tenantId);
    }
}
