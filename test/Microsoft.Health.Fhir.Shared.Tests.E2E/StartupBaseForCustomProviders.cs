// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Web;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E
{
    public class StartupBaseForCustomProviders : Startup
    {
        public StartupBaseForCustomProviders(IConfiguration configuration)
            : base(configuration)
        {
        }

        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);

            // When using custom Startup classes from a different assembly the
            // FHIR controllers from the API assemblies are not automatically registered.
            services.AddMvc()
                .AddApplicationPart(typeof(FhirController).Assembly)
                .AddApplicationPart(typeof(AadSmartOnFhirProxyController).Assembly);
        }
    }
}
