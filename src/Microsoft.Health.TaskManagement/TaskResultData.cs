// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.TaskManagement
{
    public class TaskResultData
    {
        public TaskResultData(TaskResult result, string resultData)
        {
            EnsureArg.IsNotNull(resultData, nameof(resultData));

            Result = result;
            ResultData = resultData;
        }

        public TaskResult Result { get; set; }

        public string ResultData { get; set; }
    }
}
