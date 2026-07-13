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
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Messages.Search;

namespace Microsoft.Health.Fhir.Api.Features.Health
{
    /// <summary>
    /// Pure startup gate. Returns Healthy once storage initialization completes or the configured
    /// timeout backstop elapses (hand off to readiness); otherwise Unhealthy. It makes no CMK /
    /// Key Vault call — CMK routability is handled by the readiness data-store check.
    /// </summary>
    public class StorageInitializedHealthCheck : IHealthCheck, INotificationHandler<SearchParametersInitializedNotification>
    {
        private readonly StorageInitializedHealthCheckConfiguration _configuration;
        private readonly DateTimeOffset _started = Clock.UtcNow;
        private volatile bool _storageReady;

        private const string SuccessfullyInitializedMessage = "Successfully initialized.";

        public StorageInitializedHealthCheck(IOptions<StorageInitializedHealthCheckConfiguration> configuration)
        {
            _configuration = EnsureArg.IsNotNull(configuration, nameof(configuration)).Value;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (_storageReady)
            {
                return Task.FromResult(HealthCheckResult.Healthy(SuccessfullyInitializedMessage));
            }

            TimeSpan waited = Clock.UtcNow - _started;
            if (waited >= _configuration.StorageInitializationTimeout)
            {
                return Task.FromResult(HealthCheckResult.Healthy(
                    $"Startup timeout elapsed after {(int)waited.TotalSeconds}s; handing off to readiness."));
            }

            return Task.FromResult(new HealthCheckResult(
                HealthStatus.Unhealthy,
                $"Storage is initializing. Waited: {(int)waited.TotalSeconds}s."));
        }

        public Task Handle(SearchParametersInitializedNotification notification, CancellationToken cancellationToken)
        {
            _storageReady = true;
            return Task.CompletedTask;
        }
    }
}
