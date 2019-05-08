// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class ExportJobConfiguration
    {
        /// <summary>
        /// Determines whether export is enabled or not.
        /// </summary>
        public bool Enabled { get; set; }

        public ushort MaximumNumberOfConcurrentJobsAllowed { get; set; } = 1;

        public TimeSpan JobHeartbeatTimeoutThreshold { get; set; } = TimeSpan.FromMinutes(10);

        public TimeSpan JobPollingFrequency { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// List of destinations that are supported for export operation.
        /// </summary>
        public HashSet<string> SupportedDestinations { get; } = new HashSet<string>();
    }
}
