// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.TaskManagement.UnitTests
{
    public class TestTaskFactory : ITaskFactory
    {
        private Func<TaskInfo, ITask> _factoryFunc;

        public TestTaskFactory(Func<TaskInfo, ITask> factoryFunc)
        {
            _factoryFunc = factoryFunc;
        }

        public ITask Create(TaskInfo taskInfo)
        {
            return _factoryFunc(taskInfo);
        }
    }
}
