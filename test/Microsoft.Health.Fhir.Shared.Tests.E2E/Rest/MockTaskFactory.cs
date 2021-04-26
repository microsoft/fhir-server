// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text.Json;
using Microsoft.Health.Fhir.Api.Features.Operations.BulkImport.Models;
using Microsoft.Health.Fhir.Core.Features.TaskManagement;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public class MockTaskFactory : ITaskFactory
    {
        public ITask Create(TaskInfo taskInfo)
        {
            var request = JsonSerializer.Deserialize<BulkImportRequest>(taskInfo.InputData);
            if (string.Equals(request.InputSource.ToString(), "http://failueTask/", StringComparison.OrdinalIgnoreCase))
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
