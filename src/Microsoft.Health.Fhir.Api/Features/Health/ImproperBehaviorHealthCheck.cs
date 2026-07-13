// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Health.Fhir.Core.Features.Health;

namespace Microsoft.Health.Fhir.Api.Features.Health
{
    public class ImproperBehaviorHealthCheck : IHealthCheck, INotificationHandler<ImproperBehaviorNotification>
    {
        private readonly object _lock = new();
        private volatile ImproperBehaviorHealthCheckState _state = ImproperBehaviorHealthCheckState.Healthy;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            ImproperBehaviorHealthCheckState state = _state;
            if (state.IsHealthy)
            {
                return Task.FromResult(HealthCheckResult.Healthy());
            }

            return Task.FromResult(new HealthCheckResult(HealthStatus.Unhealthy, "Improper server behavior has been detected." + state.Message));
        }

        public Task Handle(ImproperBehaviorNotification notification, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _state = new ImproperBehaviorHealthCheckState(false, _state.Message + " " + notification.Message);
            }

            return Task.CompletedTask;
        }
    }
}
