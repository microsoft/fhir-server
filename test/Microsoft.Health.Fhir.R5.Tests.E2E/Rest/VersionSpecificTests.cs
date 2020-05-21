// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Provides R5 specific tests.
    /// </summary>
    public partial class VersionSpecificTests : IClassFixture<HttpIntegrationTestFixture>
    {
        [Fact]
        public async Task GivenR5Server_WhenCapabilityStatementIsRetrieved_ThenCorrectVersionShouldBeReturned()
        {
            await TestCapabilityStatementFhirVersion("4.4.0");
        }

        [Fact]
        public async Task GivenAnObservationDefinition_WhenCreating_ThenTheCorrectResponseShouldBeReturned()
        {
            var resource = Samples.GetJsonSample<ObservationDefinition>("ObservationDefinition-example");

            Resource actual = await _client.CreateAsync(resource);

            Assert.NotNull(actual);
        }

        [Fact]
        public async Task GivenANewR5ResourceType_WhenCreated_ThenCorrectResourceShouldBeReturned()
        {
            var testId = Guid.NewGuid().ToString();

            MedicinalProductDefinition mpd = Samples.GetJsonSample<MedicinalProductDefinition>("MedicinalProductDefinition");

            mpd.Identifier.Add(new Identifier(string.Empty, testId));

            Resource actual = await _client.CreateAsync(mpd);

            Assert.NotNull(actual);
            Assert.Equal(ResourceType.MedicinalProductDefinition, actual.ResourceType);

            // We should also be able to search.
            Bundle bundle = await _client.SearchAsync(ResourceType.MedicinalProductDefinition, $"identifier={testId}");

            Assert.Collection(bundle.Entry, e => Assert.Equal(actual.Id, e.Resource.Id));
        }

        [Fact]
        public async Task GivenAResourceThatWasRenamed_WhenSearched_ThenExceptionShouldBeThrown()
        {
            FhirException exception = await Assert.ThrowsAsync<FhirException>(() => _client.SearchAsync("Sequence"));

            Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        }
    }
}
