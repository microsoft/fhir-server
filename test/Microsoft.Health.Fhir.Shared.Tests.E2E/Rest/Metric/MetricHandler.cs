// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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

        public Dictionary<Type, int> HandleCountDictionary { get; } = new Dictionary<Type, int>();

        public void ResetCount()
        {
            HandleCountDictionary.Clear();
        }

        public Task Handle(IMetricsNotification notification, CancellationToken cancellationToken)
        {
            Type notificationType = notification.GetType();
            if (HandleCountDictionary.ContainsKey(notificationType))
            {
                HandleCountDictionary[notificationType]++;
            }
            else
            {
                HandleCountDictionary.Add(notificationType, 1);
            }

            return Task.CompletedTask;
        }
    }
}
