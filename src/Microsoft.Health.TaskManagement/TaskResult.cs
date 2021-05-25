// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.TaskManagement
{
    /// <summary>
    /// Task result enums
    /// </summary>
    public enum TaskResult
    {
        /// <summary>
        /// Task successfully completed
        /// </summary>
        Success,

        /// <summary>
        /// Task completed with failed result.
        /// </summary>
        Fail,

        /// <summary>
        /// Task completed with canceled result.
        /// </summary>
        Canceled,
    }
}
