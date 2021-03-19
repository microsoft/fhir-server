// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.TaskManagement;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.TaskManagement
{
    public class TestTask : ITask
    {
        private Func<Task<TaskResultData>> _executeFunc;
        private Action _cancelAction;

        public TestTask(Func<Task<TaskResultData>> executeFunc, Action cancelAction)
        {
            _executeFunc = executeFunc;
            _cancelAction = cancelAction;
        }

        public Task<TaskResultData> ExecuteAsync()
        {
            return _executeFunc();
        }

        public void Cancel()
        {
            _cancelAction();
        }

        public void Dispose()
        {
        }
    }
}
