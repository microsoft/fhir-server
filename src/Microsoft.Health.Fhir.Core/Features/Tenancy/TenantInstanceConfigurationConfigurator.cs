// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Sets the server instance base URI for a tenant container from the tenant descriptor, so generated
    /// resource URLs, bundle links, and the capability statement reflect the tenant's own host.
    /// </summary>
    /// <remarks>
    /// A fresh <see cref="FhirServerInstanceConfiguration"/> is created per tenant, and the shared ambient
    /// tenant accessor is temporarily switched while the base URI is initialized so the value is stored
    /// under the tenant's key.
    /// </remarks>
    public sealed class TenantInstanceConfigurationConfigurator : ITenantServiceConfigurator
    {
        private readonly ITenantContextAccessor _tenantContextAccessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantInstanceConfigurationConfigurator"/> class.
        /// </summary>
        /// <param name="tenantContextAccessor">The shared ambient tenant accessor used to scope instance configuration to a tenant.</param>
        public TenantInstanceConfigurationConfigurator(ITenantContextAccessor tenantContextAccessor)
        {
            ArgumentNullException.ThrowIfNull(tenantContextAccessor);

            _tenantContextAccessor = tenantContextAccessor;
        }

        /// <inheritdoc />
        public void Configure(IServiceCollection services, TenantDescriptor tenant)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(tenant);

            TenantId priorTenant = _tenantContextAccessor.Current;

            try
            {
                _tenantContextAccessor.SetCurrent(tenant.TenantId);

                FhirServerInstanceConfiguration instanceConfiguration = new(_tenantContextAccessor);

                if (tenant.BaseUri is not null)
                {
                    instanceConfiguration.InitializeBaseUri(tenant.BaseUri.AbsoluteUri);
                }

                services.RemoveAll<IFhirServerInstanceConfiguration>();
                services.AddSingleton<IFhirServerInstanceConfiguration>(instanceConfiguration);
            }
            finally
            {
                _tenantContextAccessor.SetCurrent(priorTenant);
            }
        }
    }
}
