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
    public class TokenExtractorTests
    {
        private readonly TestResource _testResource = new TestResource();

        [Fact]
        public void GivenANullCollection_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => (IEnumerable<Coding>)null,
                r => r.System,
                r => r.Code);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenNoneEmptySystemAndEmptyCode_WhenExtracting_ThenOneResultShouldBeReturned()
        {
            var extractor = Create(
                r => r.SingleCodeableConcept.Coding,
                r => r.System,
                r => null);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateToken(
                    TestResource.ExpectedSystem1,
                    null,
                    r));
        }

        [Fact]
        public void GivenEmptySystemAndNoneEmptyCode_WhenExtracting_ThenOneResultShouldBeReturned()
        {
            var extractor = Create(
                r => r.SingleCodeableConcept.Coding,
                r => null,
                r => r.Code);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateToken(
                    null,
                    TestResource.ExpectedCode1,
                    r));
        }

        [Fact]
        public void GivenASingleCollection_WhenExtracting_ThenOneResultShouldBeReturned()
        {
            var extractor = Create(
                r => r.SingleCodeableConcept.Coding,
                r => r.System,
                r => r.Code);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateToken(
                    TestResource.ExpectedSystem1,
                    TestResource.ExpectedCode1,
                    r));
        }

        [Fact]
        public void GivenMultipleCollections_WhenExtracting_ThenMultipleResultsShouldBeReturned()
        {
            var extractor = Create(
                r => r.MultipleCodeableConcept.Coding,
                r => r.System,
                r => r.Code);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateToken(
                    TestResource.ExpectedSystem2,
                    TestResource.ExpectedCode2,
                    r),
                r => ValidateToken(
                    TestResource.ExpectedSystem3,
                    TestResource.ExpectedCode3,
                    r));
        }

        private TokenExtractor<TestResource, TCollection> Create<TCollection>(
            Func<TestResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, string> systemSelector,
            Func<TCollection, string> codeSelector)
        {
            return new TokenExtractor<TestResource, TCollection>(
                collectionSelector,
                systemSelector,
                codeSelector);
        }

        private void ValidateToken(string expectedSystem, string expectedCode, ISearchValue searchValue)
        {
            TokenSearchValue tsv = Assert.IsType<TokenSearchValue>(searchValue);

            Assert.Equal(expectedSystem, tsv.System);
            Assert.Equal(expectedCode, tsv.Code);
        }
    }
}
