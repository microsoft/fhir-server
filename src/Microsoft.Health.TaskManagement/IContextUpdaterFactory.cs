// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.TaskManagement
{
    /// <summary>
    /// Factory for task context updater
    /// </summary>
    public interface IContextUpdaterFactory
    {
        /// <summary>
        /// Create task context updater from taskId and runId
        /// </summary>
        /// <param name="taskId">Task id</param>
        /// <param name="runId">Current task run id</param>
        public IContextUpdater CreateContextUpdater(string taskId, string runId);
    }
}
