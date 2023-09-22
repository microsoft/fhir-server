// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Health
{
    public class CosmosHealthCheck : IHealthCheck
    {
        private readonly IScoped<Container> _container;
        private readonly CosmosDataStoreConfiguration _configuration;
        private readonly CosmosCollectionConfiguration _cosmosCollectionConfiguration;
        private readonly ICosmosClientTestProvider _testProvider;
        private readonly ILogger<CosmosHealthCheck> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosHealthCheck"/> class.
        /// </summary>
        /// <param name="container">The Cosmos Container factory/</param>
        /// <param name="configuration">The CosmosDB configuration.</param>
        /// <param name="namedCosmosCollectionConfigurationAccessor">The IOptions accessor to get a named version.</param>
        /// <param name="testProvider">The test provider</param>
        /// <param name="logger">The logger.</param>
        public CosmosHealthCheck(
            IScoped<Container> container,
            CosmosDataStoreConfiguration configuration,
            IOptionsSnapshot<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor,
            ICosmosClientTestProvider testProvider,
            ILogger<CosmosHealthCheck> logger)
        {
            EnsureArg.IsNotNull(container, nameof(container));
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(namedCosmosCollectionConfigurationAccessor, nameof(namedCosmosCollectionConfigurationAccessor));
            EnsureArg.IsNotNull(testProvider, nameof(testProvider));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _container = container;
            _configuration = configuration;
            _cosmosCollectionConfiguration = namedCosmosCollectionConfigurationAccessor.Get(Constants.CollectionConfigurationName);
            _testProvider = testProvider;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            const int maxExecutionTimeInSeconds = 30;
            const int maxNumberAttempts = 3;
            int attempt = 0;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using (CancellationTokenSource timeBasedTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(maxExecutionTimeInSeconds)))
                    using (CancellationTokenSource operationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeBasedTokenSource.Token))
                    {
                        await _testProvider.PerformTestAsync(_container.Value, _configuration, _cosmosCollectionConfiguration, operationTokenSource.Token);
                        return HealthCheckResult.Healthy("Successfully connected to the data store.");
                    }
                }
                catch (CosmosOperationCanceledException coce)
                {
                    // CosmosOperationCanceledException are "safe to retry on and can be treated as timeouts from the retrying perspective.".
                    // Reference: https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/troubleshoot-dotnet-sdk-request-timeout?tabs=cpu-new
                    attempt++;

                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogWarning(coce, "Failed to connect to the data store. External cancellation requested.");
                        return HealthCheckResult.Unhealthy("Failed to connect to the data store. External cancellation requested.");
                    }
                    else if (attempt >= maxNumberAttempts)
                    {
                        _logger.LogWarning(coce, "Failed to connect to the data store. There were {NumberOfAttempts} attempts to connect to the data store, but they suffered a '{ExceptionType}'.", attempt, nameof(CosmosOperationCanceledException));
                        return HealthCheckResult.Degraded("Failed to connect to the data store. Operation canceled.");
                    }
                    else
                    {
                        // Number of attempts not reached. Allow retry.
                        _logger.LogWarning(coce, "Failed to connect to the data store. Attempt {NumberOfAttempts}. '{ExceptionType}'.", attempt, nameof(CosmosOperationCanceledException));
                    }
                }
                catch (CosmosException ex) when (ex.IsCmkClientError())
                {
                    return HealthCheckResult.Degraded(
                        "Connection to the data store was unsuccesful because the client's customer-managed key is not available.",
                        exception: ex,
                        new Dictionary<string, object>() { { "IsCustomerManagedKeyError", true } });
                }
                catch (Exception ex) when (ex.IsRequestRateExceeded())
                {
                    return HealthCheckResult.Degraded("Connection to the data store was successful, however, the rate limit has been exceeded.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to connect to the data store.");

                    return HealthCheckResult.Unhealthy("Failed to connect to the data store.");
                }
            }
            while (true);
        }
    }
}
