// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Logging.Metrics.Handlers
{
    public interface IJobMonitorMetricHandler
    {
        void RegisterQueueAge(long age);

        void RegisterQueueDepth(long depth);
    }
}
