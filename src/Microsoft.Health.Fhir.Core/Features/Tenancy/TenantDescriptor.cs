// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Describes a tenant served by this process.
    /// </summary>
    /// <remarks>
    /// This type contains only tenant identity and host-specific metadata. It deliberately carries no secrets and
    /// exposes no behavior.
    /// </remarks>
    public sealed class TenantDescriptor
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyProperties =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantDescriptor"/> class.
        /// </summary>
        /// <param name="tenantId">The tenant identifier.</param>
        /// <param name="baseUri">The public base URI for the tenant, or <c>null</c> if unknown.</param>
        /// <param name="properties">Host-specific properties for the tenant, or <c>null</c> for none.</param>
        public TenantDescriptor(TenantId tenantId, Uri baseUri = null, IReadOnlyDictionary<string, string> properties = null)
        {
            TenantId = tenantId;
            BaseUri = baseUri;
            Properties = properties == null
                ? EmptyProperties
                : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets the tenant identifier.
        /// </summary>
        public TenantId TenantId { get; }

        /// <summary>
        /// Gets the public base URI for the tenant, or <c>null</c> if unknown.
        /// </summary>
        public Uri BaseUri { get; }

        /// <summary>
        /// Gets the host-specific properties for the tenant.
        /// </summary>
        public IReadOnlyDictionary<string, string> Properties { get; }
    }
}
