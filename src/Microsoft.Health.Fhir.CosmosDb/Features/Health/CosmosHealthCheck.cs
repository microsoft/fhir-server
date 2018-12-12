// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Health;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Health
{
    /// <summary>
    /// Checks for the FHIR service health.
    /// </summary>
    public class CosmosHealthCheck : IHealthCheck
    {
        private readonly IScoped<IDocumentClient> _documentClient;
        private readonly CosmosDataStoreConfiguration _configuration;
        private readonly IDocumentClientTestProvider _testProvider;
        private readonly ILogger<CosmosHealthCheck> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosHealthCheck"/> class.
        /// </summary>
        /// <param name="documentClient">The document client factory/</param>
        /// <param name="configuration">The CosmosDB configuration.</param>
        /// <param name="testProvider">The test provider</param>
        /// <param name="logger">The logger.</param>
        public CosmosHealthCheck(
            IScoped<IDocumentClient> documentClient,
            CosmosDataStoreConfiguration configuration,
            IDocumentClientTestProvider testProvider,
            ILogger<CosmosHealthCheck> logger)
        {
            EnsureArg.IsNotNull(documentClient, nameof(documentClient));
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(testProvider, nameof(testProvider));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _documentClient = documentClient;

            EnsureArg.IsNotNull(_documentClient, optsFn: options => options.WithMessage("Factory returned null."));

            _configuration = configuration;
            _testProvider = testProvider;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                // Make a non-invasive query to make sure we can reach the data store.

                await _testProvider.PerformTest(_documentClient.Value, _configuration);

                return HealthCheckResult.Healthy("Successfully connected to the data store.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to the data store.");

                return HealthCheckResult.Unhealthy("Failed to connect to the data store.");
            }
        }
    }
}
