﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration
{
    public enum BundleOrchestratorOperationStatus
    {
        Open,
        WaitingForResources,
        Processing,
        Completed,
        Failed,
        Canceled,
    }
}
