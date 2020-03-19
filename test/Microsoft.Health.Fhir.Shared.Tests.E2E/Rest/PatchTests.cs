// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class PatchTests : IClassFixture<HttpIntegrationTestFixture>
    {
        public PatchTests(HttpIntegrationTestFixture fixture)
        {
            Client = fixture.FhirClient;
        }

        protected FhirClient Client { get; set; }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatDoesNotSupportIt_WhenSubmittingAPatch_ThenMethodNotAllowedIsReturned()
        {
            FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.PatchAsync("Patient/1234", "patch content"));

            Assert.Equal(HttpStatusCode.MethodNotAllowed, ex.StatusCode);
        }
    }
}
