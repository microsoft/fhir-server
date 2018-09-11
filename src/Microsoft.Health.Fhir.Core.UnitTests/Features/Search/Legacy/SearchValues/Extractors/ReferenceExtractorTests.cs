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
    public class ReferenceExtractorTests
    {
        private readonly TestResource _testResource = new TestResource();

        [Fact]
        public void GivenANullCollection_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => (IEnumerable<TestResource>)null,
                r => r.ResourceReference);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenANullReference_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 1),
                r => null);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenAnEmptyReference_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 1),
                r => new ResourceReference());

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenASingleCollection_WhenExtracting_ThenOneResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 1),
                r => r.ResourceReference);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateReference(r));
        }

        [Fact]
        public void GivenMultipleCollections_WhenExtracting_ThenMultipleResultsShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 2),
                r => r.ResourceReference);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateReference(r),
                r => ValidateReference(r));
        }

        [Fact]
        public void GivenASingleCollection_WhenExtractingForGivenResourceType_ThenOneResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 1),
                r => r.ResourceReference,
                FHIRAllTypes.Patient);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateReference(r));
        }

        [Fact]
        public void GivenASingleCollection_WhenExtractingForANonPresentResourceType_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 1),
                r => r.ResourceReference,
                FHIRAllTypes.Observation);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenAContainedReference_WhenExtracting_ThenContainedReferenceShouldBeIgnored()
        {
            _testResource.ResourceReference = new ResourceReference()
            {
                Reference = "#res1",
            };

            var extractor = Create(
                r => Enumerable.Repeat(r, 1),
                r => r.ResourceReference);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        private ReferenceExtractor<TestResource, TCollection> Create<TCollection>(
            Func<TestResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, ResourceReference> referenceSelector,
            FHIRAllTypes? resourceTypeFilter = null)
        {
            return new ReferenceExtractor<TestResource, TCollection>(
                collectionSelector,
                referenceSelector,
                resourceTypeFilter);
        }

        private void ValidateReference(ISearchValue searchValue)
        {
            ReferenceSearchValue rsv = Assert.IsType<ReferenceSearchValue>(searchValue);

            Assert.Equal(TestResource.ExpectedReference, rsv.Reference);
        }
    }
}
