// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Export
{
#pragma warning disable CA1717 // Only FlagsAttribute enums should have plural names
    public enum ExportJobStatus
#pragma warning restore CA1717 // Only FlagsAttribute enums should have plural names
    {
        Cancelled,
        Completed,
        Failed,
        Queued,
        Running,
    }
}
