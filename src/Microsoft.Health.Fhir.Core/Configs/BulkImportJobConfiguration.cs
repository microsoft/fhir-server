// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class BulkImportJobConfiguration
    {
        /// <summary>
        /// Determines whether export is enabled or not.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Determines the number of consecutive failures (per data processing task) allowed before the job is marked as failed.
        /// Setting this number to 0 means job will not be retired and setting this number of -1 means there is no threshold.
        /// </summary>
        public int ConsecutiveFailuresThreshold { get; set; } = 3;

        /// <summary>
        /// Determines the number of seconds allowed before the worker declares job to be stale and can be picked up again.
        /// </summary>
        public ushort JobHeartbeatTimeoutThresholdInSeconds { get; set; } = 600;

        /// <summary>
        /// Determines the frequency of polling new jobs in milliseconds
        /// </summary>
        public ushort JobPollingFrequencyInMilliseconds { get; set; } = 30;

        /// <summary>
        /// Controls how many resources will be returned for each search query while exporting the data.
        /// </summary>
        public uint MaximumConcurrency { get; set; } = 5;
    }
}
