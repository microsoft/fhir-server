// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.TaskManagement
{
    public interface ITaskConsumer
    {
        Task<TaskInfo> CompleteAsync(TaskInfo task);

        Task<IReadOnlyCollection<TaskInfo>> GetNextMessagesAsync(int count);

        Task<TaskInfo> UpdateContextAsync(TaskInfo task);
    }
}
