// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    /// <summary>
    /// Extension methods for <see cref="DateTime"/> and <see cref="DateTimeOffset"/> that clamp
    /// results to <see cref="DateTime.MinValue"/>/<see cref="DateTime.MaxValue"/> instead of
    /// throwing on overflow.
    /// </summary>
    public static class DateTimeSafeExtensions
    {
        private const long MaxDaysBeforeTicksOverflow = long.MaxValue / TimeSpan.TicksPerDay;

        /// <summary>
        /// Adds the specified number of ticks to a <see cref="DateTime"/>, clamping the result
        /// to <see cref="DateTime.MinValue"/> or <see cref="DateTime.MaxValue"/> on overflow.
        /// </summary>
        public static DateTime SafeAddTicks(this DateTime value, long ticks)
        {
            if (ticks == long.MinValue)
            {
                // Can't negate long.MinValue; adding it always underflows within DateTime's range.
                return new DateTime(DateTime.MinValue.Ticks, value.Kind);
            }

            if (ticks > 0 && value.Ticks > DateTime.MaxValue.Ticks - ticks)
            {
                return new DateTime(DateTime.MaxValue.Ticks, value.Kind);
            }

            if (ticks < 0 && value.Ticks < DateTime.MinValue.Ticks - ticks)
            {
                return new DateTime(DateTime.MinValue.Ticks, value.Kind);
            }

            return value.AddTicks(ticks);
        }

        /// <summary>
        /// Adds the specified number of ticks to a <see cref="DateTimeOffset"/>, clamping the result
        /// to <see cref="DateTimeOffset.MinValue"/> or <see cref="DateTimeOffset.MaxValue"/> on overflow.
        /// </summary>
        public static DateTimeOffset SafeAddTicks(this DateTimeOffset value, long ticks)
        {
            if (ticks == long.MinValue)
            {
                // Can't negate long.MinValue; adding it always underflows within DateTimeOffset's range.
                return DateTimeOffset.MinValue;
            }

            if (ticks > 0 && value.Ticks > DateTimeOffset.MaxValue.Ticks - ticks)
            {
                return DateTimeOffset.MaxValue;
            }

            if (ticks < 0 && value.Ticks < DateTimeOffset.MinValue.Ticks - ticks)
            {
                return DateTimeOffset.MinValue;
            }

            return value.AddTicks(ticks);
        }

        /// <summary>
        /// Adds the specified number of days to a <see cref="DateTime"/>, clamping the result
        /// to <see cref="DateTime.MinValue"/> or <see cref="DateTime.MaxValue"/> on overflow.
        /// </summary>
        public static DateTime SafeAddDays(this DateTime value, int days)
        {
            // Detect if days * TimeSpan.TicksPerDay would overflow long.
            if (days > MaxDaysBeforeTicksOverflow || days < -MaxDaysBeforeTicksOverflow)
            {
                return days > 0 ? new DateTime(DateTime.MaxValue.Ticks, value.Kind) : new DateTime(DateTime.MinValue.Ticks, value.Kind);
            }

            long ticks = days * TimeSpan.TicksPerDay;
            return value.SafeAddTicks(ticks);
        }

        /// <summary>
        /// Adds the specified number of days to a <see cref="DateTimeOffset"/>, clamping the result
        /// to <see cref="DateTimeOffset.MinValue"/> or <see cref="DateTimeOffset.MaxValue"/> on overflow.
        /// </summary>
        public static DateTimeOffset SafeAddDays(this DateTimeOffset value, int days)
        {
            // Detect if days * TimeSpan.TicksPerDay would overflow long.
            if (days > MaxDaysBeforeTicksOverflow || days < -MaxDaysBeforeTicksOverflow)
            {
                return days > 0 ? new DateTimeOffset(DateTimeOffset.MaxValue.Ticks, value.Offset) : new DateTimeOffset(DateTimeOffset.MinValue.Ticks, value.Offset);
            }

            long ticks = days * TimeSpan.TicksPerDay;
            return value.SafeAddTicks(ticks);
        }
    }
}
