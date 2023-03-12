﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using EnsureThat;
using Microsoft.Health.Core.Extensions;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Queues;

internal static class IdHelper
{
    private const int ShiftFactor = 3;

    internal static readonly DateTime MaxDateTime = new DateTime(long.MaxValue >> ShiftFactor, DateTimeKind.Utc).TruncateToMillisecond().AddTicks(-1);

    public static long DateToId(DateTime dateTime)
    {
        EnsureArg.IsLte(dateTime, MaxDateTime, nameof(dateTime));
        long id = dateTime.TruncateToMillisecond().Ticks << ShiftFactor;

        Debug.Assert(id >= 0, "The ID should not have become negative");
        return id;
    }

    public static DateTime IdToDate(long resourceSurrogateId)
    {
        var dateTime = new DateTime(resourceSurrogateId >> ShiftFactor, DateTimeKind.Utc);
        return dateTime.TruncateToMillisecond();
    }
}
