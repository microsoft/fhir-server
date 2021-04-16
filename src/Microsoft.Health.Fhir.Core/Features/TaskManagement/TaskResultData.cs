// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Microsoft.Health.Fhir.Core.Features.TaskManagement
{
    public class TaskResultData
    {
        // add this for json deserialize
        public TaskResultData()
        {
        }

        public TaskResultData(TaskResult result, string resultData)
        {
            Result = result;
            ResultData = resultData;
        }

        public TaskResult Result { get; set; }

        public string ResultData { get; set; }

        public static TaskResultData ResloveTaskResultFromDbString(string result)
        {
            if (string.IsNullOrEmpty(result))
            {
                return null;
            }

            return JsonSerializer.Deserialize<TaskResultData>(result);
        }
    }
}
