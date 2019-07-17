// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Microsoft.Health.Fhir.Core.Models;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Models
{
    public class PartialDateTimeTests : IDisposable
    {
        private const string ParamNameYear = "year";
        private const string ParamNameMonth = "month";
        private const string ParamNameDay = "day";
        private const string ParamNameHour = "hour";
        private const string ParamNameMinute = "minute";
        private const string ParamNameSecond = "second";
        private const string ParamNameFraction = "fraction";
        private const string ParamNameUtcOffset = "utcOffset";
        private const string ParamNameS = "s";

        private const int DefaultYear = 2017;
        private const int DefaultMonth = 7;
        private const int DefaultDay = 1;
        private const int DefaultHour = 10;
        private const int DefaultMinute = 5;
        private const int DefaultSecond = 55;
        private const decimal DefaultFraction = 0.9931094m;
        private static readonly TimeSpan DefaultUtcOffset = TimeSpan.FromMinutes(60);

        private PartialDateTimeBuilder _builder = new PartialDateTimeBuilder();

        private CultureInfo _originalCulture;

        public PartialDateTimeTests()
        {
            _originalCulture = Thread.CurrentThread.CurrentCulture;
        }

        public static IEnumerable<object[]> GetParameterPreviousParamNullData()
        {
            yield return new object[] { ParamNameDay, null, 15, 20, 30, 30, 0.500230m, 60, ParamNameMonth }; // Day cannot be specified if month is not specified.
            yield return new object[] { ParamNameHour, null, null, 10, 15, 0, 0.10023m, 0, ParamNameMonth }; // Hour cannot be specified if month is not specified.
            yield return new object[] { ParamNameHour, 1, null, 10, 15, 0, 0.10023m, 0, ParamNameDay }; // Hour cannot be specified if day is not specified.
            yield return new object[] { ParamNameMinute, null, null, null, 10, 15, 0.999234m, 30, ParamNameMonth }; // Minute cannot be specified if month is not specified.
            yield return new object[] { ParamNameMinute, 2, null, null, 10, 15, 0.999234m, 30, ParamNameDay }; // Minute cannot be specified if day is not specified.
            yield return new object[] { ParamNameMinute, 2, 5, null, 10, 15, 0.999234m, 30, ParamNameHour }; // Minute cannot be specified if hour is not specified.
            yield return new object[] { ParamNameSecond, null, null, null, null, 10, 0.20035m, 720, ParamNameMonth }; // Second cannot be specified if month is not specified.
            yield return new object[] { ParamNameSecond, 5, null, null, null, 10, 0.20035m, 720, ParamNameDay }; // Second cannot be specified if day is not specified.
            yield return new object[] { ParamNameSecond, 5, 3, null, null, 10, 0.20035m, 720, ParamNameHour }; // Second cannot be specified if hour is not specified.
            yield return new object[] { ParamNameSecond, 5, 3, 23, null, 10, 0.20035m, 720, ParamNameMinute }; // Second cannot be specified if minute is not specified.
            yield return new object[] { ParamNameFraction, null, null, null, null, null, 0.20035m, 720, ParamNameMonth }; // Fraction cannot be specified if month is not specified.
            yield return new object[] { ParamNameFraction, 5, null, null, null, null, 0.20035m, 720, ParamNameDay }; // Fraction cannot be specified if day is not specified.
            yield return new object[] { ParamNameFraction, 5, 3, null, null, null, 0.20035m, 720, ParamNameHour }; // Fraction cannot be specified if hour is not specified.
            yield return new object[] { ParamNameFraction, 5, 3, 23, null, null, 0.20035m, 720, ParamNameMinute }; // Fraction cannot be specified if minute is not specified.
            yield return new object[] { ParamNameFraction, 5, 3, 23, 20, null, 0.20035m, 720, ParamNameSecond }; // Fraction cannot be specified if second is not specified.
        }

        [Theory]
        [MemberData(nameof(GetParameterPreviousParamNullData))]
        public void GivenPreviousParamIsNull_WhenInitializing_ThenExceptionShouldBeThrown(
            string paramName,
            int? month,
            int? day,
            int? hour,
            int? minute,
            int? second,
            decimal? fraction,
            int? utcOffsetInMinutes,
            string firstNullParamName)
        {
            _builder.Month = month;
            _builder.Day = day;
            _builder.Hour = hour;
            _builder.Minute = minute;
            _builder.Second = second;
            _builder.Fraction = fraction;
            _builder.UtcOffset = utcOffsetInMinutes == null ?
                (TimeSpan?)null :
                TimeSpan.FromMinutes(utcOffsetInMinutes.Value);

            Exception ex = Assert.Throws<ArgumentException>(paramName, () => _builder.ToPartialDateTime());

            string expectedMessage = $"The {paramName} portion of a date cannot be specified if the {firstNullParamName} portion is not specified.{Environment.NewLine}Parameter name: {paramName}";

            Assert.Equal(expectedMessage, ex.Message);
        }

        public static IEnumerable<object[]> GetParameterInvalidData()
        {
            yield return new object[] { ParamNameMinute, 10, 30, 23, null, null, null, 60 }; // Minute must be specified if Hour is specified.
            yield return new object[] { ParamNameUtcOffset, 3, 5, 17, 30, 15, 0.400123m, null }; // UtcOffset must be specified if Hour and Minutes are specified.
        }

        [Theory]
        [MemberData(nameof(GetParameterInvalidData))]
        public void GivenAnInvalidParameter_WhenInitializing_ThenExceptionShouldBeThrown(
            string paramName,
            int? month,
            int? day,
            int? hour,
            int? minute,
            int? second,
            decimal? fraction,
            int? utcOffsetInMinutes)
        {
            _builder.Month = month;
            _builder.Day = day;
            _builder.Hour = hour;
            _builder.Minute = minute;
            _builder.Second = second;
            _builder.Fraction = fraction;
            _builder.UtcOffset = utcOffsetInMinutes == null ?
                (TimeSpan?)null :
                TimeSpan.FromMinutes(utcOffsetInMinutes.Value);

            Assert.Throws<ArgumentException>(paramName, () => _builder.ToPartialDateTime());
        }

        public static IEnumerable<object[]> GetParameterOutOfRangeData()
        {
            yield return new object[] { ParamNameYear, 0, 1, 1, 1, 1, 1, 0m, 60 }; // Year cannot be less than 1.
            yield return new object[] { ParamNameYear, 10000, 1, 1, 1, 1, 1, 0m, 60 }; // Year cannot be greater than 9999.
            yield return new object[] { ParamNameMonth, 2017, 0, 1, 1, 1, 1, 0m, 60 }; // Month cannot be less than 1.
            yield return new object[] { ParamNameMonth, 2017, 13, 1, 1, 1, 1, 0m, 60 }; // Month cannot be greater than 12.
            yield return new object[] { ParamNameDay, 2017, 1, 0, 1, 1, 1, 0m, 60 }; // Day cannot be less than 1.
            yield return new object[] { ParamNameDay, 2017, 1, 32, 1, 1, 1, 0m, 60 }; // Day cannot be greater 31 in January.
            yield return new object[] { ParamNameDay, 2001, 2, 29, 1, 1, 1, 0m, 60 }; // Day cannot be greater than 28 in non-leap year.
            yield return new object[] { ParamNameDay, 2000, 2, 30, 1, 1, 1, 0m, 60 }; // Day cannot be greater than 29 in leap year.
            yield return new object[] { ParamNameHour, 2017, 1, 1, -1, 1, 1, 0m, 60 }; // Hour cannot be less than 0.
            yield return new object[] { ParamNameHour, 2017, 1, 1, 24, 1, 1, 0m, 60 }; // Hour cannot be greater than 23.
            yield return new object[] { ParamNameMinute, 2017, 1, 1, 1, -1, 1, 0m, 60 }; // Minute cannot be less than 0.
            yield return new object[] { ParamNameMinute, 2017, 1, 1, 1, 61, 1, 0m, 60 }; // Minute cannot be greater than 59.
            yield return new object[] { ParamNameSecond, 2017, 1, 1, 1, 1, -1, 0m, 60 }; // Second cannot be less than 0.
            yield return new object[] { ParamNameSecond, 2017, 1, 1, 1, 1, 61, 0m, 60 }; // Second cannot be greater than 59.
            yield return new object[] { ParamNameFraction, 2017, 1, 1, 1, 1, 1, -1m, 60 }; // Fraction cannot be less than 0.
            yield return new object[] { ParamNameFraction, 2017, 1, 1, 1, 1, 1, 1m, 60 }; // Fraction cannot be greater than .
        }

        [Theory]
        [MemberData(nameof(GetParameterOutOfRangeData))]
        public void GivenAOutOfRangeParameter_WhenInitializing_ThenExceptionShouldBeThrown(
            string paramName,
            int year,
            int month,
            int day,
            int hour,
            int minute,
            int second,
            decimal? fraction,
            int utcOffsetInMinutes)
        {
            _builder.Year = year;
            _builder.Month = month;
            _builder.Day = day;
            _builder.Hour = hour;
            _builder.Minute = minute;
            _builder.Second = second;
            _builder.Fraction = fraction;
            _builder.UtcOffset = TimeSpan.FromMinutes(utcOffsetInMinutes);

            Assert.Throws<ArgumentOutOfRangeException>(paramName, () => _builder.ToPartialDateTime());
        }

        public static IEnumerable<object[]> GetParameterNullData()
        {
            yield return new object[] { null, null, null, null, null, null, null };
            yield return new object[] { 1, null, null, null, null, null, null };
            yield return new object[] { 3, 5, null, null, null, null, null };
            yield return new object[] { 6, 1, 12, 35, null, null, 60 };
            yield return new object[] { 12, 2, 23, 8, 24, null, -150 };
            yield return new object[] { 1, 3, 5, 2, 15, 0.9991532m, 30 };
        }

        [Theory]
        [MemberData(nameof(GetParameterNullData))]
        public void GivenANullParameter_WhenInitialized_ThenCorrectPartialDateTimeShouldBeCreated(
            int? month,
            int? day,
            int? hour,
            int? minute,
            int? second,
            decimal? fraction,
            int? utcOffsetInMinutes)
        {
            _builder.Month = month;
            _builder.Day = day;
            _builder.Hour = hour;
            _builder.Minute = minute;
            _builder.Second = second;
            _builder.Fraction = fraction;

            TimeSpan? utcOffset = utcOffsetInMinutes == null ?
                (TimeSpan?)null :
                TimeSpan.FromMinutes(utcOffsetInMinutes.Value);

            _builder.UtcOffset = utcOffset;

            PartialDateTime dateTime = _builder.ToPartialDateTime();

            Assert.Equal(DefaultYear, dateTime.Year);
            Assert.Equal(month, dateTime.Month);
            Assert.Equal(day, dateTime.Day);
            Assert.Equal(hour, dateTime.Hour);
            Assert.Equal(minute, dateTime.Minute);
            Assert.Equal(second, dateTime.Second);
            Assert.Equal(fraction, dateTime.Fraction);
            Assert.Equal(utcOffset, dateTime.UtcOffset);
        }

        [Fact]
        public void GivenADateImtOffset_WhenInitialized_ThenCorrectPartialDateTimeShouldBeCreated()
        {
            const int year = 2018;
            const int month = 5;
            const int day = 20;
            const int hour = 21;
            const int minute = 23;
            const int second = 59;
            const int millisecond = 153;
            const int fraction = 1234;
            TimeSpan utcOffset = TimeSpan.FromMinutes(240);

            var dateTimeOffset = new DateTimeOffset(
                year,
                month,
                day,
                hour,
                minute,
                second,
                millisecond,
                utcOffset).AddTicks(fraction);

            PartialDateTime partialDateTime = new PartialDateTime(dateTimeOffset);

            Assert.Equal(year, partialDateTime.Year);
            Assert.Equal(month, partialDateTime.Month);
            Assert.Equal(day, partialDateTime.Day);
            Assert.Equal(hour, partialDateTime.Hour);
            Assert.Equal(minute, partialDateTime.Minute);
            Assert.Equal(second, partialDateTime.Second);
            Assert.Equal(0.1531234m, partialDateTime.Fraction);
            Assert.Equal(utcOffset, partialDateTime.UtcOffset);
        }

        [Fact]
        public void GivenANullString_WhenParsing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameS, () => PartialDateTime.Parse(null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenAnInvalidString_WhenParsing_ThenExceptionShouldBeThrown(string s)
        {
            Assert.Throws<ArgumentException>(ParamNameS, () => PartialDateTime.Parse(s));
        }

        [Fact]
        public void GivenAnInvalidFormatString_WhenParsing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<FormatException>(() => PartialDateTime.Parse("abc"));
        }

        public static IEnumerable<object[]> GetParseData()
        {
            yield return new object[] { "2017", 2017, null, null, null, null, null, null, null };
            yield return new object[] { "2017-01", 2017, 1, null, null, null, null, null, null };
            yield return new object[] { "2019-01-02", 2019, 1, 2, null, null, null, null, null };
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
            string s,
            int year,
            int? month,
            int? day,
            int? hour,
            int? minute,
            int? second,
            decimal? fraction,
            int? utcOffsetInMinute)
        {
            PartialDateTime dateTime = PartialDateTime.Parse(s);

            Assert.NotNull(dateTime);
            Assert.Equal(year, dateTime.Year);
            Assert.Equal(month, dateTime.Month);
            Assert.Equal(day, dateTime.Day);
            Assert.Equal(hour, dateTime.Hour);
            Assert.Equal(minute, dateTime.Minute);
            Assert.Equal(second, dateTime.Second);
            Assert.Equal(fraction, dateTime.Fraction);

            TimeSpan? utcOffset = null;

            if (utcOffsetInMinute != null)
            {
                utcOffset = TimeSpan.FromMinutes(utcOffsetInMinute.Value);
            }

            Assert.Equal(utcOffset, dateTime.UtcOffset);
        }

        [Fact]
        public void GivenAPartialDateTimeWithNoMissingComponent_WhenToDateTimeOffsetIsCalled_ThenCorrectDateTimeOffsetIsReturned()
        {
            PartialDateTime dateTime = _builder.ToPartialDateTime();

            DateTimeOffset actualOffset = dateTime.ToDateTimeOffset(
                2,
                (year, month) => 10,
                15,
                13,
                12,
                0.300250m,
                TimeSpan.FromMinutes(30));

            DateTimeOffset expectedOffset = new DateTimeOffset(
                DefaultYear,
                DefaultMonth,
                DefaultDay,
                DefaultHour,
                DefaultMinute,
                DefaultSecond,
                DefaultUtcOffset);

            expectedOffset = expectedOffset.AddTicks((long)(DefaultFraction * TimeSpan.TicksPerSecond));

            Assert.Equal(expectedOffset, actualOffset);
        }

        [Fact]
        public void GivenAPartialDateTimeWithMissingComponents_WhenToDateTimeOffsetIsCalled_ThenCorrectDateTimeOffsetIsReturned()
        {
            int expectedMonth = 2;
            int expectedDay = 10;
            int expectedHour = 15;
            int expectedMinute = 13;
            int expectedSecond = 12;
            decimal expectedFraction = 0.3009953m;
            TimeSpan expectedUtcOffset = TimeSpan.FromMinutes(30);

            _builder.Month = null;
            _builder.Day = null;
            _builder.Hour = null;
            _builder.Minute = null;
            _builder.Second = null;
            _builder.Fraction = null;
            _builder.UtcOffset = null;

            PartialDateTime dateTime = _builder.ToPartialDateTime();

            DateTimeOffset actualOffset = dateTime.ToDateTimeOffset(
                expectedMonth,
                (year, month) => expectedDay,
                expectedHour,
                expectedMinute,
                expectedSecond,
                expectedFraction,
                expectedUtcOffset);

            DateTimeOffset expectedOffset = new DateTimeOffset(
                DefaultYear,
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
            PartialDateTime dateTime = PartialDateTime.Parse(input);

            Assert.NotNull(dateTime);
            Assert.Equal(expected, dateTime.ToString());
        }

        [Theory]
        [InlineData("de-DE", "2018-11-29T18:30:27.911+01:00", "2018-11-29T18:30:27,9110000+01:00")]
        [InlineData("en-GB", "2018-11-29T18:30:27.911+01:00", "2018-11-29T18:30:27.9110000+01:00")]
        [InlineData("en-US", "2018-11-29T18:30:27.911+01:00", "2018-11-29T18:30:27.9110000+01:00")]
        public void GivenACulture_WhenToStringisCalled_ThenCorrectStringShouldBeReturned(string culture, string input, string expected)
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(culture);
            PartialDateTime dateTime = PartialDateTime.Parse(input);

            Assert.NotNull(dateTime);
            Assert.Equal(expected, dateTime.ToString());
        }

        public void Dispose()
        {
            Thread.CurrentThread.CurrentCulture = _originalCulture;
        }

        private class PartialDateTimeBuilder
        {
            public PartialDateTimeBuilder()
            {
                Year = DefaultYear;
                Month = DefaultMonth;
                Day = DefaultDay;
                Hour = DefaultHour;
                Minute = DefaultMinute;
                Second = DefaultSecond;
                Fraction = DefaultFraction;
                UtcOffset = DefaultUtcOffset;
            }

            public int Year { get; set; }

            public int? Month { get; set; }

            public int? Day { get; set; }

            public int? Hour { get; set; }

            public int? Minute { get; set; }

            public int? Second { get; set; }

            public decimal? Fraction { get; set; }

            public TimeSpan? UtcOffset { get; set; }

            public PartialDateTime ToPartialDateTime()
            {
                return new PartialDateTime(
                    Year,
                    Month,
                    Day,
                    Hour,
                    Minute,
                    Second,
                    Fraction,
                    UtcOffset);
            }
        }
    }
}
