// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Extensions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class DateTimeSafeExtensionsTests
    {
        // SafeAddTicks - DateTime

        [Fact]
        public void GivenNormalDateTime_WhenSafeAddTicksCalled_ThenReturnsExpectedResult()
        {
            var dt = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
            DateTime result = dt.SafeAddTicks(TimeSpan.TicksPerMillisecond);
            Assert.Equal(dt.AddTicks(TimeSpan.TicksPerMillisecond), result);
        }

        [Fact]
        public void GivenDateTimeNearMaxValue_WhenSafeAddTicksPositive_ThenClampsToMaxValue()
        {
            var dt = DateTime.MaxValue.AddTicks(-100);
            DateTime result = dt.SafeAddTicks(TimeSpan.TicksPerMillisecond);
            Assert.Equal(DateTime.MaxValue, result);
        }

        [Fact]
        public void GivenDateTimeMaxValue_WhenSafeAddTicksPositive_ThenClampsToMaxValue()
        {
            DateTime result = DateTime.MaxValue.SafeAddTicks(1);
            Assert.Equal(DateTime.MaxValue, result);
        }

        [Fact]
        public void GivenDateTimeNearMinValue_WhenSafeAddTicksNegative_ThenClampsToMinValue()
        {
            var dt = DateTime.MinValue.AddTicks(100);
            DateTime result = dt.SafeAddTicks(-TimeSpan.TicksPerMillisecond);
            Assert.Equal(DateTime.MinValue, result);
        }

        [Fact]
        public void GivenDateTimeMinValue_WhenSafeAddTicksNegative_ThenClampsToMinValue()
        {
            DateTime result = DateTime.MinValue.SafeAddTicks(-1);
            Assert.Equal(DateTime.MinValue, result);
        }

        [Fact]
        public void GivenDateTime_WhenSafeAddTicksZero_ThenReturnsOriginal()
        {
            var dt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime result = dt.SafeAddTicks(0);
            Assert.Equal(dt, result);
        }

        [Fact]
        public void GivenAnyDateTime_WhenSafeAddTicksLongMinValue_ThenClampsToMinValue()
        {
            var dt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime result = dt.SafeAddTicks(long.MinValue);
            Assert.Equal(DateTime.MinValue, result);
        }

        [Fact]
        public void GivenUtcDateTime_WhenSafeAddTicksClampsToMaxValue_ThenPreservesKind()
        {
            var dt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime result = DateTime.MaxValue.AddTicks(-1).SafeAddTicks(TimeSpan.TicksPerDay);
            Assert.Equal(DateTimeKind.Unspecified, result.Kind);
            Assert.Equal(DateTime.MaxValue.Ticks, result.Ticks);
        }

        [Fact]
        public void GivenLocalDateTime_WhenSafeAddTicksClampsToMinValue_ThenPreservesKind()
        {
            var dt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Local);
            DateTime result = dt.AddTicks(1).SafeAddTicks(-TimeSpan.TicksPerDay);
            Assert.Equal(DateTimeKind.Local, result.Kind);
            Assert.Equal(DateTime.MinValue.Ticks, result.Ticks);
        }

        // SafeAddTicks - DateTimeOffset

        [Fact]
        public void GivenNormalDateTimeOffset_WhenSafeAddTicksCalled_ThenReturnsExpectedResult()
        {
            var dto = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);
            DateTimeOffset result = dto.SafeAddTicks(TimeSpan.TicksPerMillisecond);
            Assert.Equal(dto.AddTicks(TimeSpan.TicksPerMillisecond), result);
        }

        [Fact]
        public void GivenDateTimeOffsetNearMaxValue_WhenSafeAddTicksPositive_ThenClampsToMaxValue()
        {
            var dto = DateTimeOffset.MaxValue.AddTicks(-100);
            DateTimeOffset result = dto.SafeAddTicks(TimeSpan.TicksPerMillisecond);
            Assert.Equal(DateTimeOffset.MaxValue, result);
        }

        [Fact]
        public void GivenDateTimeOffsetNearMinValue_WhenSafeAddTicksNegative_ThenClampsToMinValue()
        {
            var dto = DateTimeOffset.MinValue.AddTicks(100);
            DateTimeOffset result = dto.SafeAddTicks(-TimeSpan.TicksPerMillisecond);
            Assert.Equal(DateTimeOffset.MinValue, result);
        }

        [Fact]
        public void GivenDateTimeOffset_WhenSafeAddTicksZero_ThenReturnsOriginal()
        {
            var offset = TimeSpan.FromHours(5);
            var dto = new DateTimeOffset(2024, 6, 15, 12, 0, 0, offset);
            DateTimeOffset result = dto.SafeAddTicks(0);
            Assert.Equal(dto, result);
            Assert.Equal(offset, result.Offset);
        }

        [Fact]
        public void GivenDateTimeOffset_WhenSafeAddTicksClampsWithNonZeroOffset_ThenPreservesOffset()
        {
            var offset = TimeSpan.FromHours(5);
            var dto = new DateTimeOffset(9999, 12, 31, 23, 59, 59, offset);
            DateTimeOffset result = dto.SafeAddTicks(TimeSpan.TicksPerDay);
            Assert.Equal(offset, result.Offset);
            Assert.Equal(DateTimeOffset.MaxValue.Ticks, result.Ticks);
        }

        // SafeAddDays - DateTime

        [Fact]
        public void GivenNormalDateTime_WhenSafeAddDaysCalled_ThenReturnsExpectedResult()
        {
            var dt = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            DateTime result = dt.SafeAddDays(1);
            Assert.Equal(dt.AddDays(1), result);
        }

        [Fact]
        public void GivenDateTimeNearMaxValue_WhenSafeAddDaysPositive_ThenClampsToMaxValue()
        {
            var dt = DateTime.MaxValue.AddDays(-0.5);
            DateTime result = dt.SafeAddDays(1);
            Assert.Equal(DateTime.MaxValue, result);
        }

        [Fact]
        public void GivenDateTimeNearMinValue_WhenSafeAddDaysNegative_ThenClampsToMinValue()
        {
            var dt = DateTime.MinValue.AddDays(0.5);
            DateTime result = dt.SafeAddDays(-1);
            Assert.Equal(DateTime.MinValue, result);
        }

        // SafeAddDays - DateTimeOffset

        [Fact]
        public void GivenNormalDateTimeOffset_WhenSafeAddDaysCalled_ThenReturnsExpectedResult()
        {
            var dto = new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset result = dto.SafeAddDays(1);
            Assert.Equal(dto.AddDays(1), result);
        }

        [Fact]
        public void GivenDateTimeOffsetNearMaxValue_WhenSafeAddDaysPositive_ThenClampsToMaxValue()
        {
            var dto = DateTimeOffset.MaxValue.AddDays(-0.5);
            DateTimeOffset result = dto.SafeAddDays(1);
            Assert.Equal(DateTimeOffset.MaxValue, result);
        }

        [Fact]
        public void GivenDateTimeOffsetNearMinValue_WhenSafeAddDaysNegative_ThenClampsToMinValue()
        {
            var dto = DateTimeOffset.MinValue.AddDays(0.5);
            DateTimeOffset result = dto.SafeAddDays(-1);
            Assert.Equal(DateTimeOffset.MinValue, result);
        }

        [Fact]
        public void GivenDateTime_WhenSafeAddDaysIntMaxValue_ThenClampsToMaxValue()
        {
            var dt = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            DateTime result = dt.SafeAddDays(int.MaxValue);
            Assert.Equal(DateTime.MaxValue.Ticks, result.Ticks);
        }

        [Fact]
        public void GivenDateTime_WhenSafeAddDaysIntMinValue_ThenClampsToMinValue()
        {
            var dt = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            DateTime result = dt.SafeAddDays(int.MinValue);
            Assert.Equal(DateTime.MinValue.Ticks, result.Ticks);
        }

        [Fact]
        public void GivenUtcDateTime_WhenSafeAddDaysClampsToMaxValue_ThenPreservesKind()
        {
            var dt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime result = dt.SafeAddDays(int.MaxValue);
            Assert.Equal(DateTimeKind.Utc, result.Kind);
            Assert.Equal(DateTime.MaxValue.Ticks, result.Ticks);
        }

        [Fact]
        public void GivenLocalDateTime_WhenSafeAddDaysClampsToMinValue_ThenPreservesKind()
        {
            var dt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Local);
            DateTime result = dt.SafeAddDays(int.MinValue);
            Assert.Equal(DateTimeKind.Local, result.Kind);
            Assert.Equal(DateTime.MinValue.Ticks, result.Ticks);
        }

        [Fact]
        public void GivenDateTimeOffset_WhenSafeAddDaysIntMaxValue_ThenClampsToMaxValue()
        {
            var dto = new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset result = dto.SafeAddDays(int.MaxValue);
            Assert.Equal(DateTimeOffset.MaxValue.Ticks, result.Ticks);
        }

        [Fact]
        public void GivenDateTimeOffset_WhenSafeAddDaysIntMinValue_ThenClampsToMinValue()
        {
            var dto = new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset result = dto.SafeAddDays(int.MinValue);
            Assert.Equal(DateTimeOffset.MinValue.Ticks, result.Ticks);
        }

        [Fact]
        public void GivenDateTimeOffset_WhenSafeAddDaysClampsToMaxValue_ThenPreservesOffset()
        {
            var offset = TimeSpan.FromHours(5);
            var dto = new DateTimeOffset(2024, 1, 1, 0, 0, 0, offset);
            DateTimeOffset result = dto.SafeAddDays(int.MaxValue);
            Assert.Equal(offset, result.Offset);
            Assert.Equal(DateTimeOffset.MaxValue.Ticks, result.Ticks);
        }
    }
}
