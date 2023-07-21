// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Azure;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Polly;
using Polly.Retry;

namespace Microsoft.Health.Fhir.Azure.IntegrationDataStore
{
    public class IntegrationStoreRetryExceptionPolicyFactory
    {
        private const string RetryEndTimeContextKey = "RetryEndTime";

        private AsyncRetryPolicy _retryPolicy;

        public IntegrationStoreRetryExceptionPolicyFactory(IOptions<IntegrationDataStoreConfiguration> integrationDataStoreConfiguration)
        {
            EnsureArg.IsNotNull(integrationDataStoreConfiguration?.Value, nameof(integrationDataStoreConfiguration));

            _retryPolicy = CreateExtendedRetryPolicy(integrationDataStoreConfiguration.Value);
        }

        public AsyncRetryPolicy RetryPolicy
        {
            get
            {
                return _retryPolicy;
            }
        }

        private static AsyncRetryPolicy CreateExtendedRetryPolicy(IntegrationDataStoreConfiguration integrationDataStoreConfiguration)
        {
            return Policy.Handle<RequestFailedException>()
                .WaitAndRetryAsync(
                    retryCount: integrationDataStoreConfiguration.MaxRetryCount,
                    sleepDurationProvider: (_) => TimeSpan.FromSeconds(integrationDataStoreConfiguration.RetryInternalInSecondes),
                    onRetryAsync: (e, _, _, ctx) =>
                    {
                        if (integrationDataStoreConfiguration.MaxWaitTimeInSeconds == -1)
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
                            ctx.Add(RetryEndTimeContextKey, DateTime.UtcNow.AddSeconds(integrationDataStoreConfiguration.MaxWaitTimeInSeconds));
                        }

                        return Task.CompletedTask;
                    });
        }
    }
}
