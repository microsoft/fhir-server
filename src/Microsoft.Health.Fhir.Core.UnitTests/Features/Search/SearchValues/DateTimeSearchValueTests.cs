// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchValues
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class DateTimeSearchValueTests
    {
        private const string ParamNameStartDateTime = "startDateTime";
        private const string ParamNameS = "s";

        public static IEnumerable<object[]> GetSingleDateTimeData()
        {
            yield return new object[] { "2017", "2017-01-01T00:00:00Z", "2017-12-31T23:59:59.9999999Z" }; // Year only.
            yield return new object[] { "2018-05", "2018-05-01T00:00:00Z", "2018-05-31T23:59:59.9999999Z" }; // Year and month.
            yield return new object[] { "2013-02", "2013-02-01T00:00:00Z", "2013-02-28T23:59:59.9999999Z" }; // Year and month (non-leap year).
            yield return new object[] { "2012-02", "2012-02-01T00:00:00Z", "2012-02-29T23:59:59.9999999Z" }; // Year and month (leap year).
            yield return new object[] { "2015-07-01", "2015-07-01T00:00:00Z", "2015-07-01T23:59:59.9999999Z" }; // Year, month, and day.
            yield return new object[] { "2013-03-12T12:30", "2013-03-12T12:30:00Z", "2013-03-12T12:30:59.9999999Z" }; // Year, month, day, hour and second.
            yield return new object[] { "2013-03-12T12:30:35", "2013-03-12T12:30:35Z", "2013-03-12T12:30:35.9999999Z" }; // Year, month, day, hour and second.
            yield return new object[] { "2014-03-29T15:33:59.495+10:00", "2014-03-29T15:33:59.495+10:00", "2014-03-29T15:33:59.495+10:00" }; // Everything.
        }

        public static IEnumerable<object[]> GetStartDateTimeData()
        {
            // This data set is also used in combination with the end date time data below.
            // Make sure the start time of the corresponding row is earlier than the end time.
            yield return new object[] { "2017", "2017-01-01T00:00:00.000Z" }; // Year only.
            yield return new object[] { "2018-05", "2018-05-01T00:00:00.000Z" }; // Year and month.
            yield return new object[] { "2013-02", "2013-02-01T00:00:00.000Z" }; // Year and month (non-leap year).
            yield return new object[] { "2012-02", "2012-02-01T00:00:00.000Z" }; // Year and month (leap year).
            yield return new object[] { "2015-07-01", "2015-07-01T00:00:00.000Z" }; // Year, month, and day.
            yield return new object[] { "2013-03-12T12:30", "2013-03-12T12:30:00Z", }; // Year, month, day, hour and second.
            yield return new object[] { "2013-03-12T12:30:35", "2013-03-12T12:30:35Z" }; // Year, month, day, hour and second.
            yield return new object[] { "2014-03-29T15:33:59.495+10:00", "2014-03-29T15:33:59.495+10:00" }; // Everything.
        }

        public static IEnumerable<object[]> GetEndDateTimeData()
        {
            // This data set is also used in combination with the start date time data above.
            // Make sure the end time of the corresponding row is later than the start time.
            yield return new object[] { "2019", "2019-12-31T23:59:59.9999999Z" }; // Year only.
            yield return new object[] { "2030-05", "2030-05-31T23:59:59.9999999Z" }; // Year and month.
            yield return new object[] { "2022-02", "2022-02-28T23:59:59.9999999Z" }; // Year and month (non-leap year).
            yield return new object[] { "2016-02", "2016-02-29T23:59:59.9999999Z" }; // Year and month (leap year).
            yield return new object[] { "2017-01-01", "2017-01-01T23:59:59.9999999Z" }; // Year, month, and day.
            yield return new object[] { "2013-03-12T12:30", "2013-03-12T12:30:59.9999999Z" }; // Year, month, day, hour and second.
            yield return new object[] { "2013-03-12T12:30:35", "2013-03-12T12:30:35.9999999Z" }; // Year, month, day, hour and second.
            yield return new object[] { "2018-02-12T13:22:01.000+05:00", "2018-02-12T13:22:01.000+05:00" }; // Everything.
        }

        public static IEnumerable<object[]> GetStartDateTimeAndEndDateTimeData()
        {
            // Produce an object with start and end combined.
            // (e.g., "2017", "2019", "2017-01-01T00:00:00.000Z", "2019-12-31T23:59:59.999Z")
            return GetStartDateTimeData().Zip(GetEndDateTimeData(), (first, second) => new[] { first[0], second[0], first[1], second[1] });
        }

        [Theory]
        [MemberData(nameof(GetSingleDateTimeData))]
        public void GivenAPartialDateTime_WhenInitialized_ThenCorrectStartDateTimeAndEndDateTimeShouldBeAssigned(string input, string start, string end)
        {
            PartialDateTime inputDateTime = PartialDateTime.Parse(input);
            DateTimeOffset expectedStartDateTime = DateTimeOffset.Parse(start);
            DateTimeOffset expectedEndDateTime = DateTimeOffset.Parse(end);

            DateTimeSearchValue value = new DateTimeSearchValue(inputDateTime);

            Assert.Equal(expectedStartDateTime, value.Start);
            Assert.Equal(expectedEndDateTime, value.End);
        }

        [Theory]
        [MemberData(nameof(GetStartDateTimeData))]
        public void GivenAPartialStartDateTime_WhenInitialized_ThenCorrectStartDateTimeShouldBeAssigned(string input, string start)
        {
            PartialDateTime inputDateTime = PartialDateTime.Parse(input);
            PartialDateTime endDateTime = PartialDateTime.Parse("2018");

            DateTimeOffset expectedStartDateTime = DateTimeOffset.Parse(start);

            DateTimeSearchValue value = new DateTimeSearchValue(inputDateTime, endDateTime);

            Assert.Equal(expectedStartDateTime, value.Start);
        }

        [Theory]
        [MemberData(nameof(GetEndDateTimeData))]
        public void GivenAPartialEndDateTime_WhenInitialized_ThenCorrectEndDateTimeShouldBeAssigned(string input, string start)
        {
            PartialDateTime inputDateTime = PartialDateTime.Parse(input);
            PartialDateTime startDateTime = PartialDateTime.Parse("2000");

            DateTimeOffset expectedEndDateTime = DateTimeOffset.Parse(start);

            DateTimeSearchValue value = new DateTimeSearchValue(startDateTime, inputDateTime);

            Assert.Equal(expectedEndDateTime, value.End);
        }

        [Theory]
        [InlineData("2017", "2017")]
        [InlineData("2017-01", "2017-01")]
        [InlineData("2017-01-10", "2017-01-10")]
        [InlineData("2016-05-03T12:34:20.594Z", "2016-05-03T12:34:20.594Z")]
        [InlineData("2017-12-31T23:58:23.493+00:00", "2017-12-31T23:58:23.493Z")]
        [InlineData("2017", "2018")]
        [InlineData("2016-01", "2017")]
        [InlineData("2016-12-31", "2017")]
        [InlineData("2016-05-03T12:34:20.594Z", "2017")]
        [InlineData("2017", "2017-01")]
        [InlineData("2017", "2017-01-10")]
        [InlineData("2016", "2016-05-03T12:34:20.594Z")]
        [InlineData("2017-12-31T23:58:23.493+01:00", "2017-12-31T23:58:23.493Z")]
        [InlineData("2017-12-31T23:58:23.493Z", "2017-12-31T23:58:23.493-01:00")]
        public void GiveAStartDateTimeThatIsEarlierThanOrEqualToEndDateTime_WhenInitialized_ThenNoExceptionShouldBeThrown(string start, string end)
        {
            PartialDateTime startDateTime = PartialDateTime.Parse(start);
            PartialDateTime endDateTime = PartialDateTime.Parse(end);

            new DateTimeSearchValue(startDateTime, endDateTime);
        }

        [Theory]
        [InlineData("2018", "2017")]
        [InlineData("2016-05-03T12:34:20.594Z", "2015")]
        [InlineData("2017", "2016-01")]
        [InlineData("2017", "2016-12-31")]
        [InlineData("2017", "2016-05-03T12:34:20.594Z")]
        [InlineData("2017-12-31T23:58:23.493-01:00", "2017-12-31T23:58:23.493Z")]
        [InlineData("2017-12-31T23:58:23.493Z", "2017-12-31T23:58:23.493+01:00")]
        public void GivenAStartDateTimeLaterThanEndDateTime_WhenInitializing_ThenNoExceptionShouldBeThrown(string start, string end)
        {
            PartialDateTime startDateTime = PartialDateTime.Parse(start);
            PartialDateTime endDateTime = PartialDateTime.Parse(end);

            new DateTimeSearchValue(startDateTime, endDateTime);
        }

        [Fact]
        public void GivenANullString_WhenParsing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameS, () => DateTimeSearchValue.Parse(null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenAnInvalidString_WhenParsing_ThenExceptionShouldBeThrown(string s)
        {
            Assert.Throws<ArgumentException>(ParamNameS, () => DateTimeSearchValue.Parse(s));
        }

        [Theory]
        [MemberData(nameof(GetSingleDateTimeData))]
        public void GivenAValidString_WhenParsed_ThenCorrectSearchValueShouldBeReturned(string input, string expectedStart, string expectedEnd)
        {
            DateTimeSearchValue value = DateTimeSearchValue.Parse(input);
            DateTimeOffset expectedStartDateTime = DateTimeOffset.Parse(expectedStart);
            DateTimeOffset expectedEndDateTime = DateTimeOffset.Parse(expectedEnd);

            Assert.NotNull(value);
            Assert.Equal(expectedStartDateTime, value.Start);
            Assert.Equal(expectedEndDateTime, value.End);
        }

        [Fact]
        public void GivenASearchValue_WhenIsValidCompositeComponentIsCalled_ThenTrueShouldBeReturned()
        {
            var value = new DateTimeSearchValue(DateTimeOffset.Now);

            Assert.True(value.IsValidAsCompositeComponent);
        }

        [Fact]
        public void GivenASearchValue_WhenToStringIsCalled_ThenCorrectStringShouldBeReturned()
        {
            DateTimeSearchValue value = DateTimeSearchValue.Parse("2017");

            Assert.NotNull(value);
            Assert.Equal("2017", value.ToString());
        }

        [Theory]
        [InlineData("2017", "2018", -1, -1)]
        [InlineData("2016-01", "2017", -1, -1)]
        [InlineData("2016-12-31", "2017", -1, -1)]
        [InlineData("2016-05-03T12:34:20.594Z", "2017", -1, -1)]
        [InlineData("2017", "2017-01", 0, 1)]
        [InlineData("2017", "2017-12", -1, 0)]
        [InlineData("2017", "2017-01-10", -1, 1)]
        [InlineData("2016", "2016-05-03T12:34:20.594Z", -1, 1)]
        [InlineData("2017-12-31T23:58:23.493+01:00", "2017-12-31T23:58:23.493Z", -1, -1)]
        [InlineData("2017-12-31T23:58:23.493Z", "2017-12-31T23:58:23.493-01:00", -1, -1)]
        [InlineData("2017-01-10", "2017", 1, -1)]
        [InlineData("2016-05-03T12:34:20.594Z", "2016", 1, -1)]
        [InlineData("2017-12-31T23:58:23.493Z", "2017-12-31T23:58:23.493+01:00", 1, 1)]
        [InlineData("2017-12-31T23:58:23.493-01:00", "2017-12-31T23:58:23.493Z", 1, 1)]
        public void GivenASearchValue_WhenCompareWithDateSearchValue_ThenCorrectResultIsReturned(string original, string given, int expectedMinResult, int expectedMaxResult)
        {
            DateTimeSearchValue originalValue = DateTimeSearchValue.Parse(original);
            DateTimeSearchValue givenValue = DateTimeSearchValue.Parse(given);

            int minResult = originalValue.CompareTo(givenValue, ComparisonRange.Min);
            int maxResult = originalValue.CompareTo(givenValue, ComparisonRange.Max);

            Assert.Equal(expectedMinResult, minResult);
            Assert.Equal(expectedMaxResult, maxResult);
        }

        [Fact]
        public void GivenAStringSearchValue_WhenCompareWithNull_ThenArgumentExceptionIsThrown()
        {
            DateTimeSearchValue value = DateTimeSearchValue.Parse("2020");

            Assert.Throws<ArgumentException>(() => value.CompareTo(null, ComparisonRange.Max));
        }
    }
}
