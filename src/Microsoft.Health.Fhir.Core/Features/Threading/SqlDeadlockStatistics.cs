// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Threading
{
    /// <summary>
    /// Represents deadlock statistics for monitoring.
    /// </summary>
    public class SqlDeadlockStatistics
    {
        /// <summary>
        /// Gets or sets the number of deadlocks per second.
        /// </summary>
        public double DeadlocksPerSecond { get; set; }

        /// <summary>
        /// Gets or sets the total deadlock count since server restart.
        /// </summary>
        public long TotalDeadlockCount { get; set; }

        /// <summary>
        /// Gets or sets the timestamp of the last deadlock.
        /// </summary>
        public DateTime? LastDeadlockTime { get; set; }
    }
}
