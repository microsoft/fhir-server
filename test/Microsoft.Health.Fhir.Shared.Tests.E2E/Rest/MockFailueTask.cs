// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.TaskManagement;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public class MockFailueTask : ITask
    {
        public string RunId { get; set; }

        public Task<TaskResultData> ExecuteAsync()
        {
            throw new Exception("Task failed");
        }

        public void Cancel()
        {
        }

        public bool IsCancelling()
        {
            return false;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposedStatus)
        {
        }
    }
}
