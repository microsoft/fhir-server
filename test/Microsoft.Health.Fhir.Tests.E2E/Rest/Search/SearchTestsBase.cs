// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Web;
using Xunit;
using FhirClient = Microsoft.Health.Fhir.Tests.E2E.Common.FhirClient;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public abstract class SearchTestsBase<TFixture> : IClassFixture<TFixture>
        where TFixture : HttpIntegrationTestFixture<Startup>
    {
        protected SearchTestsBase(TFixture fixture)
            : this(fixture, fixture.FhirClient)
        {
        }

        protected SearchTestsBase(TFixture fixture, FhirClient client)
        {
            Fixture = fixture;
            Client = client;
        }

        protected TFixture Fixture { get; }

        protected FhirClient Client { get; }

        protected void ValidateBundle(Bundle bundle, params Resource[] expectedResources)
        {
            Assert.NotNull(bundle);
            Assert.Collection(
                bundle.Entry.Select(e => e.Resource),
                expectedResources.Select(er => new Action<Resource>(r => Assert.True(er.IsExactly(r)))).ToArray());
        }

        ////protected async Task<Bundle> SearchAsync(ResourceType resourceType, string queryValue)
        ////{
        ////    // Append the test session id.
        ////    return await Client.SearchAsync(
        ////        resourceType,
        ////        $"identifier={Fixture.TestSessionId}&{queryValue}");
        ////}
    }
}
