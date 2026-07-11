// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Messages.Storage;

namespace Microsoft.Health.Fhir.Api.Features.Health
{
    public class StorageInitializedHealthCheck : IHealthCheck, INotificationHandler<SearchParametersInitializedNotification>
    {
        private readonly IDatabaseStatusReporter _databaseStatusReporter;
        private readonly StorageInitializedHealthCheckConfiguration _configuration;
        private bool _storageReady;
        private readonly DateTimeOffset _started = Clock.UtcNow;

        private const string SuccessfullyInitializedMessage = "Successfully initialized.";
        private const string DegradedCMKMessage = "The health of the store has degraded. Customer-managed key is not properly set.";

        public StorageInitializedHealthCheck(
            IDatabaseStatusReporter databaseStatusReporter,
            IOptions<StorageInitializedHealthCheckConfiguration> configuration)
        {
            _databaseStatusReporter = EnsureArg.IsNotNull(databaseStatusReporter, nameof(databaseStatusReporter));
            _configuration = EnsureArg.IsNotNull(configuration, nameof(configuration)).Value;

            if (_configuration.StartupDegradedDelay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(configuration),
                    _configuration.StartupDegradedDelay,
                    $"{nameof(StorageInitializedHealthCheckConfiguration.StartupDegradedDelay)} must be non-negative.");
            }

            if (_configuration.StorageInitializationTimeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(configuration),
                    _configuration.StorageInitializationTimeout,
                    $"{nameof(StorageInitializedHealthCheckConfiguration.StorageInitializationTimeout)} must be non-negative.");
            }

            if (_configuration.StartupDegradedDelay > _configuration.StorageInitializationTimeout)
            {
                throw new ArgumentException(
                    $"{nameof(_configuration.StartupDegradedDelay)} cannot exceed {nameof(_configuration.StorageInitializationTimeout)}.",
                    nameof(configuration));
            }
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            // If the storage is ready, we can return a healthy status immediately.
            if (_storageReady)
            {
                return Task.FromResult(HealthCheckResult.Healthy(SuccessfullyInitializedMessage));
            }

            TimeSpan waited = Clock.UtcNow - _started;
            if (waited < _configuration.StartupDegradedDelay)
            {
                return Task.FromResult(new HealthCheckResult(HealthStatus.Unhealthy, $"Storage is initializing. Waited: {(int)waited.TotalSeconds}s."));
            }

            if (waited < _configuration.StorageInitializationTimeout)
            {
                return Task.FromResult(new HealthCheckResult(HealthStatus.Degraded, $"Storage is initializing. Waited: {(int)waited.TotalSeconds}s."));
            }

            // Check if customer-managed key (CMK) is properly set.
            var isCMKProperlySet = _databaseStatusReporter.IsCustomerManagerKeyProperlySetAsync(cancellationToken).GetAwaiter().GetResult();
            if (!isCMKProperlySet)
            {
                return Task.FromResult(new HealthCheckResult(HealthStatus.Degraded, DegradedCMKMessage));
            }

            return Task.FromResult(new HealthCheckResult(HealthStatus.Unhealthy, $"Storage has not been initialized. Waited: {(int)waited.TotalSeconds}s."));
        }

        public Task Handle(SearchParametersInitializedNotification notification, CancellationToken cancellationToken)
        {
            _storageReady = true;
            return Task.CompletedTask;
        }
    }
}
