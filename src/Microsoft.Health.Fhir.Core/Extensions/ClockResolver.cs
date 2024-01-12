// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Extensions;

#if NET8_0_OR_GREATER

public static class ClockResolver
{
    public static TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}

#endif
