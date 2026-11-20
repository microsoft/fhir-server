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
using Microsoft.Health.Fhir.Core.Features.Persistence;

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
        /// <param name="rawResourceFactory">The persistence writer to activate before starting hosted services.</param>
        public FhirSdkProviderStartupLogger(
            IOptions<CoreFeatureConfiguration> configuration,
            ILogger<FhirSdkProviderStartupLogger> logger,
            IRawResourceFactory rawResourceFactory)
        {
            _ = rawResourceFactory;
            _configuration = configuration.Value.FhirSdkProvider;
            _logger = logger;
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "FHIR SDK providers configured: SystemDefault={SystemDefaultProvider}; Import={ImportProvider}; FHIRPath={FhirPathProvider}; PersistenceWrite={PersistenceWriteProvider}. When PersistenceWrite is Ignixa, non-native inputs are still written by Firely. FHIRPath Patch remains Firely-backed.",
                _configuration.SystemDefault,
                _configuration.EffectiveImport,
                _configuration.EffectiveFhirPath,
                _configuration.EffectivePersistenceWrite);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
