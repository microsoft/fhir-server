// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    /// <summary>
    /// Defines behavior when DateTime or DateTimeOffset arithmetic overflows.
    /// </summary>
    public enum OverflowBehavior
    {
        /// <summary>
        /// Constrain the result to representable bounds. For DateTime, this is <see cref="DateTime.MinValue"/> or <see cref="DateTime.MaxValue"/>.
        /// For DateTimeOffset with a non-zero offset, the bounds are adjusted to the nearest representable value for that offset.
        /// This is the default and safest option.
        /// </summary>
        Constrain = 0,

        /// <summary>
        /// Throw <see cref="ArgumentOutOfRangeException"/> on overflow (same as standard AddTicks/AddDays).
        /// </summary>
        Throw = 1,

        /// <summary>
        /// Return the original value unchanged on overflow.
        /// </summary>
        ReturnOriginal = 2,
    }

    /// <summary>
    /// Extension methods for <see cref="DateTime"/> and <see cref="DateTimeOffset"/> that provide safe arithmetic
    /// operations with configurable overflow behavior. Default behavior constrains results to representable bounds
    /// instead of throwing on overflow. For DateTime, bounds are <see cref="DateTime.MinValue"/>/<see cref="DateTime.MaxValue"/>.
    /// For DateTimeOffset, bounds are adjusted to the nearest representable values for the current offset.
    /// </summary>
    public static class DateTimeSafeExtensions
    {
        private const long MaxDaysBeforeTicksOverflow = long.MaxValue / TimeSpan.TicksPerDay;

        /// <summary>
        /// Adds the specified number of ticks to a <see cref="DateTime"/>, constraining the result
        /// to <see cref="DateTime.MinValue"/> or <see cref="DateTime.MaxValue"/> on overflow.
        /// </summary>
        /// <param name="value">The DateTime value.</param>
        /// <param name="ticks">The number of ticks to add.</param>
        /// <param name="behavior">The behavior to apply on overflow (default: Constrain).</param>
        /// <param name="logger">Optional logger for overflow events.</param>
        /// <returns>The result of adding ticks, or constrained/original value based on behavior.</returns>
        public static DateTime SafeAddTicks(this DateTime value, long ticks, OverflowBehavior behavior = OverflowBehavior.Constrain, ILogger? logger = null)
        {
            if (ticks == 0)
            {
                return value;
            }

            try
            {
                return value.AddTicks(ticks);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                logger?.LogWarning(ex, "DateTime.AddTicks overflow: value={DateTime}, ticks={Ticks}", value, ticks);

                if (behavior == OverflowBehavior.Throw)
                {
                    throw;
                }

                if (behavior == OverflowBehavior.ReturnOriginal)
                {
                    return value;
                }

                return ticks > 0
                    ? new DateTime(DateTime.MaxValue.Ticks, value.Kind)
                    : new DateTime(DateTime.MinValue.Ticks, value.Kind);
            }
        }

        /// <summary>
        /// Adds the specified number of ticks to a <see cref="DateTimeOffset"/>, constraining the result
        /// to the nearest representable value (for the current offset) on overflow.
        /// </summary>
        /// <param name="value">The DateTimeOffset value.</param>
        /// <param name="ticks">The number of ticks to add.</param>
        /// <param name="behavior">The behavior to apply on overflow (default: Constrain).</param>
        /// <param name="logger">Optional logger for overflow events.</param>
        /// <returns>The result of adding ticks, or constrained/original value based on behavior.</returns>
        public static DateTimeOffset SafeAddTicks(this DateTimeOffset value, long ticks, OverflowBehavior behavior = OverflowBehavior.Constrain, ILogger? logger = null)
        {
            if (ticks == 0)
            {
                return value;
            }

            try
            {
                return value.AddTicks(ticks);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                logger?.LogWarning(ex, "DateTimeOffset.AddTicks overflow: value={DateTimeOffset}, ticks={Ticks}", value, ticks);

                if (behavior == OverflowBehavior.Throw)
                {
                    throw;
                }

                if (behavior == OverflowBehavior.ReturnOriginal)
                {
                    return value;
                }

                // Calculate max/min representable local ticks for this offset.
                // UTC = Local - Offset, so Local = UTC + Offset.
                // Max UTC ticks = DateTimeOffset.MaxValue.Ticks, so max Local ticks = DateTimeOffset.MaxValue.Ticks + Offset.Ticks.
                // When Offset >= 0, max stays at DateTimeOffset.MaxValue.Ticks (no UTC overflow).
                // When Offset < 0, max = DateTimeOffset.MaxValue.Ticks + Offset.Ticks (safe addition).
                long maxTicks = value.Offset.Ticks >= 0
                    ? DateTimeOffset.MaxValue.Ticks
                    : DateTimeOffset.MaxValue.Ticks + value.Offset.Ticks;

                long minTicks = value.Offset.Ticks <= 0
                    ? DateTimeOffset.MinValue.Ticks
                    : DateTimeOffset.MinValue.Ticks + value.Offset.Ticks;

                return ticks > 0
                    ? new DateTimeOffset(maxTicks, value.Offset)
                    : new DateTimeOffset(minTicks, value.Offset);
            }
        }

        /// <summary>
        /// Adds the specified number of days to a <see cref="DateTime"/>, constraining the result
        /// to <see cref="DateTime.MinValue"/> or <see cref="DateTime.MaxValue"/> on overflow.
        /// </summary>
        /// <param name="value">The DateTime value.</param>
        /// <param name="days">The number of days to add.</param>
        /// <param name="behavior">The behavior to apply on overflow (default: Constrain).</param>
        /// <param name="logger">Optional logger for overflow events.</param>
        /// <returns>The result of adding days, or constrained/original value based on behavior.</returns>
        public static DateTime SafeAddDays(this DateTime value, int days, OverflowBehavior behavior = OverflowBehavior.Constrain, ILogger? logger = null)
        {
            // Detect if days * TimeSpan.TicksPerDay would overflow long.
            if (days > MaxDaysBeforeTicksOverflow || days < -MaxDaysBeforeTicksOverflow)
            {
                logger?.LogWarning("DateTime.AddDays overflow: value={DateTime}, days={Days}", value, days);

                if (behavior == OverflowBehavior.Throw)
                {
                    throw new ArgumentOutOfRangeException(nameof(days));
                }

                if (behavior == OverflowBehavior.ReturnOriginal)
                {
                    return value;
                }

                return days > 0 ? new DateTime(DateTime.MaxValue.Ticks, value.Kind) : new DateTime(DateTime.MinValue.Ticks, value.Kind);
            }

            long ticks = days * TimeSpan.TicksPerDay;
            return value.SafeAddTicks(ticks, behavior, logger);
        }

        /// <summary>
        /// Adds the specified number of days to a <see cref="DateTimeOffset"/>, constraining the result
        /// to the nearest representable value (for the current offset) on overflow.
        /// </summary>
        /// <param name="value">The DateTimeOffset value.</param>
        /// <param name="days">The number of days to add.</param>
        /// <param name="behavior">The behavior to apply on overflow (default: Constrain).</param>
        /// <param name="logger">Optional logger for overflow events.</param>
        /// <returns>The result of adding days, or constrained/original value based on behavior.</returns>
        public static DateTimeOffset SafeAddDays(this DateTimeOffset value, int days, OverflowBehavior behavior = OverflowBehavior.Constrain, ILogger? logger = null)
        {
            // Detect if days * TimeSpan.TicksPerDay would overflow long.
            if (days > MaxDaysBeforeTicksOverflow || days < -MaxDaysBeforeTicksOverflow)
            {
                logger?.LogWarning("DateTimeOffset.AddDays overflow: value={DateTimeOffset}, days={Days}", value, days);

                if (behavior == OverflowBehavior.Throw)
                {
                    throw new ArgumentOutOfRangeException(nameof(days));
                }

                if (behavior == OverflowBehavior.ReturnOriginal)
                {
                    return value;
                }

                // Constrain to the representable range for the current offset.
                long offsetTicks = value.Offset.Ticks;
                long maxTicks = offsetTicks < 0 ? DateTime.MaxValue.Ticks + offsetTicks : DateTime.MaxValue.Ticks;
                long minTicks = offsetTicks > 0 ? DateTime.MinValue.Ticks + offsetTicks : DateTime.MinValue.Ticks;
                return days > 0 ? new DateTimeOffset(maxTicks, value.Offset) : new DateTimeOffset(minTicks, value.Offset);
            }

            long ticks = days * TimeSpan.TicksPerDay;
            return value.SafeAddTicks(ticks, behavior, logger);
        }
    }
}
