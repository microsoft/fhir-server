// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    public enum SearchParameterStatus
    {
        Disabled = 1,
        Supported = 2,
        Enabled = 3,
        Deleted = 4,
        PendingDelete = 5,
        PendingDisable = 6,
        Unsupported = 7,
    }
}
