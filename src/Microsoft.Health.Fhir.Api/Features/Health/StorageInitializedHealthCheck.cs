// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Messages.Storage;

namespace Microsoft.Health.Fhir.Api.Features.Health
{
    public class StorageInitializedHealthCheck : IHealthCheck, INotificationHandler<StorageInitializedNotification>
    {
        private const string SuccessfullyInitializedMessage = "Successfully initialized.";
        private bool _storageReady;
        private readonly DateTimeOffset _started = Clock.UtcNow;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (_storageReady)
            {
                return Task.FromResult(HealthCheckResult.Healthy(SuccessfullyInitializedMessage));
            }

            TimeSpan waited = Clock.UtcNow - _started;
            if (waited < TimeSpan.FromMinutes(5))
            {
                return Task.FromResult(new HealthCheckResult(HealthStatus.Degraded, $"Storage is initializing. Waited: {(int)waited.TotalSeconds}s."));
            }

            return Task.FromResult(new HealthCheckResult(HealthStatus.Unhealthy, $"Storage has not been initialized. Waited: {(int)waited.TotalSeconds}s."));
        }

        public Task Handle(StorageInitializedNotification notification, CancellationToken cancellationToken)
        {
            _storageReady = true;
            return Task.CompletedTask;
        }
    }
}
