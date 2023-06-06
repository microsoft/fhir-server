// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Extensions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// The resource surrogate ID is actually the last modified datetime with a "uniquifier" added to it by the database.
    /// The DateTime is truncated to millisecond precision, turned into its 100ns ticks representation, and then left bit-shifted by 3.
    /// </summary>
    internal static class ResourceSurrogateIdHelper
    {
        public static DateTime MaxDateTime => IdHelper.MaxDateTime;

        public static long LastUpdatedToResourceSurrogateId(DateTime dateTime)
        {
            return dateTime.DateToId();
        }

        public static DateTime ResourceSurrogateIdToLastUpdated(long resourceSurrogateId)
        {
            return resourceSurrogateId.IdToDate();
        }
    }
}
