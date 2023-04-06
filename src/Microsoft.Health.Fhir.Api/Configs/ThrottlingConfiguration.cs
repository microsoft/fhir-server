// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Api.Features.Throttling;

namespace Microsoft.Health.Fhir.Api.Configs
{
    /// <summary>
    /// Configuration values for optional request throttling.
    /// </summary>
    public class ThrottlingConfiguration
    {
        /// <summary>
        /// Whether to enable request throttling.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// The maximum number of requests that are allowed to execute concurrently.
        /// If queueing is disabled, the server responds with error code 429.
        /// If queueing is enabled, the request is queued for up to be executed once there is capacity.
        /// </summary>
        public int ConcurrentRequestLimit { get; set; }

        /// <summary>
        /// Endpoints that are excluded from throttling
        /// </summary>
        public HashSet<ExcludedEndpoint> ExcludedEndpoints { get; } = new HashSet<ExcludedEndpoint>();

        /// <summary>
        /// The maximum number of requests that can be queued up at a time.
        /// If 0, queueing is disabled.
        /// </summary>
        public int MaxQueueSize { get; set; }

        /// <summary>
        /// The total number of milliseconds a request can sit in the queue. If this time
        /// elapses before the request is picked up, the server responds with a 429.
        /// </summary>
        public int MaxMillisecondsInQueue { get; set; }

        /// <summary>
        /// a known datastore
        /// </summary>
        public string DataStore { get; set; } = string.Empty;
    }
}
