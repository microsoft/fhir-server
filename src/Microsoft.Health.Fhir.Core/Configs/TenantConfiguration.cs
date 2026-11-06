// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Tenants;

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Configures the tenants available when the server starts.
    /// </summary>
    public class TenantConfiguration
    {
        /// <summary>
        /// Gets the tenants. Changes require restarting the server.
        /// </summary>
        public IList<TenantInfo> Tenants { get; } = new List<TenantInfo>();

        /// <summary>
        /// Validates required tenant settings and unique, URL-safe tenant identifiers.
        /// </summary>
        public void Validate()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (TenantInfo tenant in Tenants)
            {
                if (tenant == null || string.IsNullOrWhiteSpace(tenant.Id) ||
                    tenant.Id is "." or ".." || !tenant.Id.All(IsUnreserved))
                {
                    throw new InvalidOperationException("FhirServer:TenantConfiguration:Tenants requires an Id containing only ASCII letters, digits, '-', '_', '.', or '~', other than '.' or '..'.");
                }

                if (!ids.Add(tenant.Id))
                {
                    throw new InvalidOperationException($"FhirServer:TenantConfiguration:Tenants contains a duplicate Id '{tenant.Id}'.");
                }

                if (string.IsNullOrWhiteSpace(tenant.DatabaseName))
                {
                    throw new InvalidOperationException($"FhirServer:TenantConfiguration:Tenants requires a DatabaseName for tenant '{tenant.Id}'.");
                }
            }
        }

        private static bool IsUnreserved(char value) =>
            char.IsAsciiLetterOrDigit(value) || value is '-' or '_' or '.' or '~';
    }
}
