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

        private const int _exponentialBackoffBaseDelayMs = 100;
        private const int _exponentialMaxJitterMs = 300;
        private const int _exponentialMaxDelayMs = 60 * 1000;

        private const int _nonExponentialBaseDelayMs = 500;
        private const int _nonExponentialMaxJitterMs = 300;

        public RetryExceptionPolicyFactory(CosmosDataStoreConfiguration configuration, RequestContextAccessor<IFhirRequestContext> requestContextAccessor, ILogger<RetryExceptionPolicyFactory> logger)
        {
            _requestContextAccessor = EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));

            _sdkOnlyRetryPolicy = Policy.NoOpAsync();

            _bundleActionRetryPolicy = configuration.IndividualBatchActionRetryOptions.MaxNumberOfRetries > 0
                ? CreateExtendedRetryPolicy(configuration.IndividualBatchActionRetryOptions.MaxNumberOfRetries / configuration.RetryOptions.MaxNumberOfRetries, configuration.IndividualBatchActionRetryOptions.MaxWaitTimeInSeconds)
                : Policy.NoOpAsync();

            _backgroundJobRetryPolicy = CreateExtendedRetryPolicy(30, -1, true);
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

                // Exponential backoff is used for background jobs. Given current values, exponential backoff is used for the first 10 retries. After that a fixed wait time of_exponentialMaxDelayMs (60 seconds) is used.
                // Jitter is multiplied by the retry attempt to increase the randomness of the retry interval for longer retry delays (especially retry > 10).
                if (useExponentialRetry)
                {
                    // Calculate exponential backoff with a cap of 60 seconds
                    var backoff = Math.Min(Math.Pow(2, retryAttempt) * _exponentialBackoffBaseDelayMs, _exponentialMaxDelayMs);

                    var jitter = RandomNumberGenerator.GetInt32(0, _exponentialMaxJitterMs) * retryAttempt; // Add jitter in milliseconds
                    return TimeSpan.FromMilliseconds(backoff + jitter);
                }

                // Default logic: 500ms + retryAttempt * jitter
                var defaultJitter = RandomNumberGenerator.GetInt32(0, _nonExponentialMaxJitterMs) * retryAttempt; // Jitter scaled by retry attempt
                return TimeSpan.FromMilliseconds(_nonExponentialBaseDelayMs + defaultJitter);
            }

            // Retry recommendations for Cosmos DB: https://learn.microsoft.com/azure/cosmos-db/nosql/conceptual-resilient-sdk-applications#should-my-application-retry-on-errors
            return Policy.Handle<RequestRateExceededException>()
                .Or<CosmosException>(e => e.IsRequestRateExceeded())
                .Or<CosmosException>(e =>
                    e.StatusCode == HttpStatusCode.ServiceUnavailable ||
                    e.StatusCode == HttpStatusCode.TooManyRequests ||
                    e.StatusCode == HttpStatusCode.Gone ||
                    e.StatusCode == (HttpStatusCode)449 || // "Retry with" status code
                    e.StatusCode == HttpStatusCode.RequestTimeout)
                .WaitAndRetryAsync(
                    retryCount: maxRetries,
                    sleepDurationProvider: (retryAttempt, exception, context) => SleepDurationProvider(retryAttempt, exception),
                    onRetryAsync: (e, timeSpan, retryAttempt, ctx) =>
                    {
                        // Log details about each retry attempt for better visibility
                        string statusCode = "N/A";
                        string diagnostics = "N/A";

                        // Single type check for CosmosException to improve performance
                        if (e is CosmosException cosmosException)
                        {
                            statusCode = cosmosException.StatusCode.ToString();
                            diagnostics = cosmosException.Diagnostics?.ToString() ?? "empty";
                        }

                        var retryType = useExponentialRetry ? "exponential" : "fixed";
                        var waitTime = timeSpan.TotalMilliseconds;

                        _logger.LogWarning(
                                e,
                                "Cosmos DB operation failed. Retrying attempt {RetryAttempt}/{MaxRetries}. Status: {StatusCode}. Wait: {WaitTimeMs}ms ({RetryType}).",
                                retryAttempt,
                                maxRetries,
                                statusCode,
                                waitTime,
                                retryType);

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
