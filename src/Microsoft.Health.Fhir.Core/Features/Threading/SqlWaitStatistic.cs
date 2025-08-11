// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Threading
{
    /// <summary>
    /// Represents SQL wait statistics for monitoring.
    /// </summary>
    public class SqlWaitStatistic
    {
        /// <summary>
        /// Gets or sets the wait type name.
        /// </summary>
        public string WaitType { get; set; }

        /// <summary>
        /// Gets or sets the number of waiting tasks.
        /// </summary>
        public long WaitingTasksCount { get; set; }

        /// <summary>
        /// Gets or sets the total wait time in milliseconds.
        /// </summary>
        public long WaitTimeMs { get; set; }

        /// <summary>
        /// Gets or sets the maximum wait time in milliseconds.
        /// </summary>
        public long MaxWaitTimeMs { get; set; }

        /// <summary>
        /// Gets or sets the signal wait time in milliseconds.
        /// </summary>
        public long SignalWaitTimeMs { get; set; }
    }
}
