// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
#pragma warning disable CA1008
    public enum SearchParameterStatus
    {
        Disabled = 1,
        Supported = 2,
        Enabled = 3,
        Deleted = 4,
    }
#pragma warning restore CA1008
}
