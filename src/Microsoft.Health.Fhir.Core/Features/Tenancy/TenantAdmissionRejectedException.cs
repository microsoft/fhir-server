// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Thrown when a tenant container cannot be admitted because the resident cap is reached and every
    /// resident tenant has work in flight. This is a load-shed signal, surfaced to callers as HTTP 503.
    /// </summary>
    public class TenantAdmissionRejectedException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TenantAdmissionRejectedException"/> class.
        /// </summary>
        public TenantAdmissionRejectedException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantAdmissionRejectedException"/> class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        public TenantAdmissionRejectedException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantAdmissionRejectedException"/> class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="innerException">The inner exception.</param>
        public TenantAdmissionRejectedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantAdmissionRejectedException"/> class.
        /// </summary>
        /// <param name="tenantId">The tenant that could not be admitted.</param>
        /// <param name="maxResidentTenants">The configured resident cap.</param>
        public TenantAdmissionRejectedException(TenantId tenantId, int maxResidentTenants)
            : base($"Tenant '{tenantId}' could not be admitted: all {maxResidentTenants} resident tenant containers are in use.")
        {
            TenantId = tenantId;
            MaxResidentTenants = maxResidentTenants;
        }

        /// <summary>
        /// Gets the tenant that could not be admitted.
        /// </summary>
        public TenantId TenantId { get; }

        /// <summary>
        /// Gets the configured resident cap.
        /// </summary>
        public int MaxResidentTenants { get; }
    }
}
