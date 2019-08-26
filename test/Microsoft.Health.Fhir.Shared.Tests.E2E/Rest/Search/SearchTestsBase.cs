// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Xunit;
using FhirClient = Microsoft.Health.Fhir.Tests.E2E.Common.FhirClient;

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

        protected FhirClient Client => Fixture.FhirClient;

        protected async Task<Bundle> ExecuteAndValidateBundle(string searchUrl, params Resource[] expectedResources)
        {
            return await ExecuteAndValidateBundle(searchUrl, searchUrl, expectedResources);
        }

        protected async Task<Bundle> ExecuteAndValidateBundle(string searchUrl, string selfLink, params Resource[] expectedResources)
        {
            Bundle bundle = await Client.SearchAsync(searchUrl);

            ValidateBundle(bundle, selfLink, expectedResources);

            return bundle;
        }

        protected void ValidateBundle(Bundle bundle, string selfLink, params Resource[] expectedResources)
        {
            ValidateBundle(bundle, expectedResources);

            var actualUrl = WebUtility.UrlDecode(bundle.SelfLink.AbsoluteUri);

            Assert.Equal(Fixture.GenerateFullUrl(selfLink), actualUrl);
        }

        protected void ValidateBundle(Bundle bundle, params Resource[] expectedResources)
        {
            Assert.NotNull(bundle);

            bundle.Entry.Sort((a, b) => string.CompareOrdinal(a.Resource.Id, b.Resource.Id));
            Array.Sort(expectedResources, (a, b) => string.CompareOrdinal(a.Id, b.Id));

            Assert.Collection(
                bundle.Entry.Select(e => e.Resource),
                expectedResources.Select(er => new Action<Resource>(r => Assert.True(er.IsExactly(r)))).ToArray());
        }
    }
}
