// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.TaskManagement
{
    public interface ITask : IDisposable
    {
        public string RunId { get; set; }

        public Task<TaskResultData> ExecuteAsync();

        public void Cancel();

        public bool IsCancelling();
    }
}
