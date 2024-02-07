// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Core.Configs;

public class HostingBackgroundServiceQueueItem
{
    public QueueType Queue { get; set; }

    // TODO: This is not honored. Make sure that it is not used in PaaS and remove.
    public bool UpdateProgressOnHeartbeat { get; set; }
}
