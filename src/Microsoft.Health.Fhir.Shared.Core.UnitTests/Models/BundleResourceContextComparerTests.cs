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
        [Fact]
        public void GivenASequenceOfResources_WhenSorting_ThenRespectTheSequenceExpected()
        {
            Guid bundleOperationId = Guid.NewGuid();

            var resources = new BundleResourceContext[]
            {
                new BundleResourceContext(HTTPVerb.HEAD, bundleOperationId),
                new BundleResourceContext(HTTPVerb.GET, bundleOperationId),
                new BundleResourceContext(HTTPVerb.PATCH, bundleOperationId),
                new BundleResourceContext(HTTPVerb.PUT, bundleOperationId),
                new BundleResourceContext(HTTPVerb.POST, bundleOperationId),
                new BundleResourceContext(HTTPVerb.DELETE, bundleOperationId),
            };

            var sortedResources = resources.OrderBy(x => x, new BundleResourceContextComparer()).ToList();

            Assert.Equal(HTTPVerb.DELETE, sortedResources[0].HttpVerb);
            Assert.Equal(HTTPVerb.POST, sortedResources[0].HttpVerb);
            Assert.Equal(HTTPVerb.PUT, sortedResources[0].HttpVerb);
            Assert.Equal(HTTPVerb.PATCH, sortedResources[0].HttpVerb);
            Assert.Equal(HTTPVerb.GET, sortedResources[0].HttpVerb);
            Assert.Equal(HTTPVerb.HEAD, sortedResources[0].HttpVerb);
        }
    }
}
