// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Web;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class ConditionalDeleteTests : IClassFixture<HttpIntegrationTestFixture<Startup>>
    {
        private readonly TestFhirClient _client;

        public ConditionalDeleteTests(HttpIntegrationTestFixture<Startup> fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenNoExistingResources_WhenDeletingConditionally_TheServerShouldReturnAccepted()
        {
            var identifier = Guid.NewGuid().ToString();
            await ValidateResults(identifier, 0);

            FhirResponse response = await _client.DeleteAsync($"{KnownResourceTypes.Observation}?identifier={identifier}", CancellationToken.None);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [InlineData(true)]
        [InlineData(false)]
        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenOneMatchingResource_WhenDeletingConditionally_TheServerShouldDeleteSuccessfully(bool hardDelete)
        {
            var identifier = Guid.NewGuid().ToString();
            await CreateWithIdentifier(identifier);
            await ValidateResults(identifier, 1);

            FhirResponse response = await _client.DeleteAsync($"{KnownResourceTypes.Observation}?identifier={identifier}&hardDelete={hardDelete}", CancellationToken.None);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            await ValidateResults(identifier, 0);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenMultipleMatchingResources_WhenDeletingConditionally_TheServerShouldReturnError()
        {
            var identifier = Guid.NewGuid().ToString();
            await CreateWithIdentifier(identifier);
            await CreateWithIdentifier(identifier);
            await ValidateResults(identifier, 2);

            await Assert.ThrowsAsync<FhirException>(() => _client.DeleteAsync($"{KnownResourceTypes.Observation}?identifier={identifier}", CancellationToken.None));
        }

        [InlineData(true)]
        [InlineData(false)]
        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenMultipleMatchingResources_WhenDeletingConditionallyWithMultipleFlag_TheServerShouldDeleteSuccessfully(bool hardDelete)
        {
            var identifier = Guid.NewGuid().ToString();
            await CreateWithIdentifier(identifier);
            await CreateWithIdentifier(identifier);
            await CreateWithIdentifier(identifier);
            await ValidateResults(identifier, 3);

            FhirResponse response = await _client.DeleteAsync($"{KnownResourceTypes.Observation}?identifier={identifier}&hardDelete={hardDelete}&multiple=true", CancellationToken.None);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(3, int.Parse(response.Headers.GetValues(KnownHeaders.ItemsDeleted).First()));

            await ValidateResults(identifier, 0);
        }

        [InlineData(true)]
        [InlineData(false)]
        [Theory]
        public async Task Given50MatchingResources_WhenDeletingConditionallyWithMultipleFlag_TheServerShouldDeleteSuccessfully(bool hardDelete)
        {
            var identifier = Guid.NewGuid().ToString();

            await Task.WhenAll(Enumerable.Range(1, 50).Select(_ => CreateWithIdentifier(identifier)));

            await ValidateResults(identifier, 50);

            FhirResponse response = await _client.DeleteAsync($"{KnownResourceTypes.Observation}?identifier={identifier}&hardDelete={hardDelete}&multiple=true", CancellationToken.None);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(50, int.Parse(response.Headers.GetValues(KnownHeaders.ItemsDeleted).First()));

            await ValidateResults(identifier, 0);
        }

        private async Task CreateWithIdentifier(string identifier)
        {
            Observation observation = Samples.GetDefaultObservation().ToPoco<Observation>();

            observation.Identifier.Add(new Identifier("http://e2etests", identifier));
            using FhirResponse<Observation> response = await _client.CreateAsync(observation);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        private async Task ValidateResults(string identifier, int expected)
        {
            FhirResponse<Bundle> result = await _client.SearchAsync(ResourceType.Observation, $"identifier={identifier}&_total=accurate");
            Assert.Equal(expected, result.Resource.Total);
        }
    }
}
