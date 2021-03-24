// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.TaskManagement;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public interface IFhirTaskDataStore
    {
        Task<IReadOnlyCollection<TaskInfo>> GetNextMessagesAsync(int count, int taskHeartbeatTimeoutThresholdInSeconds);
    }
}
