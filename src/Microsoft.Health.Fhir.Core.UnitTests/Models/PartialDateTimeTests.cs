// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Models
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class PartialDateTimeTests : IDisposable
    {
        private readonly CultureInfo _originalCulture;

        public PartialDateTimeTests()
        {
            _originalCulture = Thread.CurrentThread.CurrentCulture;
        }

        [Theory]
        [InlineData("05")] // Year needs to be specified.
        [InlineData("05-18")]
        [InlineData("05-18T23:57")]
        [InlineData("05-18T23:57:09")]
        [InlineData("05-18T23:57:09.931094")]
        [InlineData("05-18T23:57+01:00")]
        [InlineData("05-18T23:57:09+01:00")]
        [InlineData("05-18T23:57:09.931094+01:00")]
        [InlineData("2013-05T23:57")] // Month/date needs to be specified.
        [InlineData("2013-05T23:57:09")]
        [InlineData("2013-05T23:57:09.931094")]
        [InlineData("2013-05T23:57+01:00")]
        [InlineData("2013-05T23:57:09+01:00")]
        [InlineData("2013-05T23:57:09.931094+01:00")]
        [InlineData("2013T23:57")] // Month and date need to be specified.
        [InlineData("2013T23:57:09")]
        [InlineData("2013T23:57:09.931094")]
        [InlineData("2013T23:57+01:00")]
        [InlineData("2013T23:57:09+01:00")]
        [InlineData("2013T23:57:09.931094+01:00")]
        [InlineData("T23:57")] // Year, month and date need to be specified.
        [InlineData("T23:57:09")]
        [InlineData("T23:57:09.931094")]
        [InlineData("T23:57+01:00")]
        [InlineData("T23:57:09+01:00")]
        [InlineData("T23:57:09.931094+01:00")]
        [InlineData("2013-05-18T23:09.931094")] // Hour/minute/second needs to be specified.
        [InlineData("2013-05-18T23:09.931094+01:00")]
        [InlineData("2013-05-18T09.931094")] // Hour and minute need to be specified.
        [InlineData("2013-05-18T09.931094+01:00")]
        [InlineData("2013-05-18T.931094")] // Hour, minute and second need to be specified.
        [InlineData("2013-05-18T.931094+01:00")]
        public void GivenPreviousParamIsNotSpecified_WhenParsingPartialDateTime_ThenFormatExceptionShouldBeThrown(string inputString)
        {
            Exception ex = Assert.Throws<FormatException>(() => PartialDateTime.Parse(inputString));

            string expectedMessage = string.Format(Resources.DateTimeStringIsIncorrectlyFormatted, inputString);
            Assert.Equal(expectedMessage, ex.Message);
        }

        [Theory]
        [InlineData("2013+01:00")] // Time needs to be specified if UTC offset is specified.
        [InlineData("2013-05+01:00")]
        [InlineData("2013-05-18+01:00")]
        public void GivenUtcOffsetButNoTime_WhenParsingPartialDateTime_ThenFormatExceptionShouldBeThrown(string inputString)
        {
            Exception ex = Assert.Throws<FormatException>(() => PartialDateTime.Parse(inputString));

            string expectedMessage = string.Format(Resources.DateTimeStringIsIncorrectlyFormatted, inputString);
            Assert.Equal(expectedMessage, ex.Message);
        }

        [Theory]
        [InlineData("2013-05-18T23")] // Minutes need to be specified if hour is specified.
        [InlineData("2013-05-18T23+01:00")]
        public void GivenHourIsSpecifiedWithoutMinutes_WhenParsingPartialDateTime_ThenFormatExceptionShouldBeThrown(string inputString)
        {
            Exception ex = Assert.Throws<FormatException>(() => PartialDateTime.Parse(inputString));

            string expectedMessage = string.Format(Resources.DateTimeStringIsIncorrectlyFormatted, inputString);
            Assert.Equal(expectedMessage, ex.Message);
        }

        [Theory]
        [InlineData("2013-05-18T23:09+1:00")]
        [InlineData("2013-05-18T23:09-4:30")]
        [InlineData("2013-05-18T23:09+01:300")]
        [InlineData("2013-05-18T23:09+01:77")]
        [InlineData("2013-05-18T23:09+1")]
        [InlineData("2013-05-18T23:09-7")]
        [InlineData("2013-05-18T23:09-07")]
        [InlineData("2013-05-18T23:09+Z")]
        [InlineData("2013-05-18T23:09-Z")]
        [InlineData("2013-05-18T23:09ZZ")]
        [InlineData("2013-05-18T23:09-99:00")]
        [InlineData("2013-05-18T23:09-789")]
        public void GivenInvalidUtcOffset_WhenParsingPartialDateTime_ThenFormatExceptionShouldBeThrown(string inputString)
        {
            Exception ex = Assert.Throws<FormatException>(() => PartialDateTime.Parse(inputString));

            string expectedMessage = string.Format(Resources.DateTimeStringIsIncorrectlyFormatted, inputString);
            Assert.Equal(expectedMessage, ex.Message);
        }

        [Theory]
        [InlineData("0000-05-18T23:57:09.931094+01:00")] // Year cannot be less than 1.
        [InlineData("10000-05-18T23:57:09.931094+01:00")] // Year cannot be greater than 9999.
        [InlineData("2013-00-18T23:57:09.931094+01:00")] // Month cannot be less than 1.
        [InlineData("2013-13-18T23:57:09.931094+01:00")] // Month cannot be greater than 12.
        [InlineData("2013-05-00T23:57:09.931094+01:00")] // Day cannot be less than 1.
        [InlineData("2013-05-32T23:57:09.931094+01:00")] // Day cannot be greater than 31 in May.
        [InlineData("2013-02-29T23:57:09.931094+01:00")] // Day cannot be greater than 28 in non-leap year.
        [InlineData("2020-02-30T23:57:09.931094+01:00")] // Day cannot be greater than 29 in leap year.
        [InlineData("2013-05-18T-01:57:09.931094+01:00")] // Hour cannot be less than 0.
        [InlineData("2013-05-18T24:00:00Z")] // Hour cannot be greater than 23.
        [InlineData("2013-05-18T23:-01:09.931094+01:00")] // Minute cannot be less than 0.
        [InlineData("2013-05-18T23:60:00Z")] // Minute cannot be greater than 59.
        [InlineData("2013-05-18T23:57:-01.931094+01:00")] // Second cannot be less than 0.
        [InlineData("2013-05-18T23:57:60Z")] // Second cannot be greater than 59.
        [InlineData("2013-05-18T23:57:09.999999999999999999999999+01:00")] // Fraction cannot be rounded up to 1 minute.
        public void GivenAOutOfRangeParameter_WhenInitializing_ThenFormatExceptionShouldBeThrown(string inputString)
        {
            Exception ex = Assert.Throws<FormatException>(() => PartialDateTime.Parse(inputString));

            string expectedMessage = string.Format(Resources.DateTimeStringIsIncorrectlyFormatted, inputString);
            Assert.Equal(expectedMessage, ex.Message);
        }

        public static IEnumerable<object[]> GetParameterNullData()
        {
            yield return new object[] { "1999", 1999, null, null, null, null, null, null, null };
            yield return new object[] { "1999-10", 1999, 10, null, null, null, null, null, null };
            yield return new object[] { "1999-10-01", 1999, 10, 1, null, null, null, null, null };
            yield return new object[] { "1999-10-18T12:35", 1999, 10, 18, 12, 35, null, null, 0 };
            yield return new object[] { "1999-10-18T12:35+01:00", 1999, 10, 18, 12, 35, null, null, 60 };
            yield return new object[] { "1999-10-18T12:35:55-02:30", 1999, 10, 18, 12, 35, 55, null, -150 };
            yield return new object[] { "1999-10-18T12:35:55Z", 1999, 10, 18, 12, 35, 55, null, 0 };
            yield return new object[] { "1999-10-18T12:35:55.9991532-02:30", 1999, 10, 18, 12, 35, 55, 0.9991532m, -150 };
        }

        [Theory]
        [MemberData(nameof(GetParameterNullData))]
        public void GivenANullParameter_WhenInitialized_ThenCorrectPartialDateTimeShouldBeCreated(
            string inputString,
            int year,
            int? month,
            int? day,
            int? hour,
            int? minute,
            int? second,
            decimal? fraction,
            int? utcOffsetInMinutes)
        {
            TimeSpan? utcOffset = utcOffsetInMinutes == null ? null : TimeSpan.FromMinutes(utcOffsetInMinutes.Value);

            var dateTime = PartialDateTime.Parse(inputString);

            Assert.Equal(year, dateTime.Year);
            Assert.Equal(month, dateTime.Month);
            Assert.Equal(day, dateTime.Day);
            Assert.Equal(hour, dateTime.Hour);
            Assert.Equal(minute, dateTime.Minute);
            Assert.Equal(second, dateTime.Second);
            Assert.Equal(fraction, dateTime.Fraction);
            Assert.Equal(utcOffset, dateTime.UtcOffset);
        }

        [Fact]
        public void GivenADateTimeOffset_WhenInitialized_ThenCorrectPartialDateTimeShouldBeCreated()
        {
            const int year = 2018;
            const int month = 5;
            const int day = 20;
            const int hour = 21;
            const int minute = 23;
            const int second = 59;
            const int millisecond = 153;
            const int fraction = 1234;
            var utcOffset = TimeSpan.FromMinutes(240);

            var dateTimeOffset = new DateTimeOffset(year, month, day, hour, minute, second, millisecond, utcOffset).AddTicks(fraction);

            var partialDateTime = new PartialDateTime(dateTimeOffset);

            Assert.Equal(year, partialDateTime.Year);
            Assert.Equal(month, partialDateTime.Month);
            Assert.Equal(day, partialDateTime.Day);
            Assert.Equal(hour, partialDateTime.Hour);
            Assert.Equal(minute, partialDateTime.Minute);
            Assert.Equal(second, partialDateTime.Second);
            Assert.Equal(0.1531234m, partialDateTime.Fraction);
            Assert.Equal(utcOffset, partialDateTime.UtcOffset);
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        [InlineData("        ")]
        public void GivenAnEmptyStringOrWhiteSpaces_WhenParsing_ThenArgumentExceptionShouldBeThrown(string inputString)
        {
            Assert.Throws<ArgumentException>(() => PartialDateTime.Parse(inputString));
        }

        [Fact]
        public void GivenANullString_WhenParsing_ThenArgumentNullExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(() => PartialDateTime.Parse(null));
        }

        [Theory]
        [InlineData("****")]
        [InlineData("!")]
        [InlineData("abc")]
        public void GivenAnInvalidString_WhenParsing_ThenFormatExceptionShouldBeThrown(string inputString)
        {
            Exception ex = Assert.Throws<FormatException>(() => PartialDateTime.Parse(inputString));

            string expectedMessage = string.Format(Resources.DateTimeStringIsIncorrectlyFormatted, inputString);
            Assert.Equal(expectedMessage, ex.Message);
        }

        public static IEnumerable<object[]> GetParseData()
        {
            yield return new object[] { "2017-01-07T11:21:12", 2017, 1, 7, 11, 21, 12, null, 0 };
            yield return new object[] { "2017-02-15T13:30:00Z", 2017, 2, 15, 13, 30, 0, null, 0 };
            yield return new object[] { "2018-02-03T05:30:03.0Z", 2018, 2, 3, 5, 30, 3, 0.0m, 0 };
            yield return new object[] { "2013-10-12T23:23:35.9995555+12:00", 2013, 10, 12, 23, 23, 35, 0.9995555m, 720 };
            yield return new object[] { "2015-12-12T20:00:24.1001+03:00", 2015, 12, 12, 20, 00, 24, 0.1001000m, 180 };
            yield return new object[] { "2017-01-01T04:30:20.100001+03:00", 2017, 01, 01, 04, 30, 20, 0.1000010m, 180 };
        }

        [Theory]
        [MemberData(nameof(GetParseData))]
        public void GivenAValidString_WhenParsed_ThenCorrectPartialDateTimeShouldBeCreated(
            string inputString,
            int year,
            int? month,
            int? day,
            int? hour,
            int? minute,
            int? second,
            decimal? fraction,
            int? utcOffsetInMinutes)
        {
            TimeSpan? utcOffset = utcOffsetInMinutes == null ? null : TimeSpan.FromMinutes(utcOffsetInMinutes.Value);

            var dateTime = PartialDateTime.Parse(inputString);

            Assert.Equal(year, dateTime.Year);
            Assert.Equal(month, dateTime.Month);
            Assert.Equal(day, dateTime.Day);
            Assert.Equal(hour, dateTime.Hour);
            Assert.Equal(minute, dateTime.Minute);
            Assert.Equal(second, dateTime.Second);
            Assert.Equal(fraction, dateTime.Fraction);
            Assert.Equal(utcOffset, dateTime.UtcOffset);
        }

        [Fact]
        public void GivenAPartialDateTimeWithNoMissingComponent_WhenToDateTimeOffsetWithoutArgumentsIsCalled_ThenCorrectDateTimeOffsetIsReturned()
        {
            var dateTime = PartialDateTime.Parse("2013-10-12T23:01:35.9995555+02:00");

            var actualOffset = dateTime.ToDateTimeOffset();

            var expectedOffset = new DateTimeOffset(
                2013,
                10,
                12,
                23,
                01,
                35,
                TimeSpan.FromMinutes(120));

            expectedOffset = expectedOffset.AddTicks((long)(0.9995555m * TimeSpan.TicksPerSecond));

            Assert.Equal(expectedOffset, actualOffset);
        }

        [Fact]
        public void GivenAPartialDateTimeWithNoMissingComponent_WhenToDateTimeOffsetWithArgumentsIsCalled_ThenCorrectDateTimeOffsetIsReturned()
        {
            var dateTime = PartialDateTime.Parse("2013-10-12T23:01:35.9995555+02:00");

            var actualOffset = dateTime.ToDateTimeOffset(
                2,
                (year, month) => 10,
                15,
                13,
                12,
                0.300250m,
                TimeSpan.FromMinutes(30));

            var expectedOffset = new DateTimeOffset(
                2013,
                10,
                12,
                23,
                01,
                35,
                TimeSpan.FromMinutes(120));

            expectedOffset = expectedOffset.AddTicks((long)(0.9995555m * TimeSpan.TicksPerSecond));

            Assert.Equal(expectedOffset, actualOffset);
        }

        [Fact]
        public void GivenAPartialDateTimeWithMissingComponents_WhenToDateTimeOffsetWithoutArgumentsIsCalled_ThenCorrectDateTimeOffsetIsReturned()
        {
            const int expectedMonth = 1;
            const int expectedDay = 1;
            const int expectedHour = 0;
            const int expectedMinute = 0;
            const int expectedSecond = 0;
            const decimal expectedFraction = 0.0m;
            var expectedUtcOffset = TimeSpan.FromMinutes(0);

            var dateTime = PartialDateTime.Parse("2013");

            var actualOffset = dateTime.ToDateTimeOffset();

            var expectedOffset = new DateTimeOffset(
                2013,
                expectedMonth,
                expectedDay,
                expectedHour,
                expectedMinute,
                expectedSecond,
                expectedUtcOffset);

            expectedOffset = expectedOffset.AddTicks((long)(expectedFraction * TimeSpan.TicksPerSecond));

            Assert.Equal(expectedOffset, actualOffset);
        }

        [Fact]
        public void GivenAPartialDateTimeWithMissingComponents_WhenToDateTimeOffsetWithArgumentsIsCalled_ThenCorrectDateTimeOffsetIsReturned()
        {
            const int expectedMonth = 2;
            const int expectedDay = 10;
            const int expectedHour = 15;
            const int expectedMinute = 13;
            const int expectedSecond = 12;
            const decimal expectedFraction = 0.3009953m;
            var expectedUtcOffset = TimeSpan.FromMinutes(30);

            var dateTime = PartialDateTime.Parse("2013");

            var actualOffset = dateTime.ToDateTimeOffset(
                expectedMonth,
                (year, month) => expectedDay,
                expectedHour,
                expectedMinute,
                expectedSecond,
                expectedFraction,
                expectedUtcOffset);

            var expectedOffset = new DateTimeOffset(
                2013,
                expectedMonth,
                expectedDay,
                expectedHour,
                expectedMinute,
                expectedSecond,
                expectedUtcOffset);

            expectedOffset = expectedOffset.AddTicks((long)(expectedFraction * TimeSpan.TicksPerSecond));

            Assert.Equal(expectedOffset, actualOffset);
        }

        [Theory]
        [InlineData("2017", "2017")]
        [InlineData("2017-01", "2017-01")]
        [InlineData("2018-01-25", "2018-01-25")]
        [InlineData("2018-01-25T12:15", "2018-01-25T12:15+00:00")]
        [InlineData("2018-01-25T07:55+05:30", "2018-01-25T07:55+05:30")]
        [InlineData("2018-01-25T10:12:15", "2018-01-25T10:12:15+00:00")]
        [InlineData("2018-01-25T10:12:03.229", "2018-01-25T10:12:03.2290000+00:00")]
        [InlineData("2018-01-25T03:01:58+05:30", "2018-01-25T03:01:58+05:30")]
        [InlineData("2018-01-09T00:23:25.0+10:00", "2018-01-09T00:23:25.0000000+10:00")]
        [InlineData("2018-01-09T00:23:25.1234567-01:00", "2018-01-09T00:23:25.1234567-01:00")]
        [InlineData("2018-11-29T18:30:27.911+01:00", "2018-11-29T18:30:27.9110000+01:00")]
        public void GivenAValidPartialDateTime_WhenToStringIsCalled_ThenCorrectStringShouldBeReturned(string input, string expected)
        {
            var dateTime = PartialDateTime.Parse(input);

            Assert.NotNull(dateTime);
            Assert.Equal(expected, dateTime.ToString());
        }

        [Theory]
        [InlineData("de-DE", "2018-11-29T18:30:27.911+01:00", "2018-11-29T18:30:27,9110000+01:00")]
        [InlineData("en-GB", "2018-11-29T18:30:27.911+01:00", "2018-11-29T18:30:27.9110000+01:00")]
        [InlineData("en-US", "2018-11-29T18:30:27.911+01:00", "2018-11-29T18:30:27.9110000+01:00")]
        public void GivenACulture_WhenToStringIsCalled_ThenCorrectStringShouldBeReturned(string culture, string inputString, string expectedString)
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
            var dateTime = PartialDateTime.Parse(inputString);

            Assert.NotNull(dateTime);
            Assert.Equal(expectedString, dateTime.ToString());
        }

        public void Dispose()
        {
            Thread.CurrentThread.CurrentCulture = _originalCulture;
        }
    }
}
