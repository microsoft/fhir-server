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
    public class CompositeStringExtractorTests
    {
        private readonly TestResource _testResource = new TestResource();

        [Fact]
        public void GivenANullCompositeToken_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => null,
                r => r.FhirString);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenANullString_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => r.SingleCodeableConcept,
                r => null);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenAnEmptyString_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => r.SingleCodeableConcept,
                r => new FhirString());

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenAnEmptyCompositeToken_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => new CodeableConcept(),
                r => r.FhirString);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenAString_WhenExtracting_ThenOneResultShouldBeReturned()
        {
            var extractor = Create(
                r => r.SingleCodeableConcept,
                r => r.FhirString);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ExtractorTestHelper.ValidateComposite<StringSearchValue>(
                    TestResource.ExpectedSystem1,
                    TestResource.ExpectedCode1,
                    r,
                    ssv => ValidateString(ssv)));
        }

        [Fact]
        public void GivenMultipleCompositeToken_WhenExtracting_ThenTwoResultsShouldBeReturned()
        {
            var extractor = Create(
                r => r.MultipleCodeableConcept,
                r => r.FhirString);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ExtractorTestHelper.ValidateComposite<StringSearchValue>(
                    TestResource.ExpectedSystem2,
                    TestResource.ExpectedCode2,
                    r,
                    ssv => ValidateString(ssv)),
                r => ExtractorTestHelper.ValidateComposite<StringSearchValue>(
                    TestResource.ExpectedSystem3,
                    TestResource.ExpectedCode3,
                    r,
                    ssv => ValidateString(ssv)));
        }

        private CompositeStringExtractor<TestResource> Create(
            Func<TestResource, CodeableConcept> compositeTokenSelector,
            Func<TestResource, FhirString> stringSelector)
        {
            return new CompositeStringExtractor<TestResource>(
                compositeTokenSelector,
                stringSelector);
        }

        private void ValidateString(StringSearchValue ssv)
        {
            Assert.Equal(TestResource.ExpectedString, ssv.String);
        }
    }
}
