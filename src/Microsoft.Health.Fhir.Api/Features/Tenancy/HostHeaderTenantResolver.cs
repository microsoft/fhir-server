// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Core.Features.Tenancy;

namespace Microsoft.Health.Fhir.Api.Features.Tenancy
{
    /// <summary>
    /// Resolves the tenant from the request <c>Host</c> header by matching it against the
    /// host component of each registered tenant's base URI. Matching ignores case and port.
    /// </summary>
    public sealed class HostHeaderTenantResolver : ITenantResolver
    {
        private readonly ITenantRegistry _registry;
        private readonly object _lock = new();
        private Dictionary<string, TenantId> _hostToTenant;
        private int _snapshotCount = -1;

        /// <summary>
        /// Initializes a new instance of the <see cref="HostHeaderTenantResolver"/> class.
        /// </summary>
        /// <param name="registry">The tenant registry to resolve against.</param>
        public HostHeaderTenantResolver(ITenantRegistry registry)
        {
            EnsureArg.IsNotNull(registry, nameof(registry));
            _registry = registry;
        }

        /// <inheritdoc />
        public bool TryResolve(HttpContext httpContext, out TenantId tenantId)
        {
            EnsureArg.IsNotNull(httpContext, nameof(httpContext));

            string host = httpContext.Request.Host.Host;

            if (string.IsNullOrEmpty(host))
            {
                tenantId = default;
                return false;
            }

            return GetLookup().TryGetValue(host, out tenantId);
        }

        private Dictionary<string, TenantId> GetLookup()
        {
            IReadOnlyCollection<TenantDescriptor> tenants = _registry.Tenants;

            lock (_lock)
            {
                if (_hostToTenant == null || _snapshotCount != tenants.Count)
                {
                    var lookup = new Dictionary<string, TenantId>(StringComparer.OrdinalIgnoreCase);

                    foreach (TenantDescriptor tenant in tenants)
                    {
                        if (tenant.BaseUri != null)
                        {
                            lookup[tenant.BaseUri.Host] = tenant.TenantId;
                        }
                    }

                    _hostToTenant = lookup;
                    _snapshotCount = tenants.Count;
                }

                return _hostToTenant;
            }
        }
    }
}
