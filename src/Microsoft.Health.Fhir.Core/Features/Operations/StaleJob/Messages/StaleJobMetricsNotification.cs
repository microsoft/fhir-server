// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Features.Operations.StaleJob.Messages
{
    public class StaleJobMetricsNotification : INotification
    {
        public StaleJobMetricsNotification(IReadOnlyDictionary<QueueType, double> queueAges)
        {
            QueueAges = EnsureArg.IsNotNull(queueAges, nameof(queueAges));
        }

        public IReadOnlyDictionary<QueueType, double> QueueAges { get; }
    }
}
