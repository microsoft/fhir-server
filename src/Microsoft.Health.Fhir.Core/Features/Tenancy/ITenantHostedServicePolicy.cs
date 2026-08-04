// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Classifies hosted services registered in the root composition root so tenant container
    /// construction knows which of them to start per tenant.
    /// </summary>
    public interface ITenantHostedServicePolicy
    {
        /// <summary>
        /// Classifies the hosted service with the supplied full type name.
        /// </summary>
        /// <param name="hostedServiceTypeName">The full name of the hosted service implementation type.</param>
        /// <returns>The disposition for that service.</returns>
        /// <exception cref="TenantHostedServiceNotClassifiedException">
        /// Thrown when the service has no classification.
        /// </exception>
        TenantHostedServiceDisposition Classify(string hostedServiceTypeName);

        /// <summary>
        /// Sets or overrides the classification for a hosted service type.
        /// </summary>
        /// <param name="hostedServiceTypeName">The full name of the hosted service implementation type.</param>
        /// <param name="disposition">The disposition to apply.</param>
        void Set(string hostedServiceTypeName, TenantHostedServiceDisposition disposition);
    }
}
