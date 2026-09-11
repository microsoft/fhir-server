// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Api.Modules
{
    /// <summary>
    /// Logs the configured FHIR SDK provider and the seams controlled by it.
    /// </summary>
    public sealed class FhirSdkProviderStartupLogger : IHostedService
    {
        private readonly FhirSdkProviderConfiguration _configuration;
        private readonly ILogger<FhirSdkProviderStartupLogger> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirSdkProviderStartupLogger"/> class.
        /// </summary>
        /// <param name="configuration">The Core feature configuration.</param>
        /// <param name="logger">The startup logger.</param>
        public FhirSdkProviderStartupLogger(
            IOptions<CoreFeatureConfiguration> configuration,
            ILogger<FhirSdkProviderStartupLogger> logger)
        {
            _configuration = configuration.Value.FhirSdkProvider;
            _logger = logger;
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "FHIR SDK providers configured: Default={DefaultProvider}; Import={ImportProvider}; FHIRPath={FhirPathProvider}. FHIRPath Patch remains Firely-backed.",
                _configuration.Default,
                _configuration.EffectiveImport,
                _configuration.EffectiveFhirPath);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
