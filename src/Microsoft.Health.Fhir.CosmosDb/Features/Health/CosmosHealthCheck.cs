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
using Microsoft.Health.Fhir.Core.Features.Health;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Health
{
    /// <summary>
    /// Checks for the FHIR service health.
    /// </summary>
    public class CosmosHealthCheck : IHealthCheck
    {
        private readonly IDocumentClient _documentClient;
        private readonly CosmosDataStoreConfiguration _configuration;
        private readonly IDocumentClientTestProvider _testProvider;
        private readonly ILogger<CosmosHealthCheck> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosHealthCheck"/> class.
        /// </summary>
        /// <param name="documentClientFactory">The document client factory/</param>
        /// <param name="configuration">The CosmosDB configuration.</param>
        /// <param name="testProvider">The test provider</param>
        /// <param name="logger">The logger.</param>
        public CosmosHealthCheck(
            Func<IDocumentClient> documentClientFactory,
            CosmosDataStoreConfiguration configuration,
            IDocumentClientTestProvider testProvider,
            ILogger<CosmosHealthCheck> logger)
        {
            EnsureArg.IsNotNull(documentClientFactory, nameof(documentClientFactory));
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(testProvider, nameof(testProvider));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _documentClient = documentClientFactory.Invoke();

            EnsureArg.IsNotNull(_documentClient, optsFn: options => options.WithMessage("Factory returned null."));

            _configuration = configuration;
            _testProvider = testProvider;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                // Make a non-invasive query to CosmosDB to make sure we can reach the database.

                await _testProvider.PerformTest(_documentClient, _configuration);

                return HealthCheckResult.Healthy("Successfully connected to CosmosDB.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to CosmosDB.");

                return HealthCheckResult.Unhealthy("Failed to connect to CosmosDB.");
            }
        }
    }
}
