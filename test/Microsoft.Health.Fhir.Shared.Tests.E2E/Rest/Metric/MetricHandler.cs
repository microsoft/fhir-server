// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Metrics;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.Rest.Metric
{
    public class MetricHandler : INotificationHandler<IMetricsNotification>
    {
        public MetricHandler()
        {
        }

        public int HandleCount { get; private set; }

        public void ResetCount()
        {
            HandleCount = 0;
        }

        public Task Handle(IMetricsNotification notification, CancellationToken cancellationToken)
        {
            HandleCount++;
            return Task.CompletedTask;
        }
    }
}
