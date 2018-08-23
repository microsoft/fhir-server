// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Legacy.SearchValues
{
    public class CompositeTokenExtractorTests
    {
        private readonly TestResource _testResource = new TestResource();

        [Fact]
        public void GivenANullCollection_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => (IEnumerable<TestResource>)null,
                r => r.SingleCodeableConcept,
                r => r.SingleCodeableConcept);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenANullCompositeToken_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(_testResource, 1),
                r => null,
                r => r.SingleCodeableConcept);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenANullToken_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 1),
                r => r.SingleCodeableConcept,
                r => null);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenAnEmptyCompositeToken_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(_testResource, 1),
                r => new CodeableConcept(),
                r => r.MultipleCodeableConcept);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenAnEmptyToken_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(_testResource, 1),
                r => r.MultipleCodeableConcept,
                r => new CodeableConcept());

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenASingleCollectionWithSingleCompositeAndSingleToken_WhenExtracting_ThenOneResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(_testResource, 1),
                r => r.SingleCodeableConcept,
                r => r.SingleCodeableConcept);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateCombo(
                    TestResource.ExpectedSystem1,
                    TestResource.ExpectedCode1,
                    TestResource.ExpectedSystem1,
                    TestResource.ExpectedCode1,
                    r));
        }

        [Fact]
        public void GivenASingleCollectionWithSingleCompositeAndMultiToken_WhenExtracting_ThenTwoResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(_testResource, 1),
                r => r.SingleCodeableConcept,
                r => r.MultipleCodeableConcept);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateCombo(
                    TestResource.ExpectedSystem1,
                    TestResource.ExpectedCode1,
                    TestResource.ExpectedSystem2,
                    TestResource.ExpectedCode2,
                    r),
                r => ValidateCombo(
                    TestResource.ExpectedSystem1,
                    TestResource.ExpectedCode1,
                    TestResource.ExpectedSystem3,
                    TestResource.ExpectedCode3,
                    r));
        }

        [Fact]
        public void GivenASingleCollectionWithMultipleCompositeAndSingleToken_WhenExtracting_ThenTwoResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(_testResource, 1),
                r => r.MultipleCodeableConcept,
                r => r.SingleCodeableConcept);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateCombo(
                    TestResource.ExpectedSystem2,
                    TestResource.ExpectedCode2,
                    TestResource.ExpectedSystem1,
                    TestResource.ExpectedCode1,
                    r),
                r => ValidateCombo(
                    TestResource.ExpectedSystem3,
                    TestResource.ExpectedCode3,
                    TestResource.ExpectedSystem1,
                    TestResource.ExpectedCode1,
                    r));
        }

        [Fact]
        public void GivenASingleCollectionWithMultipleCompositeAndMultiToken_WhenExtracting_ThenTwoResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(_testResource, 1),
                r => r.MultipleCodeableConcept,
                r => r.MultipleCodeableConcept);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateCombo(
                    TestResource.ExpectedSystem2,
                    TestResource.ExpectedCode2,
                    TestResource.ExpectedSystem2,
                    TestResource.ExpectedCode2,
                    r),
                r => ValidateCombo(
                    TestResource.ExpectedSystem2,
                    TestResource.ExpectedCode2,
                    TestResource.ExpectedSystem3,
                    TestResource.ExpectedCode3,
                    r),
                r => ValidateCombo(
                    TestResource.ExpectedSystem3,
                    TestResource.ExpectedCode3,
                    TestResource.ExpectedSystem2,
                    TestResource.ExpectedCode2,
                    r),
                r => ValidateCombo(
                    TestResource.ExpectedSystem3,
                    TestResource.ExpectedCode3,
                    TestResource.ExpectedSystem3,
                    TestResource.ExpectedCode3,
                    r));
        }

        [Fact]
        public void GivenAMultipleCollectionWithMultipleCompositeAndMultiToken_WhenExtracting_ThenTwoResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(_testResource, 2),
                r => r.MultipleCodeableConcept,
                r => r.MultipleCodeableConcept);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateCombo(
                    TestResource.ExpectedSystem2,
                    TestResource.ExpectedCode2,
                    TestResource.ExpectedSystem2,
                    TestResource.ExpectedCode2,
                    r),
                r => ValidateCombo(
                    TestResource.ExpectedSystem2,
                    TestResource.ExpectedCode2,
                    TestResource.ExpectedSystem3,
                    TestResource.ExpectedCode3,
                    r),
                r => ValidateCombo(
                    TestResource.ExpectedSystem3,
                    TestResource.ExpectedCode3,
                    TestResource.ExpectedSystem2,
                    TestResource.ExpectedCode2,
                    r),
                r => ValidateCombo(
                    TestResource.ExpectedSystem3,
                    TestResource.ExpectedCode3,
                    TestResource.ExpectedSystem3,
                    TestResource.ExpectedCode3,
                    r),
                r => ValidateCombo(
                    TestResource.ExpectedSystem2,
                    TestResource.ExpectedCode2,
                    TestResource.ExpectedSystem2,
                    TestResource.ExpectedCode2,
                    r),
                r => ValidateCombo(
                    TestResource.ExpectedSystem2,
                    TestResource.ExpectedCode2,
                    TestResource.ExpectedSystem3,
                    TestResource.ExpectedCode3,
                    r),
                r => ValidateCombo(
                    TestResource.ExpectedSystem3,
                    TestResource.ExpectedCode3,
                    TestResource.ExpectedSystem2,
                    TestResource.ExpectedCode2,
                    r),
                r => ValidateCombo(
                    TestResource.ExpectedSystem3,
                    TestResource.ExpectedCode3,
                    TestResource.ExpectedSystem3,
                    TestResource.ExpectedCode3,
                    r));
        }

        private CompositeTokenExtractor<TestResource, TCollection> Create<TCollection>(
            Func<TestResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, CodeableConcept> compositeTokenSelector,
            Func<TCollection, CodeableConcept> tokenSelector)
        {
            return new CompositeTokenExtractor<TestResource, TCollection>(
                collectionSelector,
                compositeTokenSelector,
                tokenSelector);
        }

        private void ValidateCombo(
            string expectedCompositeSystem,
            string expectedCompositeCode,
            string expectedTokenSystem,
            string expectedTokenCode,
            ISearchValue searchValue)
        {
            ExtractorTestHelper.ValidateComposite<TokenSearchValue>(
                expectedCompositeSystem,
                expectedCompositeCode,
                searchValue,
                tsv => ValidateToken(
                    expectedTokenSystem,
                    expectedTokenCode,
                    tsv));
        }

        private void ValidateToken(string expectedSystem, string expectedCode, TokenSearchValue tsv)
        {
            Assert.Equal(expectedSystem, tsv.System);
            Assert.Equal(expectedCode, tsv.Code);
        }
    }
}
