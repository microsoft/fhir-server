// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public abstract class GraphQlBase<TFixture> : IClassFixture<TFixture>
        where TFixture : HttpIntegrationTestFixture
    {
        private Regex _continuationToken = new Regex("[?&]ct");

        protected GraphQlBase(TFixture fixture)
        {
            Fixture = fixture;
        }

        protected TFixture Fixture { get; }

        protected TestFhirClient Client => Fixture.TestFhirClient;

        /*protected async Task<Bundle> ExecuteAndValidateBundle(string searchUrl, string selfLink, Tuple<string, string> customHeader, params Resource[] expectedResources)
        {
            Bundle bundle = await Client.SearchAsync(searchUrl, customHeader);

            ValidateBundle(bundle, selfLink, true, expectedResources);

            return bundle;
        }*/

        protected async Task<System.Net.Http.HttpResponseMessage> SendGraphQlRequest(string url)
        {
            var response = await Client.HttpClient.GetAsync(url);

            return response;
        }
    }
}
