// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.CosmosDb.Configs;
using Polly;
using Polly.Retry;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    public class RetryExceptionPolicyFactory
    {
        private readonly int _maxNumberOfRetries;

        public RetryExceptionPolicyFactory(CosmosDataStoreConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            _maxNumberOfRetries = configuration.RetryOptions.MaxNumberOfRetries;
        }

        public AsyncRetryPolicy CreateRetryPolicy()
        {
            var policy = Policy
                .Handle<CosmosException>(RetryExceptionPolicy.IsTransient)
                .WaitAndRetryAsync(
                    _maxNumberOfRetries,
                    retryAttempt =>
                        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

            return policy;
        }
    }
}
