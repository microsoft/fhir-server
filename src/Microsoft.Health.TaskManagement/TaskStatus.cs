// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.TaskManagement
{
    // 0:created  1=running, 2=completed, 3=failed, 4=cancelled, 5=archived

    /// <summary>
    /// Task status num
    /// </summary>
#pragma warning disable CA1028 // Enum Storage should be Int32
    public enum TaskStatus : byte
#pragma warning restore CA1028 // Enum Storage should be Int32
    {
        /// <summary>
        /// Task created but not ready for execution.
        /// </summary>
        Created,

        /// <summary>
        /// Task is repicked up and running.
        /// </summary>
        Running,

        /// <summary>
        /// Task completed.
        /// </summary>
        Completed,

        /// <summary>
        /// Task failed.
        /// </summary>
        Failed,

        /// <summary>
        /// Task cancelled.
        /// </summary>
        Cancelled,

        /// <summary>
        /// Task Archived.
        /// </summary>
        Archived,
    }
}
