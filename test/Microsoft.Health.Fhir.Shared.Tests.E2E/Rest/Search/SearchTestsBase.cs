// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public abstract class SearchTestsBase<TFixture> : IClassFixture<TFixture>
        where TFixture : HttpIntegrationTestFixture
    {
        private const string ContinuationToken = "&ct";

        protected SearchTestsBase(TFixture fixture)
        {
            Fixture = fixture;
        }

        protected TFixture Fixture { get; }

        protected TestFhirClient Client => Fixture.TestFhirClient;

        protected async Task<Bundle> ExecuteAndValidateBundle(string searchUrl, params Resource[] expectedResources)
        {
            return await ExecuteAndValidateBundle(searchUrl, searchUrl, true, expectedResources);
        }

        protected async Task<Bundle> ExecuteAndValidateBundle(string searchUrl, bool sort, params Resource[] expectedResources)
        {
            return await ExecuteAndValidateBundle(searchUrl, searchUrl, sort, expectedResources);
        }

        protected async Task<Bundle> ExecuteAndValidateBundleSuperset(string searchUrl, bool sort, params Resource[] expectedResources)
        {
            Bundle bundle = await Client.SearchAsync(searchUrl);

            ValidateBundleIsSuperset(bundle, sort, expectedResources);

            return bundle;
        }

        protected async Task<Bundle> ExecuteAndValidateBundle(string searchUrl, string selfLink, params Resource[] expectedResources)
        {
            Bundle bundle = await Client.SearchAsync(searchUrl);

            ValidateBundle(bundle, selfLink, true, expectedResources);

            return bundle;
        }

        protected async Task<Bundle> ExecuteAndValidateBundle(string searchUrl, string selfLink, bool sort, params Resource[] expectedResources)
        {
            FhirResponse<Bundle> firstBundle = await Client.SearchAsync(searchUrl);

            var pageSize = 10;
            var expectedFirstBundle = expectedResources.Length > pageSize ? expectedResources.ToList().GetRange(0, pageSize).ToArray() : expectedResources;

            ValidateBundle(firstBundle, selfLink, sort, expectedFirstBundle);

            var nextLink = firstBundle.Resource.NextLink?.ToString();
            if (nextLink != null)
            {
                FhirResponse<Bundle> secondBundle = await Client.SearchAsync(nextLink);

                // Truncating host and appending continuation token
                nextLink = selfLink + nextLink.Substring(nextLink.IndexOf(ContinuationToken));
                ValidateBundle(secondBundle, nextLink, sort, expectedResources.ToList().GetRange(pageSize, expectedResources.Length - pageSize).ToArray());
            }

            return firstBundle;
        }

        protected void ValidateBundle(Bundle bundle, string selfLink, params Resource[] expectedResources)
        {
            ValidateBundle(bundle, selfLink, true, expectedResources);
        }

        protected void ValidateBundle(Bundle bundle, string selfLink, bool sort, params Resource[] expectedResources)
        {
            ValidateBundle(bundle, sort, expectedResources);
            string actualUrl;

            // checking if continuation token is present in the link
            if (bundle.SelfLink.AbsoluteUri.Contains(ContinuationToken))
            {
                // avoiding url decode of continuation token
                int tokenIndex = bundle.SelfLink.AbsoluteUri.IndexOf(ContinuationToken, StringComparison.Ordinal);
                actualUrl = WebUtility.UrlDecode(bundle.SelfLink.AbsoluteUri.Substring(0, tokenIndex)) + bundle.SelfLink.AbsoluteUri.Substring(tokenIndex);
            }
            else
            {
                actualUrl = WebUtility.UrlDecode(bundle.SelfLink.AbsoluteUri);
            }

            Assert.Equal(Fixture.GenerateFullUrl(selfLink), actualUrl);
        }

        protected void ValidateBundle(Bundle bundle, params Resource[] expectedResources)
        {
            ValidateBundle(bundle, true, expectedResources);
        }

        protected void ValidateBundle(Bundle bundle, bool sort, params Resource[] expectedResources)
        {
            Assert.NotNull(bundle);

            if (sort)
            {
                bundle.Entry.Sort((a, b) => string.CompareOrdinal(a.Resource.Id, b.Resource.Id));
                Array.Sort(expectedResources, (a, b) => string.CompareOrdinal(a.Id, b.Id));
            }

            Assert.Collection(
                bundle.Entry.Select(e => e.Resource),
                expectedResources.Select(er => new Action<Resource>(r => Assert.True(er.IsExactly(r)))).ToArray());
        }

        protected void ValidateBundleIsSuperset(Bundle bundle, bool sort, params Resource[] expectedResources)
        {
            Assert.NotNull(bundle);

            if (sort)
            {
                bundle.Entry.Sort((a, b) => string.CompareOrdinal(a.Resource.Id, b.Resource.Id));
                Array.Sort(expectedResources, (a, b) => string.CompareOrdinal(a.Id, b.Id));
            }

            // make sure the result is not empty
            Assert.NotEmpty(bundle.Entry.Select(e => e.Resource));

            // make sure the returned result contains all of the expected item
            // and maybe some more items (if the db is not empty, if we didn't use tag to filter) ...
            var set = new HashSet<string>();
            foreach (var r in bundle.Entry)
            {
                set.Add(r.Resource.Id);
            }

            foreach (var r in expectedResources)
            {
                Assert.Contains(r.Id, set);
            }

            // make sure items are sorted
            var expectedList = expectedResources.OrderBy(x => ((Patient)x).BirthDate);
            Assert.True(expectedList.SequenceEqual(expectedResources));
        }
    }
}
