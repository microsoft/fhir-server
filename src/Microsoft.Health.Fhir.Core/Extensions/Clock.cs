// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Extensions;

#if NET8_0_OR_GREATER

/// <summary>
/// Clock has been removed in Shared Components in favor of .NET8 TimeProvider.
/// This class provides a wrapper to the TimeProvider to maintain the same interface while we continue to support .net6
/// </summary>
public static class Clock
{
    public static DateTimeOffset UtcNow
    {
        get
        {
            return ClockResolver.TimeProvider.GetUtcNow();
        }
    }
}

#endif
