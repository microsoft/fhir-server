// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Extensions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// The resource surrogate ID is actually the last modified datetime with a "uniquifier" added to it by the database.
    /// The DateTime is truncated to millisecond precision, turned into its 100ns ticks representation, and then left bit-shifted by 3.
    /// </summary>
    internal static class ResourceSurrogateIdHelper
    {
        private const int ShiftFactor = 3;

        internal static readonly DateTime MaxDateTime = new DateTime(long.MaxValue >> ShiftFactor, DateTimeKind.Utc).TruncateToMillisecond().AddTicks(-1);

        public static long LastUpdatedToResourceSurrogateId(DateTime dateTime)
        {
            EnsureArg.IsLte(dateTime, MaxDateTime, nameof(dateTime));
            long id = dateTime.TruncateToMillisecond().Ticks << ShiftFactor;

            Debug.Assert(id >= 0, "The ID should not have become negative");
            return id;
        }

        public static DateTime ResourceSurrogateIdToLastUpdated(long resourceSurrogateId)
        {
            var dateTime = new DateTime(resourceSurrogateId >> ShiftFactor, DateTimeKind.Utc);
            return dateTime.TruncateToMillisecond();
        }
    }
}
