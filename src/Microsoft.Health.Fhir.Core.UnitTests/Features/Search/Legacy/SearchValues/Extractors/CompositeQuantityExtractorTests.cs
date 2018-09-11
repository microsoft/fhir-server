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
    public class CompositeQuantityExtractorTests
    {
        private readonly TestResource _testResource = new TestResource();

        [Fact]
        public void GivenANullCollection_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => (IEnumerable<TestResource>)null,
                r => r.SingleCodeableConcept,
                r => r.Quantity);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenANullCompositeToken_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 1),
                r => null,
                r => r.Quantity);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenAnEmptyCompositeToken_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 1),
                r => new CodeableConcept(),
                r => r.Quantity);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenANullQuantity_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 1),
                r => r.MultipleCodeableConcept,
                r => null);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenAnEmptyQuantity_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 1),
                r => r.MultipleCodeableConcept,
                r => new Quantity());

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenSingleCompositeToken_WhenExtracting_ThenOneResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 1),
                r => r.SingleCodeableConcept,
                r => r.Quantity);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ExtractorTestHelper.ValidateComposite<QuantitySearchValue>(
                    TestResource.ExpectedSystem1,
                    TestResource.ExpectedCode1,
                    r,
                    qsv => ValidateQuantity(qsv)));
        }

        [Fact]
        public void GivenMultipleCompositeToken_WhenExtracting_ThenTwoResultsShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 1),
                r => r.MultipleCodeableConcept,
                r => r.Quantity);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ExtractorTestHelper.ValidateComposite<QuantitySearchValue>(
                    TestResource.ExpectedSystem2,
                    TestResource.ExpectedCode2,
                    r,
                    qsv => ValidateQuantity(qsv)),
                r => ExtractorTestHelper.ValidateComposite<QuantitySearchValue>(
                    TestResource.ExpectedSystem3,
                    TestResource.ExpectedCode3,
                    r,
                    qsv => ValidateQuantity(qsv)));
        }

        [Fact]
        public void GivenMultipleCollections_WhenExtracting_ThenMultipleResultsShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 2),
                r => r.MultipleCodeableConcept,
                r => r.Quantity);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ExtractorTestHelper.ValidateComposite<QuantitySearchValue>(
                    TestResource.ExpectedSystem2,
                    TestResource.ExpectedCode2,
                    r,
                    qsv => ValidateQuantity(qsv)),
                r => ExtractorTestHelper.ValidateComposite<QuantitySearchValue>(
                    TestResource.ExpectedSystem3,
                    TestResource.ExpectedCode3,
                    r,
                    qsv => ValidateQuantity(qsv)),
                r => ExtractorTestHelper.ValidateComposite<QuantitySearchValue>(
                    TestResource.ExpectedSystem2,
                    TestResource.ExpectedCode2,
                    r,
                    qsv => ValidateQuantity(qsv)),
                r => ExtractorTestHelper.ValidateComposite<QuantitySearchValue>(
                    TestResource.ExpectedSystem3,
                    TestResource.ExpectedCode3,
                    r,
                    qsv => ValidateQuantity(qsv)));
        }

        private CompositeQuantityExtractor<TestResource, TCollection> Create<TCollection>(
            Func<TestResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, CodeableConcept> compositeTokenSelector,
            Func<TCollection, Quantity> quantitySelector)
        {
            return new CompositeQuantityExtractor<TestResource, TCollection>(
                collectionSelector,
                compositeTokenSelector,
                quantitySelector);
        }

        private void ValidateQuantity(QuantitySearchValue qsv)
        {
            Assert.Equal(TestResource.ExpectedQuantitySystem, qsv.System);
            Assert.Equal(TestResource.ExpectedQuantityCode, qsv.Code);
            Assert.Equal(TestResource.ExpectedQuantityValue, qsv.Quantity);
        }
    }
}
