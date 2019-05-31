// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public abstract class SearchTestsBase<TFixture> : IClassFixture<TFixture>
        where TFixture : HttpIntegrationTestFixture
    {
        protected SearchTestsBase(TFixture fixture)
        {
            Fixture = fixture;
        }

        protected TFixture Fixture { get; }

        protected ICustomFhirClient Client => Fixture.FhirClient;

        protected async Task<ResourceElement> ExecuteAndValidateBundle(string searchUrl, params ResourceElement[] expectedResources)
        {
            return await ExecuteAndValidateBundle(searchUrl, searchUrl, expectedResources);
        }

        protected async Task<ResourceElement> ExecuteAndValidateBundle(string searchUrl, string selfLink, params ResourceElement[] expectedResources)
        {
            ResourceElement bundle = await Client.SearchAsync(searchUrl);

            ValidateBundle(bundle, selfLink, expectedResources);

            return bundle;
        }

        protected void ValidateBundle(ResourceElement bundle, string selfLink, params ResourceElement[] expectedResources)
        {
            ValidateBundle(bundle, expectedResources);

            var actualUrl = WebUtility.UrlDecode(bundle.Scalar<string>("Resource.link.where(relation = 'self').url"));

            Assert.Equal(Fixture.GenerateFullUrl(selfLink), actualUrl);
        }

        protected void ValidateBundle(ResourceElement bundle, params ResourceElement[] expectedResources)
        {
            Assert.NotNull(bundle);
            Assert.Collection(
                bundle.Select("Resource.entry.resource"),
                expectedResources.Select(er => new Action<ITypedElement>(r => Assert.True(Client.Compare(er, r)))).ToArray());
        }

        protected void ValidateBundle(IEnumerable<ResourceElement> resources, params ResourceElement[] expectedResources)
        {
            Assert.NotNull(resources);
            Assert.Collection(
                resources,
                expectedResources.Select(er => new Action<ResourceElement>(r => Assert.True(Client.Compare(er, r)))).ToArray());
        }
    }
}
