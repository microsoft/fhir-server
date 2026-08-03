// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Tenancy;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    /// <summary>
    /// Provides global, thread-safe access to FHIR server instance configuration (base URI, etc.)
    /// that persists across requests and is available to background tasks that execute outside the HTTP request context.
    ///
    /// This is a singleton service that is populated on the first HTTP request via the middleware and then remains
    /// available for background operations like reindexing that don't have access to the RequestContextAccessor.
    ///
    /// Values are stored per tenant (see <see cref="ITenantContextAccessor"/>). In a process that serves a single
    /// FHIR service every caller observes <see cref="TenantId.Default"/>, so behavior is identical to a
    /// single-valued cache. In a process that serves several tenants, each tenant latches its own base URI and
    /// cannot observe another tenant's.
    ///
    /// This design ensures:
    /// - Minimal performance impact (lazy-initialization pattern)
    /// - Thread-safe access (ConcurrentDictionary; first writer per tenant wins)
    /// - No per-request overhead (values are captured once per tenant and reused)
    /// - Available to background services and job processing
    /// </summary>
    public class FhirServerInstanceConfiguration : IFhirServerInstanceConfiguration
    {
        private readonly ITenantContextAccessor _tenantContextAccessor;
        private readonly ConcurrentDictionary<TenantId, Uri> _baseUriByTenant = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirServerInstanceConfiguration"/> class.
        /// </summary>
        /// <param name="tenantContextAccessor">Supplies the tenant that the current operation belongs to.</param>
        public FhirServerInstanceConfiguration(ITenantContextAccessor tenantContextAccessor)
        {
            EnsureArg.IsNotNull(tenantContextAccessor, nameof(tenantContextAccessor));

            _tenantContextAccessor = tenantContextAccessor;
        }

        /// <summary>
        /// Gets the base URI of the FHIR server instance for the current tenant.
        /// Populated on that tenant's first HTTP request and cached for the lifetime of the application.
        /// Returns <c>null</c> when the current tenant has not served a request yet.
        /// </summary>
        public Uri BaseUri =>
            _baseUriByTenant.TryGetValue(_tenantContextAccessor.Current, out Uri baseUri) ? baseUri : null;

        /// <summary>
        /// Initializes the base URI of the instance configuration for the current tenant.
        /// This method is idempotent and thread-safe - only the first caller for a given tenant will succeed
        /// in setting the value.
        /// </summary>
        /// <param name="baseUriString">The base URI string of the FHIR server.</param>
        /// <returns>True if the base URI is initialized for the current tenant (either by this call or a previous call); false if the URI is invalid.</returns>
        public bool InitializeBaseUri(string baseUriString)
        {
            EnsureArg.IsNotNullOrWhiteSpace(baseUriString, nameof(baseUriString));

            TenantId tenantId = _tenantContextAccessor.Current;

            if (!Uri.TryCreate(baseUriString, UriKind.Absolute, out Uri baseUri))
            {
                return _baseUriByTenant.ContainsKey(tenantId);
            }

            _baseUriByTenant.GetOrAdd(tenantId, baseUri);

            return true;
        }
    }
}
