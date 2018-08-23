// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Legacy.SearchValues
{
    public class DateTimeExtractorTests
    {
        private const string DefaultDateTime = "2017-04-13";

        public static readonly DateTimeOffset ExpectedDateTimeStart = new DateTimeOffset(2017, 4, 13, 0, 0, 0, TimeSpan.Zero);
        public static readonly DateTimeOffset ExpectedDateTimeEnd = new DateTimeOffset(2017, 4, 13, 23, 59, 59, 999, TimeSpan.Zero).AddTicks(9999);

        private const string DefaultPeriodStart = "2017-04-13";
        private const string DefaultPeriodEnd = "2017-05-02";

        public static readonly DateTimeOffset ExpectedPeriodStart = new DateTimeOffset(2017, 4, 13, 0, 0, 0, TimeSpan.Zero);
        public static readonly DateTimeOffset ExpectedPeriodEnd = new DateTimeOffset(2017, 5, 2, 23, 59, 59, 999, TimeSpan.Zero).AddTicks(9999);
        public static readonly DateTimeOffset ExpectedPeriodStartMin = new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public static readonly DateTimeOffset ExpectedPeriodEndMax = new DateTimeOffset(9999, 12, 31, 23, 59, 59, 999, TimeSpan.Zero).AddTicks(9999);

        private readonly TestResource _testResource = new TestResource();

        [Fact]
        public void GivenANullDateTimes_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => (IEnumerable<string>)null,
                s => s,
                e => e);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenAnEmptyDateTimes_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => new string[0],
                s => s,
                e => e);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenADateTime_WhenExtracting_ThenOneResultShouldBeReturned()
        {
            var extractor = Create(
                r => new[] { DefaultDateTime },
                s => s,
                e => e);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateStartAndEndDateTime(r));
        }

        [Fact]
        public void GivenMultipleDateTime_WhenExtracting_ThenMultipleResultsShouldBeReturned()
        {
            var extractor = Create(
                r => new[] { DefaultDateTime, DefaultDateTime },
                s => s,
                e => e);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateStartAndEndDateTime(r),
                r => ValidateStartAndEndDateTime(r));
        }

        [Fact]
        public void GivenANullDateTime_WhenExtracting_ThenItShouldBeRemoved()
        {
            var extractor = Create(
                r => new[] { DefaultDateTime, null, DefaultDateTime },
                s => s,
                e => e);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateStartAndEndDateTime(r),
                r => ValidateStartAndEndDateTime(r));
        }

        [Fact]
        public void GivenAnEmptyPeriod_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => new[] { new Period() },
                s => s.Start,
                e => e.End);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenAPeriod_WhenExtracting_ThenOneResultShouldBeReturned()
        {
            var extractor = Create(
                r => new[] { new Period(new FhirDateTime(DefaultPeriodStart), new FhirDateTime(DefaultPeriodEnd)) },
                s => s.Start,
                e => e.End);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidatePeriodStartAndEndDateTime(r, ExpectedPeriodStart, ExpectedPeriodEnd));
        }

        [Fact]
        public void GivenAPeriodWithNoStart_WhenExtracting_ThenOneResultShouldBeReturned()
        {
            var extractor = Create(
                r => new[] { new Period(null, new FhirDateTime(DefaultPeriodEnd)) },
                s => s.Start,
                e => e.End);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidatePeriodStartAndEndDateTime(r, ExpectedPeriodStartMin, ExpectedPeriodEnd));
        }

        [Fact]
        public void GivenAPeriodWithNoEnd_WhenExtracting_ThenOneResultShouldBeReturned()
        {
            var extractor = Create(
                r => new[] { new Period(new FhirDateTime(DefaultPeriodStart), null) },
                s => s.Start,
                e => e.End);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidatePeriodStartAndEndDateTime(r, ExpectedPeriodStart, ExpectedPeriodEndMax));
        }

        [Fact]
        public void GivenMultiplePeriods_WhenExtracting_ThenMultipleResultsShouldBeReturned()
        {
            var extractor = Create(
                r => new[]
                {
                    new Period { Start = DefaultPeriodStart },
                    new Period(new FhirDateTime(DefaultPeriodStart), new FhirDateTime(DefaultPeriodEnd)),
                    new Period { End = DefaultPeriodEnd },
                },
                s => s.Start,
                e => e.End);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidatePeriodStartAndEndDateTime(r, ExpectedPeriodStart, ExpectedPeriodEndMax),
                r => ValidatePeriodStartAndEndDateTime(r, ExpectedPeriodStart, ExpectedPeriodEnd),
                r => ValidatePeriodStartAndEndDateTime(r, ExpectedPeriodStartMin, ExpectedPeriodEnd));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenANonDatePortionPeriod_WhenExtracting_ThenItShouldBeSetToDefault(string testValue)
        {
            var extractor = Create(
                r => new[]
                {
                    new Period { StartElement = new FhirDateTime(DefaultPeriodStart), End = testValue },
                    new Period { Start = testValue, EndElement = new FhirDateTime(DefaultPeriodEnd) },
                },
                s => s.Start,
                e => e.End);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidatePeriodStartAndEndDateTime(r, ExpectedPeriodStart, ExpectedPeriodEndMax),
                r => ValidatePeriodStartAndEndDateTime(r, ExpectedPeriodStartMin, ExpectedPeriodEnd));
        }

        [Fact]
        public void GivenANullPeriod_WhenExtracting_ThenItShouldBeRemoved()
        {
            var extractor = Create(
                r => new[]
                {
                    new Period(new FhirDateTime(DefaultPeriodStart), null),
                    null,
                    new Period(null, new FhirDateTime(DefaultPeriodEnd)),
                },
                s => s.Start,
                e => e.End);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidatePeriodStartAndEndDateTime(r, ExpectedPeriodStart, ExpectedPeriodEndMax),
                r => ValidatePeriodStartAndEndDateTime(r, ExpectedPeriodStartMin, ExpectedPeriodEnd));
        }

        private DateTimeExtractor<TestResource, TCollection> Create<TCollection>(
            Func<TestResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, string> dateTimeStartSelector,
            Func<TCollection, string> dateTimeEndSelector)
        {
            return new DateTimeExtractor<TestResource, TCollection>(
                collectionSelector,
                dateTimeStartSelector,
                dateTimeEndSelector);
        }

        private void ValidateStartAndEndDateTime(ISearchValue searchValue)
        {
            DateTimeSearchValue dtsv = Assert.IsType<DateTimeSearchValue>(searchValue);

            Assert.Equal(ExpectedDateTimeStart, dtsv.Start);
            Assert.Equal(ExpectedDateTimeEnd, dtsv.End);
        }

        private void ValidatePeriodStartAndEndDateTime(ISearchValue searchValue, DateTimeOffset expectedStart, DateTimeOffset expectedEnd)
        {
            DateTimeSearchValue dtsv = Assert.IsType<DateTimeSearchValue>(searchValue);

            Assert.Equal(expectedStart, dtsv.Start);
            Assert.Equal(expectedEnd, dtsv.End);
        }
    }
}
