// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Practices.EnterpriseLibrary.TransientFaultHandling;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
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
            _minWaitTime = TimeSpan.FromSeconds(Math.Min(RetryStrategy.DefaultMinBackoff.TotalSeconds, configuration.RetryOptions.MaxWaitTimeInSeconds));
            _maxWaitTime = TimeSpan.FromSeconds(configuration.RetryOptions.MaxWaitTimeInSeconds);
        }

        public RetryPolicy CreateRetryPolicy()
        {
            var strategy = new ExponentialBackoff(
                _maxNumberOfRetries,
                _minWaitTime,
                _maxWaitTime,
                RetryStrategy.DefaultClientBackoff);

            return new RetryPolicy<RetryExceptionPolicy>(strategy);
        }
    }
}
