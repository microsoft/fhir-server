// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Models
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Bundle)]
    [Trait(Traits.Category, Categories.Transaction)]
    public sealed class BundleResourceContextComparerTests
    {
        private readonly Guid _bundleOperationId = Guid.NewGuid();
        private readonly BundleResourceContextComparer _comparer = new BundleResourceContextComparer();

        [Fact]
        public void GivenASequenceOfResources_WhenSorting_ThenRespectTheSequenceExpected()
        {
            var resources = new BundleResourceContext[]
            {
                new BundleResourceContext(HTTPVerb.HEAD, _bundleOperationId),
                new BundleResourceContext(HTTPVerb.GET, _bundleOperationId),
                new BundleResourceContext(HTTPVerb.PATCH, _bundleOperationId),
                new BundleResourceContext(HTTPVerb.PUT, _bundleOperationId),
                new BundleResourceContext(HTTPVerb.POST, _bundleOperationId),
                new BundleResourceContext(HTTPVerb.DELETE, _bundleOperationId),
            };

            var sortedResources = resources.OrderBy(x => x, _comparer).ToList();

            Assert.Equal(HTTPVerb.DELETE, sortedResources[0].HttpVerb);
            Assert.Equal(HTTPVerb.POST, sortedResources[1].HttpVerb);
            Assert.Equal(HTTPVerb.PUT, sortedResources[2].HttpVerb);
            Assert.Equal(HTTPVerb.PATCH, sortedResources[3].HttpVerb);
            Assert.Equal(HTTPVerb.GET, sortedResources[4].HttpVerb);
            Assert.Equal(HTTPVerb.HEAD, sortedResources[5].HttpVerb);
        }

        [Fact]
        public void GivenAEmptySequenceOfResources_WhenSorting_NoErrorsShouldBeRaised()
        {
            var resources = Array.Empty<BundleResourceContext>();
            var sortedResources = resources.OrderBy(x => x, _comparer).ToList();

            Assert.Empty(sortedResources);
        }

        [Fact]
        public void GivenASequenceWithASingleResource_WhenSorting_NoErrorsShouldBeRaised()
        {
            var resources = new BundleResourceContext[]
            {
                new BundleResourceContext(HTTPVerb.PATCH, _bundleOperationId),
            };

            var sortedResources = resources.OrderBy(x => x, _comparer).ToList();

            Assert.Single(sortedResources);
            Assert.Equal(HTTPVerb.PATCH, sortedResources[0].HttpVerb);
        }

        [Fact]
        public void GivenASequenceOfResourcesWithNullValues_WhenSorting_ThenRespectTheSequenceExpected()
        {
            var resources = new BundleResourceContext[]
            {
                null,
                new BundleResourceContext(HTTPVerb.POST, _bundleOperationId),
                null,
            };

            var sortedResources = resources.OrderBy(x => x, _comparer).ToList();

            Assert.Null(sortedResources[0]);
            Assert.Null(sortedResources[1]);
            Assert.Equal(HTTPVerb.POST, sortedResources[2].HttpVerb);
        }
    }
}
