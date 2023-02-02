// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Polly;
using Polly.Retry;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class RetryExceptionPolicyFactory
    {
        private const string RetryEndTimeContextKey = "RetryEndTime";
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly AsyncPolicy _sdkOnlyRetryPolicy;
        private readonly AsyncPolicy _bundleActionRetryPolicy;
        private readonly AsyncPolicy _backgroundJobRetryPolicy;

        public RetryExceptionPolicyFactory(CosmosDataStoreConfiguration configuration, RequestContextAccessor<IFhirRequestContext> requestContextAccessor)
        {
            _requestContextAccessor = requestContextAccessor;
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));

            _sdkOnlyRetryPolicy = Policy.NoOpAsync();

            _bundleActionRetryPolicy = configuration.IndividualBatchActionRetryOptions.MaxNumberOfRetries > 0
                ? CreateExtendedRetryPolicy(configuration.IndividualBatchActionRetryOptions.MaxNumberOfRetries / configuration.RetryOptions.MaxNumberOfRetries, configuration.IndividualBatchActionRetryOptions.MaxWaitTimeInSeconds)
                : Policy.NoOpAsync();

            _backgroundJobRetryPolicy = CreateExtendedRetryPolicy(100, -1);
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

        private static AsyncRetryPolicy CreateExtendedRetryPolicy(int maxRetries, int maxWaitTimeInSeconds)
        {
            return Policy.Handle<RequestRateExceededException>()
                .Or<CosmosException>(e => e.IsRequestRateExceeded())
                .Or<CosmosException>(e => (e.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || e.StatusCode == System.Net.HttpStatusCode.RequestTimeout))
                .WaitAndRetryAsync(
                    retryCount: maxRetries,
                    sleepDurationProvider: (_, e, _) => e.AsRequestRateExceeded()?.RetryAfter ?? TimeSpan.FromSeconds(2),
                    onRetryAsync: (e, _, _, ctx) =>
                    {
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
