// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Provides STU3 specific tests.
    /// </summary>
    public partial class VersionSpecificTests : IClassFixture<HttpIntegrationTestFixture>
    {
        [Fact]
        public async Task GivenStu3Server_WhenCapabilityStatementIsRetrieved_ThenCorrectVersionShouldBeReturned()
        {
            await TestCapabilityStatementFhirVersion("3.0.2");
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenAnObservation_WithInvalidDecimalSpecification_ThenBadRequestShouldBeReturned()
        {
            var resource = Samples.GetJsonSample<Observation>("ObservationWithInvalidDecimalSpecification");
            using FhirException exception = await Assert.ThrowsAsync<FhirException>(() => _client.CreateAsync(resource));
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }
    }
}
