// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Applies tenant-specific changes to the service collection used to build a tenant container.
    /// </summary>
    public interface ITenantServiceConfigurator
    {
        /// <summary>
        /// Configures the services for a tenant container.
        /// </summary>
        /// <param name="services">The tenant service collection, ready to be built.</param>
        /// <param name="tenant">The tenant the container is being built for.</param>
        void Configure(IServiceCollection services, TenantDescriptor tenant);
    }
}
