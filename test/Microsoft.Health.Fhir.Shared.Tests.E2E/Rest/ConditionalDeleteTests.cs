// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.ConditionalOperations)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class ConditionalDeleteTests : IClassFixture<HttpIntegrationTestFixture<Startup>>
    {
        private const string _resourceType = KnownResourceTypes.Encounter;
        private readonly TestFhirClient _client;
        private SemaphoreSlim _createSemaphore = new(5, 5);

        public ConditionalDeleteTests(HttpIntegrationTestFixture<Startup> fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [InlineData(1)]
        [InlineData(100)]
        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenNoExistingResources_WhenDeletingConditionally_TheServerShouldReturnAccepted(int deleteCount)
        {
            var identifier = Guid.NewGuid().ToString();
            await ValidateResults(identifier, 0);

            FhirResponse response = await _client.DeleteAsync($"{_resourceType}?identifier={identifier}&_count={deleteCount}", CancellationToken.None);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [InlineData(true, 1)]
        [InlineData(true, 100)]
        [InlineData(false, 1)]
        [InlineData(false, 100)]
        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenOneMatchingResource_WhenDeletingConditionally_TheServerShouldDeleteSuccessfully(bool hardDelete, int deleteCount)
        {
            var identifier = Guid.NewGuid().ToString();
            await CreateWithIdentifier(identifier);
            await ValidateResults(identifier, 1);

            FhirResponse response = await _client.DeleteAsync($"{_resourceType}?identifier={identifier}&hardDelete={hardDelete}&_count={deleteCount}", CancellationToken.None);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            await ValidateResults(identifier, 0);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenMultipleMatchingResources_WhenDeletingConditionallyInSingleMode_TheServerShouldReturnError()
        {
            var identifier = Guid.NewGuid().ToString();
            await CreateWithIdentifier(identifier);
            await CreateWithIdentifier(identifier);
            await ValidateResults(identifier, 2);

            await Assert.ThrowsAsync<FhirException>(() => _client.DeleteAsync($"{_resourceType}?identifier={identifier}", CancellationToken.None));
        }

        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(int.MaxValue)]
        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenMultipleMatchingResources_WhenDeletingConditionallyWithOutOfRanceCount_TheServerShouldReturnError(int deleteCount)
        {
            var identifier = Guid.NewGuid().ToString();
            await Assert.ThrowsAsync<FhirException>(() => _client.DeleteAsync($"{_resourceType}?identifier={identifier}&_count={deleteCount}", CancellationToken.None));
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

            FhirResponse response = await _client.DeleteAsync($"{_resourceType}?identifier={identifier}&hardDelete={hardDelete}&_count=100", CancellationToken.None);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(3, int.Parse(response.Headers.GetValues(KnownHeaders.ItemsDeleted).First()));

            await ValidateResults(identifier, 0);
        }

        [InlineData(true, 50, 50, 0)]
        [InlineData(false, 50, 50, 0)]
        [InlineData(true, 10, 5, 5)]
        [InlineData(false, 10, 5, 5)]
        [Theory]
        public async Task GivenMatchingResources_WhenDeletingConditionallyWithMultipleFlag_TheServerShouldDeleteSuccessfully(bool hardDelete, int create, int delete, int expected)
        {
            var identifier = Guid.NewGuid().ToString();

            await Task.WhenAll(Enumerable.Range(1, create).Select(_ => CreateWithIdentifier(identifier)));

            await GetResourceCount(identifier);

            FhirResponse response = await _client.DeleteAsync($"{_resourceType}?identifier={identifier}&hardDelete={hardDelete}&_count={delete}", CancellationToken.None);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(delete, int.Parse(response.Headers.GetValues(KnownHeaders.ItemsDeleted).First()));

            await ValidateResults(identifier, expected);
        }

        private async Task CreateWithIdentifier(string identifier)
        {
            await _createSemaphore.WaitAsync();

            try
            {
                Encounter encounter = Samples.GetJsonSample("Encounter-For-Patient-f001").ToPoco<Encounter>();

                encounter.Identifier.Add(new Identifier("http://e2etests", identifier));
                using FhirResponse<Encounter> response = await _client.CreateAsync(encounter);

                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            }
            finally
            {
                _createSemaphore.Release();
            }
        }

        private async Task ValidateResults(string identifier, int expected)
        {
            var result = await GetResourceCount(identifier);
            Assert.Equal(expected, result);
        }

        private async Task<int?> GetResourceCount(string identifier)
        {
            FhirResponse<Bundle> result = await _client.SearchAsync(ResourceType.Encounter, $"identifier={identifier}&_summary=count");
            return result.Resource.Total;
        }
    }
}
