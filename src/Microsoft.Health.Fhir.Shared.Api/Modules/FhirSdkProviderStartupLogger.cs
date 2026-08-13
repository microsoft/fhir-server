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
    /// Logs the FHIR SDK provider resolved for each migrated feature seam.
    /// </summary>
    /// <remarks>
    /// Every migrated seam is listed with the provider it actually resolved to, rather than just the configured
    /// default. That keeps the per-seam overrides visible, and it is also the signal that a configuration written
    /// against the earlier scalar <c>FhirSdkProvider</c> setting has not bound - such a value leaves every seam on
    /// Firely, which would otherwise be silent.
    /// </remarks>
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
                "FHIR SDK provider default: {FhirSdkProviderDefault}. Migrated seams: Import={ImportProvider}, FhirPath={FhirPathProvider}, Serialization={SerializationProvider}. All other seams remain Firely-backed.",
                _configuration.Default,
                _configuration.EffectiveImport,
                _configuration.EffectiveFhirPath,
                _configuration.EffectiveSerialization);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
