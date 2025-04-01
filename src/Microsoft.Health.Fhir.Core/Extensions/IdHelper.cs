// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using EnsureThat;
using Microsoft.Health.Core.Extensions;

namespace Microsoft.Health.Fhir.Core.Extensions;

/// <summary>
/// The DateTime is truncated to millisecond precision, turned into its 100ns ticks representation, and then left bit-shifted by 3.
/// </summary>
/// <remarks>
/// In the SQL provider, the resource surrogate ID is actually the last modified datetime with a "uniquifier" added to it by the database.
/// </remarks>
internal static class IdHelper
{
    private const int ShiftFactor = 3;

    internal static readonly DateTimeOffset MaxDateTime = new DateTime(long.MaxValue >> ShiftFactor, DateTimeKind.Utc).TruncateToMillisecond().AddTicks(-1);

    public static long ToId(this DateTimeOffset dateTimeOffset)
    {
        EnsureArg.IsLte(dateTimeOffset, MaxDateTime, nameof(dateTimeOffset));
        long id = dateTimeOffset.UtcDateTime.TruncateToMillisecond().Ticks << ShiftFactor;

        Debug.Assert(id >= 0, "The ID should not have become negative");
        return id;
    }

    public static DateTimeOffset ToDate(this long resourceSurrogateId)
    {
        var dateTime = new DateTime(resourceSurrogateId >> ShiftFactor, DateTimeKind.Utc);
        var offset = new DateTimeOffset(dateTime.TruncateToMillisecond(), TimeSpan.Zero);
        return offset;
    }
}
