// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations;

public static class QueueClientExtensions
{
    public static Task<IReadOnlyList<JobInfo>> EnqueueAsync<T>(this IQueueClient queueClient, QueueType queueType, long? groupId = null, bool forceOneActiveJobGroup = false, bool isCompleted = false, CancellationToken cancellationToken = default, params T[] definitions)
    {
        return queueClient.EnqueueAsync((byte)queueType, definitions.Select(x => JsonConvert.SerializeObject(x)).ToArray(), groupId, forceOneActiveJobGroup, isCompleted, cancellationToken);
    }
}
