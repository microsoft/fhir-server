// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Features.Operations.JobMonitor.Messages
{
    public class JobMonitorMetricsNotification : INotification
    {
        public JobMonitorMetricsNotification(
            IReadOnlyDictionary<QueueType, long> queueAges,
            IReadOnlyDictionary<QueueType, QueueDepth> queueDepths)
        {
            QueueAges = EnsureArg.IsNotNull(queueAges, nameof(queueAges));
            QueueDepths = EnsureArg.IsNotNull(queueDepths, nameof(queueDepths));
        }

        public IReadOnlyDictionary<QueueType, long> QueueAges { get; }

        public IReadOnlyDictionary<QueueType, QueueDepth> QueueDepths { get; }
    }
}
