// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Features.Tenants
{
    /// <summary>
    /// Holds a snapshot of tenant settings loaded at startup.
    /// </summary>
    public sealed class TenantManager : ITenantManager
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TenantManager"/> class.
        /// </summary>
        /// <param name="configuration">Tenant settings loaded at startup.</param>
        public TenantManager(IOptions<TenantConfiguration> configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            configuration.Value.Validate();
            Tenants = configuration.Value.Tenants.ToFrozenDictionary(tenant => tenant.Id, StringComparer.Ordinal);
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, TenantInfo> Tenants { get; }
    }
}
