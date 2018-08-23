// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Legacy.SearchValues
{
    public class NumberExtractorTests
    {
        private readonly TestResource _testResource = new TestResource();

        [Fact]
        public void GivenANullNumber_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => null);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenANumber_WhenExtracting_ThenOneResultShouldBeReturned()
        {
            var extractor = Create(
                r => new[] { r.Decimal });

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                ValidateNumber);
        }

        [Fact]
        public void GivenANumberAndNull_WhenExtracting_ThenOneResultShouldBeReturned()
        {
            var extractor = Create(
                r => new[] { r.Decimal, null });

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                ValidateNumber);
        }

        [Fact]
        public void GivenMultipleNumbers_WhenExtracting_ThenMultipleResultsShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r.Decimal, 2));

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                ValidateNumber,
                ValidateNumber);
        }

        private NumberExtractor<TestResource> Create(
            Func<TestResource, IEnumerable<decimal?>> numberSelector)
        {
            return new NumberExtractor<TestResource>(
                numberSelector);
        }

        private void ValidateNumber(ISearchValue searchValue)
        {
            NumberSearchValue nsv = Assert.IsType<NumberSearchValue>(searchValue);

            Assert.Equal(TestResource.ExpectedDecimal, nsv.Number);
        }
    }
}
