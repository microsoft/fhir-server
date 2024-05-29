// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Extensions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// The resource surrogate ID is actually the last modified datetime with a "uniquifier" added to it by the database.
    /// The DateTime is truncated to millisecond precision, turned into its 100ns ticks representation, and then left bit-shifted by 3.
    /// </summary>
    internal static class ResourceSurrogateIdHelper
    {
        public static DateTimeOffset MaxDateTime => IdHelper.MaxDateTime;

        public static long ToSurrogateId(this DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.ToId();
        }

        public static DateTimeOffset ToLastUpdated(this long resourceSurrogateId)
        {
            return resourceSurrogateId.ToDate();
        }

        public static DateTimeOffset TruncateToMillisecond(this DateTimeOffset dateTimeOffset)
        {
            return new DateTimeOffset(dateTimeOffset.DateTime.TruncateToMillisecond(), dateTimeOffset.Offset);
        }
    }
}
