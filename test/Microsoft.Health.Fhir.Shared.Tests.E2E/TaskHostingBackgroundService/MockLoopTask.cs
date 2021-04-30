// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.TaskManagement;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.TaskHostingBackgroundService
{
    public class MockLoopTask : ITask
    {
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public string RunId { get; set; }

        public async Task<TaskResultData> ExecuteAsync()
        {
            CancellationToken cancellationToken = _cancellationTokenSource.Token;

            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    var taskResultData = new TaskResultData(TaskResult.Canceled, string.Empty);
                    return taskResultData;
                }

                await Task.Delay(1000);
            }
        }

        public void Cancel()
        {
            _cancellationTokenSource?.Cancel();
        }

        public bool IsCancelling()
        {
            return _cancellationTokenSource?.IsCancellationRequested ?? true;
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
