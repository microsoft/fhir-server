// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Polly;
using Polly.Retry;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class RetryExceptionPolicyFactory
    {
        private const string RetryEndTimeContextKey = "RetryEndTime";
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly ILogger<RetryExceptionPolicyFactory> _logger;
        private readonly AsyncPolicy _sdkOnlyRetryPolicy;
        private readonly AsyncPolicy _bundleActionRetryPolicy;
        private readonly AsyncPolicy _backgroundJobRetryPolicy;

        public RetryExceptionPolicyFactory(CosmosDataStoreConfiguration configuration, RequestContextAccessor<IFhirRequestContext> requestContextAccessor, ILogger<RetryExceptionPolicyFactory> logger)
        {
            _requestContextAccessor = EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));

            _sdkOnlyRetryPolicy = Policy.NoOpAsync();

            _bundleActionRetryPolicy = configuration.IndividualBatchActionRetryOptions.MaxNumberOfRetries > 0
                ? CreateExtendedRetryPolicy(configuration.IndividualBatchActionRetryOptions.MaxNumberOfRetries / configuration.RetryOptions.MaxNumberOfRetries, configuration.IndividualBatchActionRetryOptions.MaxWaitTimeInSeconds)
                : Policy.NoOpAsync();

            _backgroundJobRetryPolicy = CreateExtendedRetryPolicy(100, -1, true);
        }

        public AsyncPolicy RetryPolicy
        {
            get
            {
                return _requestContextAccessor.RequestContext switch
                {
                    null or { IsBackgroundTask: true } => _backgroundJobRetryPolicy,
                    { ExecutingBatchOrTransaction: true } => _bundleActionRetryPolicy,
                    _ => _sdkOnlyRetryPolicy,
                };
            }
        }

        public AsyncPolicy BackgroundWorkerRetryPolicy => _backgroundJobRetryPolicy;

        private AsyncRetryPolicy CreateExtendedRetryPolicy(int maxRetries, int maxWaitTimeInSeconds, bool useExponentialRetry = false)
        {
            // Define a sleep duration provider based on the retry strategy
            TimeSpan SleepDurationProvider(int retryAttempt, Exception exception)
            {
                // Respect x-ms-retry-after-ms from RequestRateExceededException
                if (exception.AsRequestRateExceeded()?.RetryAfter is TimeSpan retryAfter)
                {
                    return retryAfter;
                }

                // Respect x-ms-retry-after-ms from CosmosException
                if (exception is CosmosException cosmosException && cosmosException.StatusCode == HttpStatusCode.TooManyRequests && cosmosException.RetryAfter.HasValue)
                {
                    return cosmosException.RetryAfter.Value;
                }

                if (useExponentialRetry)
                {
                    // Exponential backoff with jitter
                    var backoff = Math.Pow(2, retryAttempt) * 100; // Exponential backoff in milliseconds
                    var jitter = RandomNumberGenerator.GetInt32(0, 300); // Add jitter in milliseconds
                    return TimeSpan.FromMilliseconds(backoff + jitter);
                }

                // Default fixed wait time
                return TimeSpan.FromSeconds(2);
            }

            // Retry recommendations for Cosmos DB: https://learn.microsoft.com/azure/cosmos-db/nosql/conceptual-resilient-sdk-applications#should-my-application-retry-on-errors
            return Policy.Handle<RequestRateExceededException>()
                .Or<CosmosException>(e => e.IsRequestRateExceeded())
                .Or<CosmosException>(e =>
                    e.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                    e.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                    e.StatusCode == System.Net.HttpStatusCode.Gone ||
                    e.StatusCode == (HttpStatusCode)449 || // "Retry with" status code
                    e.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
                .WaitAndRetryAsync(
                    retryCount: maxRetries,
                    sleepDurationProvider: (retryAttempt, exception, context) => SleepDurationProvider(retryAttempt, exception),
                    onRetryAsync: (e, _, _, ctx) =>
                    {
                        if (e is CosmosException cosmosException)
                        {
                            if (cosmosException.StatusCode == HttpStatusCode.ServiceUnavailable)
                            {
                                var diagnostics = cosmosException.Diagnostics.ToString();
                                _logger.LogWarning(cosmosException, "Received a ServiceUnavailable response from Cosmos DB. Retrying. Diagnostics: {CosmosDiagnostics}", diagnostics);
                            }
                        }

                        if (maxWaitTimeInSeconds == -1)
                        {
                            // no timeout
                            return Task.CompletedTask;
                        }

                        if (ctx.TryGetValue(RetryEndTimeContextKey, out var endTimeObj))
                        {
                            if (DateTime.UtcNow >= (DateTime)endTimeObj)
                            {
                                ExceptionDispatchInfo.Throw(e);
                            }

                            // otherwise, we have enough time to retry
                        }
                        else
                        {
                            ctx.Add(RetryEndTimeContextKey, DateTime.UtcNow.AddSeconds(maxWaitTimeInSeconds));
                        }

                        return Task.CompletedTask;
                    });
        }
    }
}
