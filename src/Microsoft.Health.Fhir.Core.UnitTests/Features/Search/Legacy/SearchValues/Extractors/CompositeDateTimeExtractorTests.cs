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
    public class CompositeDateTimeExtractorTests
    {
        private readonly TestResource _testResource = new TestResource();

        [Fact]
        public void GivenANullCompositeToken_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => null,
                r => r.FhirDateTime);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenAnEmptyCompositeToken_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => new CodeableConcept(),
                r => r.FhirDateTime);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenSingleCompositeToken_WhenExtracting_ThenOneResultShouldBeReturned()
        {
            var extractor = Create(
                r => r.SingleCodeableConcept,
                r => r.FhirDateTime);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ExtractorTestHelper.ValidateComposite<DateTimeSearchValue>(
                    TestResource.ExpectedSystem1,
                    TestResource.ExpectedCode1,
                    r,
                    dtsv => ValidateStartAndEndDateTime(dtsv)));
        }

        [Fact]
        public void GivenMultipleCompositeToken_WhenExtracting_ThenTwoResultsShouldBeReturned()
        {
            var extractor = Create(
                r => r.MultipleCodeableConcept,
                r => r.FhirDateTime);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ExtractorTestHelper.ValidateComposite<DateTimeSearchValue>(
                    TestResource.ExpectedSystem2,
                    TestResource.ExpectedCode2,
                    r,
                    dtsv => ValidateStartAndEndDateTime(dtsv)),
                r => ExtractorTestHelper.ValidateComposite<DateTimeSearchValue>(
                    TestResource.ExpectedSystem3,
                    TestResource.ExpectedCode3,
                    r,
                    dtsv => ValidateStartAndEndDateTime(dtsv)));
        }

        [Fact]
        public void GivenANullDateTime_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => r.MultipleCodeableConcept,
                r => null);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        private CompositeDateTimeExtractor<TestResource> Create(
            Func<TestResource, CodeableConcept> compositeTokenSelector,
            Func<TestResource, FhirDateTime> dateTimeSelector)
        {
            return new CompositeDateTimeExtractor<TestResource>(
                compositeTokenSelector,
                dateTimeSelector);
        }

        private void ValidateStartAndEndDateTime(DateTimeSearchValue dtsv)
        {
            Assert.Equal(TestResource.ExpectedDateTimeStart, dtsv.Start);
            Assert.Equal(TestResource.ExpectedDateTimeEnd, dtsv.End);
        }
    }
}
