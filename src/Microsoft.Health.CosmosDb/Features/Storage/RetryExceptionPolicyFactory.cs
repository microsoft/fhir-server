// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Health.CosmosDb.Configs;
using Polly;
using Polly.Retry;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    public class RetryExceptionPolicyFactory
    {
        private readonly int _maxNumberOfRetries;
        private const string RequestChargeKey = "RequestCharge";

        public RetryExceptionPolicyFactory(CosmosDataStoreConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            _maxNumberOfRetries = configuration.RetryOptions.MaxNumberOfRetries;
        }

        public AsyncRetryPolicy CreateRetryPolicy()
        {
            return CreateTrackedRetryPolicy(null);
        }

        public AsyncRetryPolicy CreateTrackedRetryPolicy(Context context)
        {
            if (context != null)
            {
                context.Add(RequestChargeKey, 0D);
            }

            var policy = Policy
                .Handle<DocumentClientException>(RetryExceptionPolicy.IsTransient)
                .WaitAndRetryAsync(
                    _maxNumberOfRetries,
                    (retryAttempt, ctx) =>
                        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    (ex, ts, ctx) =>
                    {
                        if (ctx == null)
                        {
                            return;
                        }

                        double existing = (double)ctx[RequestChargeKey];
                        ctx[RequestChargeKey] = existing + ((DocumentClientException)ex).RequestCharge;
                    });

            return policy;
        }

        public static double? GetRequestChargeForTrackedRetryPolicy(Context context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            object output;
            return context.TryGetValue(RequestChargeKey, out output) ? (double)output : (double?)null;
        }
    }
}
