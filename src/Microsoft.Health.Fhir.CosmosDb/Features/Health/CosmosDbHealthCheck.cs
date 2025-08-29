// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Health;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Health;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Health
{
    public class CosmosDbHealthCheck : IHealthCheck
    {
        private const string UnhealthyDescription = "The store is unhealthy.";
        private const string DegradedDescription = "The health of the store has degraded.";
        private const string CMKErrorDescription = "Connection to the data store was unsuccesful because the client's customer-managed key is not available.";

        private readonly IScoped<Container> _container;
        private readonly CosmosDataStoreConfiguration _configuration;
        private readonly CosmosCollectionConfiguration _cosmosCollectionConfiguration;
        private readonly ICosmosClientTestProvider _testProvider;
        private readonly ILogger<CosmosDbHealthCheck> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosDbHealthCheck"/> class.
        /// </summary>
        /// <param name="container">The Cosmos Container factory/</param>
        /// <param name="configuration">The CosmosDB configuration.</param>
        /// <param name="namedCosmosCollectionConfigurationAccessor">The IOptions accessor to get a named version.</param>
        /// <param name="testProvider">The test provider</param>
        /// <param name="logger">The logger.</param>
        public CosmosDbHealthCheck(
            IScoped<Container> container,
            CosmosDataStoreConfiguration configuration,
            IOptionsSnapshot<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor,
            ICosmosClientTestProvider testProvider,
            ILogger<CosmosDbHealthCheck> logger)
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

            // CosmosOperationCanceledException are "safe to retry on and can be treated as timeouts from the retrying perspective.".
            // Reference: https://learn.microsoft.com/azure/cosmos-db/nosql/troubleshoot-dotnet-sdk-request-timeout?tabs=cpu-new
            // Cosmos 503 and 449 are transient errors that can be retried.
            // Reference: https://learn.microsoft.com/azure/cosmos-db/nosql/conceptual-resilient-sdk-applications#should-my-application-retry-on-errors
            static bool IsRetryableException(Exception ex) =>
                ex is CosmosOperationCanceledException ||
                (ex is CosmosException cex && (cex.StatusCode == HttpStatusCode.ServiceUnavailable || cex.StatusCode == (HttpStatusCode)449));

            // Adds the CosmosDiagnostics to the log message if the exception is a CosmosException.
            // This avoids truncation of the diagnostics details in the exception by moving it to the properties bag.
            void LogWithDetails(LogLevel logLevel, Exception ex, string message, IEnumerable<object> logArgs = null)
            {
                logArgs ??= [];

                if (ex is CosmosException cosmosException && cosmosException.Diagnostics is not null)
                {
                    message = message + " CosmosDiagnostics: {CosmosDiagnostics}";
                    logArgs = logArgs.Append(cosmosException.Diagnostics.ToString());
                }

                _logger.Log(logLevel, ex, message, logArgs.ToArray());
            }

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var timeBasedTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(maxExecutionTimeInSeconds));
                    using var operationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeBasedTokenSource.Token);
                    await _testProvider.PerformTestAsync(_container.Value, operationTokenSource.Token);
                    return HealthCheckResult.Healthy("Successfully connected.");
                }
                catch (Exception ex) when (IsRetryableException(ex))
                {
                    attempt++;

                    if (cancellationToken.IsCancellationRequested)
                    {
                        // Handling an extenal cancellation.
                        // No reasons to retry as the cancellation was external to the health check.

                        LogWithDetails(LogLevel.Warning, ex, "Failed to connect to the data store. External cancellation requested.");

                        return HealthCheckResult.Unhealthy(
                            description: UnhealthyDescription,
                            data: new Dictionary<string, object>
                            {
                                { "Reason", HealthStatusReason.ServiceUnavailable },
                                { "Error", FhirHealthErrorCode.Error408.ToString() },
                            });
                    }
                    else if (attempt >= maxNumberAttempts)
                    {
                        // This is a very rare situation. This condition indicates that multiple attempts to connect to the data store happened, but they were not successful.

                        LogWithDetails(
                            LogLevel.Warning,
                            ex,
                            "Failed to connect to the data store. There were {NumberOfAttempts} attempts to connect to the data store, but they suffered a '{ExceptionType}'.",
                            [attempt, ex.GetType().Name]);

                        return HealthCheckResult.Unhealthy(
                            description: UnhealthyDescription,
                            data: new Dictionary<string, object>
                            {
                                { "Reason", HealthStatusReason.ServiceUnavailable },
                                { "Error", FhirHealthErrorCode.Error501.ToString() },
                            });
                    }
                    else
                    {
                        // Number of attempts not reached. Allow retry.
                        LogWithDetails(
                            LogLevel.Warning,
                            ex,
                            "Failed to connect to the data store. Attempt {NumberOfAttempts}. '{ExceptionType}'.",
                            [attempt, ex.GetType().Name]);
                    }
                }
                catch (CosmosException ex) when (ex.IsCmkClientError())
                {
                    // Handling CMK errors.
                    LogWithDetails(
                        LogLevel.Warning,
                        ex,
                        CMKErrorDescription);

                    return HealthCheckResult.Degraded(
                        description: DegradedDescription,
                        data: new Dictionary<string, object>
                        {
                            { "Reason", HealthStatusReason.CustomerManagedKeyAccessLost },
                            { "Error", FhirHealthErrorCode.Error412.ToString() },
                        });
                }
                catch (CustomerManagedKeyException ex)
                {
                    LogWithDetails(
                        LogLevel.Warning,
                        ex,
                        CMKErrorDescription);

                    return HealthCheckResult.Degraded(
                        description: DegradedDescription,
                        data: new Dictionary<string, object>
                        {
                            { "Reason", ex.Message },
                            { "Error", FhirHealthErrorCode.Error412.ToString() },
                        });
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.RequestTimeout)
                {
                    // Handling timeout exceptions

                    LogWithDetails(
                        LogLevel.Warning,
                        ex,
                        "Failed to connect to the data store. Request has timed out.");

                    return HealthCheckResult.Degraded(
                        description: DegradedDescription,
                        data: new Dictionary<string, object>
                        {
                            { "Reason", HealthStatusReason.ServiceDegraded },
                            { "Error", FhirHealthErrorCode.Error408.ToString() },
                        });
                }
                catch (Exception ex) when (ex.IsRequestRateExceeded())
                {
                    // Handling request rate exceptions.

                    LogWithDetails(
                        LogLevel.Warning,
                        ex,
                        "Failed to connect to the data store. Rate limit has been exceeded.");

                    return HealthCheckResult.Degraded(
                        description: DegradedDescription,
                        data: new Dictionary<string, object>
                        {
                            { "Reason", HealthStatusReason.ServiceDegraded },
                            { "Error", FhirHealthErrorCode.Error429.ToString() },
                        });
                }
                catch (Exception ex)
                {
                    // Handling other exceptions.

                    const string message = "Failed to connect to the data store.";
                    LogWithDetails(LogLevel.Warning, ex, message);

                    return HealthCheckResult.Unhealthy(
                        description: UnhealthyDescription,
                        data: new Dictionary<string, object>
                        {
                            { "Reason", HealthStatusReason.ServiceUnavailable },
                            { "Error", FhirHealthErrorCode.Error500.ToString() },
                        });
                }
            }
            while (true);
        }
    }
}
