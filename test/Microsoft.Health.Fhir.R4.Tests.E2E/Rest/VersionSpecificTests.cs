// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Provides R4 specific tests.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DomainLogicValidation)]
    public partial class VersionSpecificTests : IClassFixture<HttpIntegrationTestFixture>
    {
        [Fact]
        public async Task GivenR4Server_WhenCapabilityStatementIsRetrieved_ThenCorrectVersionShouldBeReturned()
        {
            await TestCapabilityStatementFhirVersion("4.0.1");
        }

        [Fact]
        public async Task GivenAnObservationDefinition_WhenCreating_ThenTheCorrectResponseShouldBeReturned()
        {
            var resource = Samples.GetJsonSample<ObservationDefinition>("ObservationDefinition-example");

            Resource actual = await _client.CreateAsync(resource);

            Assert.NotNull(actual);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenAnObservation_WithInvalidDecimalSpecification_ThenBadRequestShouldBeReturned()
        {
            var resource = Samples.GetJsonSample<Observation>("ObservationWithInvalidDecimalSpecification");
            using FhirClientException exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.CreateAsync(resource));
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [Fact]
        public async Task GivenANewR4ResourceType_WhenCreated_ThenCorrectResourceShouldBeReturned()
        {
            var testId = Guid.NewGuid().ToString();

            MedicinalProduct mp = Samples.GetJsonSample<MedicinalProduct>("MedicinalProduct");

            mp.Identifier.Add(new Identifier(string.Empty, testId));

            Resource actual = await _client.CreateAsync(mp);

            Assert.NotNull(actual);
            Assert.Equal(ResourceType.MedicinalProduct.ToString(), actual.TypeName);

            // We should also be able to search.
            Bundle bundle = await _client.SearchAsync(ResourceType.MedicinalProduct, $"identifier={testId}");

            Assert.Collection(bundle.Entry, e => Assert.Equal(actual.Id, e.Resource.Id));
        }

        [Fact]
        public async Task GivenAResourceThatWasRenamed_WhenSearched_ThenExceptionShouldBeThrown()
        {
            using FhirClientException exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.SearchAsync("Sequence"));

            Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        }
    }
}
