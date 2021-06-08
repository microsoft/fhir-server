// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;

namespace Microsoft.Health.TaskManagement
{
    /// <summary>
    /// Interface to update task context.
    /// </summary>
    public interface IContextUpdater
    {
        /// <summary>
        /// Update context for the task.
        /// </summary>
        /// <param name="taskId">Id for the task</param>
        /// <param name="context">Task context in string format</param>
        /// <returns>Task infomation after context updated.</returns>
        public Task<TaskInfo> UpdateContextAsync(string taskId, string context);
    }
}
