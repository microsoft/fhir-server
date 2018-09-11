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
    public class CompositeReferenceExtractorTests
    {
        private readonly TestResource _testResource = new TestResource();

        [Fact]
        public void GivenANullCollection_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => (IEnumerable<TestResource>)null,
                r => r.ResourceType,
                r => r.ResourceReference);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenANullEnum_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 1),
                r => null,
                r => r.ResourceReference);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenANullReference_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 1),
                r => r.ResourceType,
                r => null);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenAnEmptyReference_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 1),
                r => r.ResourceType,
                r => new ResourceReference());

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        [Fact]
        public void GivenAReference_WhenExtracting_ThenOneResultShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 1),
                r => r.ResourceType,
                r => r.ResourceReference);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateComposite(r));
        }

        [Fact]
        public void GivenMultipleCollections_WhenExtracting_ThenMultipleResultsShouldBeReturned()
        {
            var extractor = Create(
                r => Enumerable.Repeat(r, 2),
                r => r.ResourceType,
                r => r.ResourceReference);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateComposite(r),
                r => ValidateComposite(r));
        }

        [Fact]
        public void GivenAContainedReference_WhenExtracting_ThenContainedReferenceShouldBeIgnored()
        {
            _testResource.ResourceReference = new ResourceReference()
            {
                Reference = "#res1",
            };

            var extractor = Create(
                r => Enumerable.Repeat(r, 2),
                r => r.ResourceType,
                r => r.ResourceReference);

            IReadOnlyCollection<ISearchValue> results = extractor.Extract(_testResource);

            ExtractorTestHelper.ValidateCollectionIsEmpty(results);
        }

        private CompositeReferenceExtractor<TestResource, TCollection> Create<TCollection>(
            Func<TestResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, Enum> compositeTokenSelector,
            Func<TCollection, ResourceReference> referenceSelector)
        {
            return new CompositeReferenceExtractor<TestResource, TCollection>(
                collectionSelector,
                compositeTokenSelector,
                referenceSelector);
        }

        private void ValidateComposite(ISearchValue searchValue)
        {
            ExtractorTestHelper.ValidateComposite<ReferenceSearchValue>(
                    "http://hl7.org/fhir/resource-types",
                    "Resource",
                    searchValue,
                    rsv => ValidateReference(rsv));
        }

        private void ValidateReference(ReferenceSearchValue rsv)
        {
            Assert.Equal(TestResource.ExpectedReference, rsv.Reference);
        }
    }
}
