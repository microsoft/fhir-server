// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class DateTimeOffsetExtensions
    {
        private const string DateTimeOffsetFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK";

        /// <summary>
        /// Formats the input DateTimeOffset as an ISO 8601 string. This is different from the standard format string "o"
        /// in that the fractional digits are omitted once zero.
        /// </summary>
        /// <param name="offset">The input date to format</param>
        /// <returns>ISO 8601 formatted string from <paramref name="offset"/></returns>
        public static string ToInstantString(this DateTimeOffset offset)
        {
            return offset.ToString(DateTimeOffsetFormat);
        }
    }
}
