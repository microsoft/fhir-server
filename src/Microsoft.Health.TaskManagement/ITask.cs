// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.TaskManagement
{
    /// <summary>
    /// Interface for execution task.
    /// </summary>
    public interface ITask
    {
        /// <summary>
        /// Execute the task.
        /// </summary>
        /// <param name="progress">Report task progress.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task result in string. </returns>
        public Task<string> ExecuteAsync(IProgress<string> progress, CancellationToken cancellationToken);
    }
}
