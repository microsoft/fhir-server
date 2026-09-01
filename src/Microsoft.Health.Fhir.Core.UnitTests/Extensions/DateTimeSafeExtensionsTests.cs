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
    }
}
