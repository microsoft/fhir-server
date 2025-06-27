// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Core.Configs;

public class HostingBackgroundServiceQueueItem
{
    /// <summary>
    /// Determines whether job and related queue is enabled or not.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the queue type.
    /// </summary>
    public QueueType Queue { get; set; }

    /// <summary>
    /// Gets or sets the max running task count at the same time for this queue type.
    /// </summary>
    public short? MaxRunningTaskCount { get; set; }
}
