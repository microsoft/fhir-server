// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Microsoft.Health.TaskManagement
{
    /// <summary>
    /// Interface for execution task.
    /// </summary>
    public interface ITask : IDisposable
    {
        /// <summary>
        /// RunId for this task execution. Retry would generate different runid.
        /// </summary>
        public string RunId { get; set; }

        /// <summary>
        /// Execute the task.
        /// </summary>
        /// <returns>Task result data for the execution.</returns>
        public Task<TaskResultData> ExecuteAsync();

        /// <summary>
        /// Cancel the task execution.
        /// </summary>
        public void Cancel();

        /// <summary>
        /// Check if the task is in cancelling status.
        /// </summary>
        /// <returns>return true if cancel operation triggered.</returns>
        public bool IsCancelling();
    }
}
