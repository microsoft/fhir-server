// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.CosmosDb.Configs;
using Polly;
using Polly.Retry;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    public class RetryExceptionPolicyFactory
    {
        private readonly int _maxNumberOfRetries;
        private readonly TimeSpan _minWaitTime;
        private readonly TimeSpan _maxWaitTime;

        public RetryExceptionPolicyFactory(CosmosDataStoreConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            _maxNumberOfRetries = configuration.RetryOptions.MaxNumberOfRetries;
            _minWaitTime = TimeSpan.FromSeconds(configuration.RetryOptions.MinWaitTimeInSeconds);
            _maxWaitTime = TimeSpan.FromSeconds(configuration.RetryOptions.MaxWaitTimeInSeconds);
        }

        public RetryPolicy CreateRetryPolicy()
        {
            var policy = Policy
                .Handle<Exception>(RetryExceptionPolicy.IsTransient)
                .WaitAndRetryAsync(
                    _maxNumberOfRetries,
                    retryAttempt =>
                        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

            return policy;
        }
    }
}
