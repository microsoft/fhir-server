// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class TaskWrapper
    {
        public TaskInfo TaskInfo { get; set; }

        public System.Threading.Tasks.Task RunningTask { get; set; }
    }
}
