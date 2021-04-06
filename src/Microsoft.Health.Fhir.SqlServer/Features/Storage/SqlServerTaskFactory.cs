// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.TaskManagement;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlServerTaskFactory : ITaskFactory
    {
        public ITask Create(TaskInfo taskInfo)
        {
            return new SqlServerTask
            {
                RunId = taskInfo.RunId,
            };
        }
    }
}
