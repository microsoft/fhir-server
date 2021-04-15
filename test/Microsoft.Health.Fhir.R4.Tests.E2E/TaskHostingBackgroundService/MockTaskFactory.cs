// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.TaskManagement;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.TaskHostingBackgroundService
{
    public class MockTaskFactory : ITaskFactory
    {
        public ITask Create(TaskInfo taskInfo)
        {
            if (taskInfo.TaskTypeId == 101)
            {
                return new MockFailueTask
                {
                    RunId = taskInfo.RunId,
                };
            }
            else
            {
                return new MockSuccessTask
                {
                    RunId = taskInfo.RunId,
                };
            }
        }
    }
}
