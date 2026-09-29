// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    /// <summary>
    /// Reports the request concurrency observed by the throttling middleware on a single server instance.
    /// </summary>
    public interface IThrottlingMetricHandler
    {
        /// <summary>
        /// Reports a sample of the throttling middleware's concurrency state.
        /// </summary>
        /// <param name="requestsInFlight">Requests executing when the sample was taken.</param>
        /// <param name="peakRequestsInFlight">Highest number of requests executing at once since the previous sample.</param>
        /// <param name="queuedRequests">Requests waiting in the throttling queue when the sample was taken.</param>
        /// <param name="concurrentRequestLimit">The configured concurrent request limit.</param>
        void ReportConcurrency(long requestsInFlight, long peakRequestsInFlight, long queuedRequests, long concurrentRequestLimit);
    }
}
