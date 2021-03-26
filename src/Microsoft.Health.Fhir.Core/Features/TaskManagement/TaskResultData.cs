// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.TaskManagement
{
    public class TaskResultData
    {
        public TaskResultData(TaskResult result, string resultData)
        {
            Result = result;
            ResultData = resultData;
        }

        public TaskResult Result { get; set; }

        public string ResultData { get; set; }
    }
}
