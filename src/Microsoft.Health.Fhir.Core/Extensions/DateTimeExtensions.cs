// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class DateTimeExtensions
    {
        /// <summary>
        /// Truncates a DateTime to millisecond precision
        /// </summary>
        /// <param name="dateTime">The DateTime</param>
        /// <returns>The truncated dateTime</returns>
        public static DateTime TruncateToMillisecond(this DateTime dateTime)
        {
            return new DateTime(dateTime.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, dateTime.Kind);
        }
    }
}
