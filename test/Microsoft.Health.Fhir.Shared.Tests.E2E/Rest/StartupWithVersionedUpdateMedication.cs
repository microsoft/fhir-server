// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Configures versioned-update Medication tests in an isolated in-process database.
    /// </summary>
    [RequiresIsolatedDatabase]
    public class StartupWithVersionedUpdateMedication : StartupBaseForCustomProviders
    {
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="StartupWithVersionedUpdateMedication"/> class.
        /// </summary>
        /// <param name="configuration">The test server configuration.</param>
        public StartupWithVersionedUpdateMedication(IConfiguration configuration)
            : base(configuration)
        {
            _configuration = configuration;
        }

        /// <inheritdoc />
        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);

            var coreFeatures = new CoreFeatureConfiguration();
            _configuration.GetSection("FhirServer:CoreFeatures").Bind(coreFeatures);
            coreFeatures.Versioning.ResourceTypeOverrides["Medication"] = "versioned-update";

            IOptions<CoreFeatureConfiguration> coreFeatureOptions = Options.Create(coreFeatures);
            services.Replace(new ServiceDescriptor(typeof(IOptions<CoreFeatureConfiguration>), coreFeatureOptions));
        }
    }
}
