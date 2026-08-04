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
    /// This type is intended to carry tenant identity plus non-secret host metadata and exposes no behavior.
    /// Property keys are copied into a case-insensitive read-only dictionary.
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
        /// <exception cref="ArgumentException">Thrown when <paramref name="properties"/> contains keys that differ only by case and therefore collide during the case-insensitive defensive copy.</exception>
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
