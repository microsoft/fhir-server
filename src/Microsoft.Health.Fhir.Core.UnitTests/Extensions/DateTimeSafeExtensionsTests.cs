// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
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
        public void GivenDateTimeNearMaxValue_WhenSafeAddTicksPositive_ThenConstrainsToMaxValue()
        {
            var dt = DateTime.MaxValue.AddTicks(-100);
            DateTime result = dt.SafeAddTicks(TimeSpan.TicksPerMillisecond);
            Assert.Equal(DateTime.MaxValue, result);
        }

        [Fact]
        public void GivenDateTimeNearMinValue_WhenSafeAddTicksNegative_ThenConstrainsToMinValue()
        {
            var dt = DateTime.MinValue.AddTicks(100);
            DateTime result = dt.SafeAddTicks(-TimeSpan.TicksPerMillisecond);
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
        public void GivenAnyDateTime_WhenSafeAddTicksLongMinValue_ThenConstrainsToMinValue()
        {
            var dt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime result = dt.SafeAddTicks(long.MinValue);
            Assert.Equal(DateTime.MinValue, result);
        }

        [Fact]
        public void GivenUtcDateTime_WhenSafeAddTicksConstrainsToMaxValue_ThenPreservesKind()
        {
            var dt = new DateTime(DateTime.MaxValue.Ticks - 1, DateTimeKind.Utc);
            DateTime result = dt.SafeAddTicks(TimeSpan.TicksPerDay);
            Assert.Equal(DateTimeKind.Utc, result.Kind);
            Assert.Equal(DateTime.MaxValue.Ticks, result.Ticks);
        }

        [Fact]
        public void GivenLocalDateTime_WhenSafeAddTicksConstrainsToMinValue_ThenPreservesKind()
        {
            var dt = new DateTime(DateTime.MinValue.Ticks + 1, DateTimeKind.Local);
            DateTime result = dt.SafeAddTicks(-TimeSpan.TicksPerDay);
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
        public void GivenDateTimeOffsetNearMaxValue_WhenSafeAddTicksPositive_ThenConstrainsToMaxValue()
        {
            var dto = DateTimeOffset.MaxValue.AddTicks(-100);
            DateTimeOffset result = dto.SafeAddTicks(TimeSpan.TicksPerMillisecond);
            Assert.Equal(DateTimeOffset.MaxValue, result);
        }

        [Fact]
        public void GivenDateTimeOffsetNearMinValue_WhenSafeAddTicksNegative_ThenConstrainsToMinValue()
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
        public void GivenDateTimeOffset_WhenSafeAddTicksConstrainsToMaxValue_ThenPreservesOffset()
        {
            var offset = TimeSpan.FromHours(5);
            var dto = new DateTimeOffset(9999, 12, 31, 23, 59, 59, offset);
            DateTimeOffset result = dto.SafeAddTicks(TimeSpan.TicksPerDay);
            Assert.Equal(offset, result.Offset);
            Assert.True(result.Ticks > dto.Ticks);
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
        public void GivenDateTimeNearMaxValue_WhenSafeAddDaysPositive_ThenConstrainsToMaxValue()
        {
            var dt = DateTime.MaxValue.AddDays(-0.5);
            DateTime result = dt.SafeAddDays(1);
            Assert.Equal(DateTime.MaxValue, result);
        }

        [Fact]
        public void GivenDateTimeNearMinValue_WhenSafeAddDaysNegative_ThenConstrainsToMinValue()
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
        public void GivenDateTimeOffsetNearMaxValue_WhenSafeAddDaysPositive_ThenConstrainsToMaxValue()
        {
            var dto = DateTimeOffset.MaxValue.AddDays(-0.5);
            DateTimeOffset result = dto.SafeAddDays(1);
            Assert.Equal(DateTimeOffset.MaxValue, result);
        }

        [Fact]
        public void GivenDateTimeOffsetNearMinValue_WhenSafeAddDaysNegative_ThenConstrainsToMinValue()
        {
            var dto = DateTimeOffset.MinValue.AddDays(0.5);
            DateTimeOffset result = dto.SafeAddDays(-1);
            Assert.Equal(DateTimeOffset.MinValue, result);
        }

        [Fact]
        public void GivenDateTime_WhenSafeAddDaysIntMaxValue_ThenConstrainsToMaxValue()
        {
            var dt = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            DateTime result = dt.SafeAddDays(int.MaxValue);
            Assert.Equal(DateTime.MaxValue.Ticks, result.Ticks);
        }

        [Fact]
        public void GivenDateTime_WhenSafeAddDaysIntMinValue_ThenConstrainsToMinValue()
        {
            var dt = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            DateTime result = dt.SafeAddDays(int.MinValue);
            Assert.Equal(DateTime.MinValue.Ticks, result.Ticks);
        }

        [Fact]
        public void GivenUtcDateTime_WhenSafeAddDaysConstrainsToMaxValue_ThenPreservesKind()
        {
            var dt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime result = dt.SafeAddDays(int.MaxValue);
            Assert.Equal(DateTimeKind.Utc, result.Kind);
            Assert.Equal(DateTime.MaxValue.Ticks, result.Ticks);
        }

        [Fact]
        public void GivenLocalDateTime_WhenSafeAddDaysConstrainsToMinValue_ThenPreservesKind()
        {
            var dt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Local);
            DateTime result = dt.SafeAddDays(int.MinValue);
            Assert.Equal(DateTimeKind.Local, result.Kind);
            Assert.Equal(DateTime.MinValue.Ticks, result.Ticks);
        }

        [Fact]
        public void GivenDateTimeOffset_WhenSafeAddDaysIntMaxValue_ThenConstrainsToMaxValue()
        {
            var dto = new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset result = dto.SafeAddDays(int.MaxValue);
            Assert.Equal(DateTimeOffset.MaxValue.Ticks, result.Ticks);
        }

        [Fact]
        public void GivenDateTimeOffset_WhenSafeAddDaysIntMinValue_ThenConstrainsToMinValue()
        {
            var dto = new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset result = dto.SafeAddDays(int.MinValue);
            Assert.Equal(DateTimeOffset.MinValue.Ticks, result.Ticks);
        }

        [Fact]
        public void GivenDateTimeOffset_WhenSafeAddDaysConstrainsToMaxValue_ThenPreservesOffset()
        {
            var offset = TimeSpan.FromHours(5);
            var dto = new DateTimeOffset(2024, 1, 1, 0, 0, 0, offset);
            DateTimeOffset result = dto.SafeAddDays(int.MaxValue);
            Assert.Equal(offset, result.Offset);
            Assert.True(result.UtcTicks <= DateTimeOffset.MaxValue.UtcTicks);
        }

        [Fact]
        public void GivenDateTimeOffsetWithNegativeOffset_WhenSafeAddDaysOverflows_ThenConstrainsWithoutThrowing()
        {
            var offset = TimeSpan.FromHours(-5);
            var dto = new DateTimeOffset(2024, 1, 1, 0, 0, 0, offset);
            DateTimeOffset result = dto.SafeAddDays(int.MaxValue);
            Assert.Equal(offset, result.Offset);
            Assert.Equal(DateTimeOffset.MaxValue.UtcTicks, result.UtcTicks);
        }

        // OverflowBehavior Tests - DateTime.SafeAddTicks

        [Theory]
        [InlineData(OverflowBehavior.Throw)]
        [InlineData(OverflowBehavior.ReturnOriginal)]
        public void GivenDateTimeNearMaxValue_WhenSafeAddTicksWithBehavior_ThenHandlesOverflow(OverflowBehavior behavior)
        {
            var dt = DateTime.MaxValue.AddTicks(-100);
            if (behavior == OverflowBehavior.Throw)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => dt.SafeAddTicks(TimeSpan.TicksPerMillisecond, behavior));
            }
            else
            {
                DateTime result = dt.SafeAddTicks(TimeSpan.TicksPerMillisecond, behavior);
                Assert.Equal(dt, result);
            }
        }

        [Theory]
        [InlineData(OverflowBehavior.Throw)]
        [InlineData(OverflowBehavior.ReturnOriginal)]
        public void GivenDateTimeNearMinValue_WhenSafeAddTicksNegativeWithBehavior_ThenHandlesOverflow(OverflowBehavior behavior)
        {
            var dt = DateTime.MinValue.AddTicks(100);
            if (behavior == OverflowBehavior.Throw)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => dt.SafeAddTicks(-TimeSpan.TicksPerMillisecond, behavior));
            }
            else
            {
                DateTime result = dt.SafeAddTicks(-TimeSpan.TicksPerMillisecond, behavior);
                Assert.Equal(dt, result);
            }
        }

        [Theory]
        [InlineData(OverflowBehavior.Constrain)]
        [InlineData(OverflowBehavior.Throw)]
        [InlineData(OverflowBehavior.ReturnOriginal)]
        public void GivenNormalDateTime_WhenSafeAddTicksWithBehavior_ThenReturnsExpectedResult(OverflowBehavior behavior)
        {
            var dt = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
            DateTime result = dt.SafeAddTicks(TimeSpan.TicksPerMillisecond, behavior);
            Assert.Equal(dt.AddTicks(TimeSpan.TicksPerMillisecond), result);
        }

        // OverflowBehavior Tests - DateTimeOffset.SafeAddTicks

        [Theory]
        [InlineData(OverflowBehavior.Throw)]
        [InlineData(OverflowBehavior.ReturnOriginal)]
        public void GivenDateTimeOffsetNearMaxValue_WhenSafeAddTicksWithBehavior_ThenHandlesOverflow(OverflowBehavior behavior)
        {
            var dto = DateTimeOffset.MaxValue.AddTicks(-100);
            if (behavior == OverflowBehavior.Throw)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => dto.SafeAddTicks(TimeSpan.TicksPerMillisecond, behavior));
            }
            else
            {
                DateTimeOffset result = dto.SafeAddTicks(TimeSpan.TicksPerMillisecond, behavior);
                Assert.Equal(dto, result);
            }
        }

        [Theory]
        [InlineData(OverflowBehavior.Throw)]
        [InlineData(OverflowBehavior.ReturnOriginal)]
        public void GivenDateTimeOffsetNearMinValue_WhenSafeAddTicksNegativeWithBehavior_ThenHandlesOverflow(OverflowBehavior behavior)
        {
            var dto = DateTimeOffset.MinValue.AddTicks(100);
            if (behavior == OverflowBehavior.Throw)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => dto.SafeAddTicks(-TimeSpan.TicksPerMillisecond, behavior));
            }
            else
            {
                DateTimeOffset result = dto.SafeAddTicks(-TimeSpan.TicksPerMillisecond, behavior);
                Assert.Equal(dto, result);
            }
        }

        [Theory]
        [InlineData(OverflowBehavior.Constrain)]
        [InlineData(OverflowBehavior.Throw)]
        [InlineData(OverflowBehavior.ReturnOriginal)]
        public void GivenNormalDateTimeOffset_WhenSafeAddTicksWithBehavior_ThenReturnsExpectedResult(OverflowBehavior behavior)
        {
            var dto = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);
            DateTimeOffset result = dto.SafeAddTicks(TimeSpan.TicksPerMillisecond, behavior);
            Assert.Equal(dto.AddTicks(TimeSpan.TicksPerMillisecond), result);
        }

        [Fact]
        public void GivenDateTimeOffsetWithCustomOffset_WhenSafeAddTicksWithReturnOriginalBehavior_ThenReturnsOriginalWithOffsetPreserved()
        {
            var offset = TimeSpan.FromHours(5);
            var unspecifiedDt = new DateTime(9999, 12, 31, 12, 0, 0, DateTimeKind.Unspecified);
            var dto = new DateTimeOffset(unspecifiedDt, offset);
            DateTimeOffset result = dto.SafeAddTicks(TimeSpan.TicksPerDay, OverflowBehavior.ReturnOriginal);
            Assert.Equal(dto, result);
            Assert.Equal(offset, result.Offset);
        }

        // OverflowBehavior Tests - DateTime.SafeAddDays

        [Theory]
        [InlineData(OverflowBehavior.Throw)]
        [InlineData(OverflowBehavior.ReturnOriginal)]
        public void GivenDateTimeNearMaxValue_WhenSafeAddDaysWithBehavior_ThenHandlesOverflow(OverflowBehavior behavior)
        {
            var dt = DateTime.MaxValue.AddDays(-0.5);
            if (behavior == OverflowBehavior.Throw)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => dt.SafeAddDays(1, behavior));
            }
            else
            {
                DateTime result = dt.SafeAddDays(1, behavior);
                Assert.Equal(dt, result);
            }
        }

        [Theory]
        [InlineData(OverflowBehavior.Throw)]
        [InlineData(OverflowBehavior.ReturnOriginal)]
        public void GivenDateTimeNearMinValue_WhenSafeAddDaysNegativeWithBehavior_ThenHandlesOverflow(OverflowBehavior behavior)
        {
            var dt = DateTime.MinValue.AddDays(0.5);
            if (behavior == OverflowBehavior.Throw)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => dt.SafeAddDays(-1, behavior));
            }
            else
            {
                DateTime result = dt.SafeAddDays(-1, behavior);
                Assert.Equal(dt, result);
            }
        }

        [Theory]
        [InlineData(OverflowBehavior.Constrain)]
        [InlineData(OverflowBehavior.Throw)]
        [InlineData(OverflowBehavior.ReturnOriginal)]
        public void GivenNormalDateTime_WhenSafeAddDaysWithBehavior_ThenReturnsExpectedResult(OverflowBehavior behavior)
        {
            var dt = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            DateTime result = dt.SafeAddDays(1, behavior);
            Assert.Equal(dt.AddDays(1), result);
        }

        // OverflowBehavior Tests - DateTimeOffset.SafeAddDays

        [Theory]
        [InlineData(OverflowBehavior.Throw)]
        [InlineData(OverflowBehavior.ReturnOriginal)]
        public void GivenDateTimeOffsetNearMaxValue_WhenSafeAddDaysWithBehavior_ThenHandlesOverflow(OverflowBehavior behavior)
        {
            var dto = DateTimeOffset.MaxValue.AddDays(-0.5);
            if (behavior == OverflowBehavior.Throw)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => dto.SafeAddDays(1, behavior));
            }
            else
            {
                DateTimeOffset result = dto.SafeAddDays(1, behavior);
                Assert.Equal(dto, result);
            }
        }

        [Theory]
        [InlineData(OverflowBehavior.Throw)]
        [InlineData(OverflowBehavior.ReturnOriginal)]
        public void GivenDateTimeOffsetNearMinValue_WhenSafeAddDaysNegativeWithBehavior_ThenHandlesOverflow(OverflowBehavior behavior)
        {
            var dto = DateTimeOffset.MinValue.AddDays(0.5);
            if (behavior == OverflowBehavior.Throw)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => dto.SafeAddDays(-1, behavior));
            }
            else
            {
                DateTimeOffset result = dto.SafeAddDays(-1, behavior);
                Assert.Equal(dto, result);
            }
        }

        [Theory]
        [InlineData(OverflowBehavior.Constrain)]
        [InlineData(OverflowBehavior.Throw)]
        [InlineData(OverflowBehavior.ReturnOriginal)]
        public void GivenNormalDateTimeOffset_WhenSafeAddDaysWithBehavior_ThenReturnsExpectedResult(OverflowBehavior behavior)
        {
            var dto = new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset result = dto.SafeAddDays(1, behavior);
            Assert.Equal(dto.AddDays(1), result);
        }

        [Fact]
        public void GivenDateTimeOffsetWithCustomOffset_WhenSafeAddDaysWithReturnOriginalBehavior_ThenReturnsOriginalWithOffsetPreserved()
        {
            var offset = TimeSpan.FromHours(-7);
            var dto = new DateTimeOffset(2024, 1, 1, 0, 0, 0, offset);
            DateTimeOffset result = dto.SafeAddDays(int.MaxValue, OverflowBehavior.ReturnOriginal);
            Assert.Equal(dto, result);
            Assert.Equal(offset, result.Offset);
        }

        // Logging Tests

        [Fact]
        public void GivenDateTimeNearMaxValue_WhenSafeAddTicksWithLoggerAndThrowBehavior_ThenLogsWarningBeforeThrowing()
        {
            var logger = Substitute.For<ILogger>();
            var dt = DateTime.MaxValue.AddTicks(-100);

            Assert.Throws<ArgumentOutOfRangeException>(() => dt.SafeAddTicks(TimeSpan.TicksPerMillisecond, OverflowBehavior.Throw, logger));

            logger.ReceivedWithAnyArgs().Log(default, default, default, default, default!);
        }

        [Fact]
        public void GivenDateTimeNearMaxValue_WhenSafeAddTicksWithLoggerAndConstrainBehavior_ThenLogsWarningAndConstrains()
        {
            var logger = Substitute.For<ILogger>();
            var dt = DateTime.MaxValue.AddTicks(-100);

            DateTime result = dt.SafeAddTicks(TimeSpan.TicksPerMillisecond, OverflowBehavior.Constrain, logger);

            Assert.Equal(DateTime.MaxValue, result);
            logger.ReceivedWithAnyArgs().Log(default, default, default, default, default!);
        }

        [Fact]
        public void GivenNormalDateTime_WhenSafeAddTicksWithLogger_ThenDoesNotLog()
        {
            var logger = Substitute.For<ILogger>();
            var dt = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);

            DateTime result = dt.SafeAddTicks(TimeSpan.TicksPerMillisecond, OverflowBehavior.Constrain, logger);

            Assert.Equal(dt.AddTicks(TimeSpan.TicksPerMillisecond), result);
            logger.DidNotReceiveWithAnyArgs().Log(default, default, default, default, default!);
        }

        [Fact]
        public void GivenDateTimeNearMaxValue_WhenSafeAddDaysWithLoggerAndConstrainBehavior_ThenLogsWarning()
        {
            var logger = Substitute.For<ILogger>();
            var dt = DateTime.MaxValue.AddDays(-0.5);

            DateTime result = dt.SafeAddDays(1, OverflowBehavior.Constrain, logger);

            Assert.Equal(DateTime.MaxValue, result);
            logger.ReceivedWithAnyArgs().Log(default, default, default, default, default!);
        }

        [Fact]
        public void GivenDateTimeOffsetNearMaxValue_WhenSafeAddTicksWithLoggerAndConstrainBehavior_ThenLogsWarning()
        {
            var logger = Substitute.For<ILogger>();
            var dto = DateTimeOffset.MaxValue.AddTicks(-100);

            DateTimeOffset result = dto.SafeAddTicks(TimeSpan.TicksPerMillisecond, OverflowBehavior.Constrain, logger);

            Assert.Equal(DateTimeOffset.MaxValue, result);
            logger.ReceivedWithAnyArgs().Log(default, default, default, default, default!);
        }

        [Fact]
        public void GivenDateTimeOffsetNearMaxValue_WhenSafeAddDaysWithLoggerAndConstrainBehavior_ThenLogsWarning()
        {
            var logger = Substitute.For<ILogger>();
            var dto = DateTimeOffset.MaxValue.AddDays(-0.5);

            DateTimeOffset result = dto.SafeAddDays(1, OverflowBehavior.Constrain, logger);

            Assert.Equal(DateTimeOffset.MaxValue, result);
            logger.ReceivedWithAnyArgs().Log(default, default, default, default, default!);
        }
    }
}
