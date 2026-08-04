// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Thrown when a hosted service registered in the root composition root has no entry in the tenant
    /// hosted service policy, so it is not known whether it should run once per process or once per tenant.
    /// </summary>
    public class TenantHostedServiceNotClassifiedException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TenantHostedServiceNotClassifiedException"/> class.
        /// </summary>
        /// <param name="hostedServiceTypeName">The full name of the unclassified hosted service type.</param>
        public TenantHostedServiceNotClassifiedException(string hostedServiceTypeName)
            : base(FormatMessage(EnsureArg.IsNotNullOrWhiteSpace(hostedServiceTypeName, nameof(hostedServiceTypeName))))
        {
            HostedServiceTypeName = hostedServiceTypeName;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantHostedServiceNotClassifiedException"/> class.
        /// </summary>
        public TenantHostedServiceNotClassifiedException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantHostedServiceNotClassifiedException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The inner exception.</param>
        public TenantHostedServiceNotClassifiedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Gets the full name of the hosted service type that could not be classified.
        /// </summary>
        public string HostedServiceTypeName { get; }

        private static string FormatMessage(string hostedServiceTypeName) =>
            $"The hosted service '{hostedServiceTypeName}' has no entry in the tenant hosted service policy. " +
            "Every IHostedService must be explicitly classified as Shared, Relocated, or PerTenantInitializer " +
            "before tenant containers can be created. Call ITenantHostedServicePolicy.Set to classify it.";
    }
}
