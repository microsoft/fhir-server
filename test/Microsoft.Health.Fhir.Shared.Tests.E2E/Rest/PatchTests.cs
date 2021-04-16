// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class PatchTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public PatchTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingAPatch_ThenServerShouldPatchCorrectly()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);
            string patchDocument = "[{\"op\":\"replace\",\"path\":\"/gender\",\"value\":\"female\"}]";

            var patch = await _client.PatchAsync(response.Resource, patchDocument);

            Assert.Equal(HttpStatusCode.NoContent, patch.Response.StatusCode);
        }
    }
}
