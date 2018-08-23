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
    public class QuantityExtractorTests
    {
        private readonly TestResource _testResource = new TestResource();

        [Fact]
        public void GivenANullCollection_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => (IEnumerable<TestResource>)null,
                r => r.Quantity);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenANullQuantity_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 1),
                r => null);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenAnEmptyQuantity_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 1),
                r => new Quantity());

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenASingleCollection_WhenExtracting_ThenOneResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 1),
                r => r.Quantity);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateQuantity(r));
        }

        [Fact]
        public void GivenMultipleCollections_WhenExtracting_ThenMultipleResultsShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 2),
                r => r.Quantity);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateQuantity(r),
                r => ValidateQuantity(r));
        }

        private QuantityExtractor<TestResource, TCollection> Create<TCollection>(
            Func<TestResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, Quantity> quantitySelector)
        {
            return new QuantityExtractor<TestResource, TCollection>(
                collectionSelector,
                quantitySelector);
        }

        private void ValidateQuantity(ISearchValue searchValue)
        {
            QuantitySearchValue qsv = Assert.IsType<QuantitySearchValue>(searchValue);

            Assert.Equal(TestResource.ExpectedQuantitySystem, qsv.System);
            Assert.Equal(TestResource.ExpectedQuantityCode, qsv.Code);
            Assert.Equal(TestResource.ExpectedQuantityValue, qsv.Quantity);
        }
    }
}
