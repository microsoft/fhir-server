// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.JobManagement
{
    /// <summary>
    /// Job status enum
    /// </summary>
#pragma warning disable CA1028 // Enum Storage should be Int32
    public enum JobStatus : byte
#pragma warning restore CA1028 // Enum Storage should be Int32
    {
        /// <summary>
        /// Job created but not ready for execution.
        /// </summary>
        Created,

        /// <summary>
        /// Job is repicked up and running.
        /// </summary>
        Running,

        /// <summary>
        /// Job completed.
        /// </summary>
        Completed,

        /// <summary>
        /// Job failed.
        /// </summary>
        Failed,

        /// <summary>
        /// Job cancelled.
        /// </summary>
        Cancelled,

        /// <summary>
        /// Job Archived.
        /// </summary>
        Archived,
    }
}
