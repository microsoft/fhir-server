// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public interface IJobMonitorMetricHandler
    {
        void ReportJobQueueAge(string queueName, long value);

        void ReportJobQueuePending(string queueName, long value);

        void ReportJobQueueRunning(string queueName, long value);
    }
}
