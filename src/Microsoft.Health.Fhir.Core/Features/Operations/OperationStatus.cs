// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    public enum OperationStatus
    {
        Unknown = -1,
        Queued = 0,
        Running = 1,
        Completed = 2,
        Failed = 3,
        Canceled = 4,
        Paused = 5,
    }
}
