// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.JobManagement
{
    /// <summary>
    /// Job result enums
    /// </summary>
    public enum JobResult
    {
        /// <summary>
        /// Job successfully completed
        /// </summary>
        Success,

        /// <summary>
        /// Job completed with failed result.
        /// </summary>
        Fail,

        /// <summary>
        /// Job completed with canceled result.
        /// </summary>
        Canceled,
    }
}
