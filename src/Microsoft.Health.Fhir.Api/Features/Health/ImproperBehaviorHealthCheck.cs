// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Medino;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Health.Fhir.Core.Features.Health;

namespace Microsoft.Health.Fhir.Api.Features.Health
{
    public class ImproperBehaviorHealthCheck : IHealthCheck, INotificationHandler<ImproperBehaviorNotification>
    {
        private bool _isHealthy = true;
        private string _message = string.Empty;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (_isHealthy)
            {
                return Task.FromResult(HealthCheckResult.Healthy());
            }

            return Task.FromResult(new HealthCheckResult(HealthStatus.Unhealthy, "Improper server behavior has been detected." + _message));
        }

        public Task Handle(ImproperBehaviorNotification notification, CancellationToken cancellationToken)
        {
            _isHealthy = false;
            _message += " " + notification.Message;
            return Task.CompletedTask;
        }
    }
}
