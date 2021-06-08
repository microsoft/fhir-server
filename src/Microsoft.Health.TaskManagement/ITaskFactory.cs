// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.TaskManagement
{
    /// <summary>
    /// Interface for factory to create task from task information
    /// </summary>
    public interface ITaskFactory
    {
        /// <summary>
        /// Create new task from TaskInfo
        /// </summary>
        /// <param name="taskInfo">Task information payload.</param>
        /// <returns>Task for execution.</returns>
        ITask Create(TaskInfo taskInfo);
    }
}
