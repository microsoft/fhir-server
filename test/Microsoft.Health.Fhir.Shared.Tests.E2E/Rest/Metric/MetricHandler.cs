// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Metric
{
    public class MetricHandler : INotificationHandler<INotification>
    {
        public MetricHandler()
        {
        }

        public Dictionary<Type, List<INotification>> NotificationMapping { get; } = new Dictionary<Type, List<INotification>>();

        public void ResetCount()
        {
            NotificationMapping.Clear();
        }

        public Task Handle(INotification notification, CancellationToken cancellationToken)
        {
            Type notificationType = notification.GetType();
            if (NotificationMapping.TryGetValue(notificationType, out List<INotification> foundNotifications))
            {
                foundNotifications.Add(notification);
            }
            else
            {
                NotificationMapping.Add(notificationType, new List<INotification> { notification });
            }

            return Task.CompletedTask;
        }
    }
}
