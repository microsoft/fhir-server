// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Definition
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class EmbeddedSearchParameterDefinitionSourceTests
    {
        private readonly EmbeddedSearchParameterDefinitionSource _source =
            new EmbeddedSearchParameterDefinitionSource(ModelInfoProvider.Instance);

        [Fact]
        public void GivenTheEmbeddedSource_WhenSystemResourcesAreRead_ThenTheSpecAndMicrosoftBundlesAreBothIncluded()
        {
            IReadOnlyList<ITypedElement> resources = _source.GetSystemSearchParameterResources();

            Assert.NotEmpty(resources);

            IEnumerable<string> urls = resources.Select(r => r.Scalar("url")?.ToString());

            Assert.Contains("http://hl7.org/fhir/SearchParameter/Resource-id", urls);
            Assert.Contains(urls, u => u != null && u.StartsWith("http://hl7.org/fhir/SearchParameter/", StringComparison.Ordinal));
            Assert.Contains("https://azurehealthcareapis.com/data-extensions/expiry-date", urls);
        }

        [Fact]
        public void GivenTheEmbeddedSource_WhenSystemResourcesAreReadTwice_ThenTheSameCachedInstanceIsReturned()
        {
            IReadOnlyList<ITypedElement> first = _source.GetSystemSearchParameterResources();
            IReadOnlyList<ITypedElement> second = _source.GetSystemSearchParameterResources();

            Assert.Same(first, second);
        }

        [Fact]
        public void GivenTheEmbeddedSource_WhenSystemResourcesAreReadConcurrently_ThenTheSameCachedInstanceIsReturned()
        {
            var results = new IReadOnlyList<ITypedElement>[8];

            Parallel.For(0, results.Length, i => results[i] = _source.GetSystemSearchParameterResources());

            Assert.All(results, result => Assert.Same(results[0], result));
        }
    }
}
