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
    public class StringExtractorTests
    {
        private readonly TestResource _testResource = new TestResource();

        [Fact]
        public void GivenANullStrings_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => null);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenAnEmptyStrings_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => new string[0]);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenANullString_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => new string[] { null });

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenAString_WhenExtracting_ThenOneResultShouldBeReturned()
        {
            var extractor = Create(
                r => new string[] { r.FhirString.Value });

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateString(r));
        }

        [Fact]
        public void GivenMultipleStrings_WhenExtracting_ThenMultipleResultsShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r.FhirString.Value, 2));

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateString(r),
                r => ValidateString(r));
        }

        private StringExtractor<TestResource> Create(
            Func<TestResource, IEnumerable<string>> stringsSelector)
        {
            return new StringExtractor<TestResource>(
                stringsSelector);
        }

        private void ValidateString(ISearchValue searchValue)
        {
            StringSearchValue ssv = Assert.IsType<StringSearchValue>(searchValue);

            Assert.Equal(TestResource.ExpectedString, ssv.String);
        }
    }
}
