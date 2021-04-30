// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
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
        /// <param name="context">Task context in string format</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        public Task UpdateContextAsync(string context, CancellationToken cancellationToken);
    }
}
