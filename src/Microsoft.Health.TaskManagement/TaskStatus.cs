// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.TaskManagement
{
    /// <summary>
    /// Task status num
    /// </summary>
    public enum TaskStatus
    {
        /// <summary>
        /// Task created but not ready for execution.
        /// </summary>
        Created,

        /// <summary>
        /// Task is ready for execution
        /// </summary>
        Queued,

        /// <summary>
        /// Task is repicked up and running.
        /// </summary>
        Running,

        /// <summary>
        /// Task completed.
        /// </summary>
        Completed,
    }
}
