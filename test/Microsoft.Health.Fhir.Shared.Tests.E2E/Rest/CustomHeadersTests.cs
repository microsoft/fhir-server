// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.CustomHeaders)]
    [Trait(Traits.Category, Categories.Web)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class CustomHeadersTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public CustomHeadersTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenRequestIdHeader_WhenSendRequest_TheServerShouldReturnSameValueInCorrelationHeader()
        {
            var message = new HttpRequestMessage(HttpMethod.Get, "Patient");
            var id = Guid.NewGuid().ToString();
            message.Headers.Add(KnownHeaders.RequestId, id);

            using HttpResponseMessage response = await _client.HttpClient.SendAsync(message);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(id, response.Headers.GetValues(KnownHeaders.CorrelationId));
            Assert.DoesNotContain(id, response.Headers.GetValues(KnownHeaders.RequestId));
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenNoRequestHeader_WhenSendRequest_TheServerShouldntReturnCorrelationId()
        {
            var message = new HttpRequestMessage(HttpMethod.Get, "Patient");

            using HttpResponseMessage response = await _client.HttpClient.SendAsync(message);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotEmpty(response.Headers.GetValues(KnownHeaders.RequestId));
            Assert.False(response.Headers.Contains(KnownHeaders.CorrelationId));
        }

        [Theory]
        [InlineData("1")]
        [InlineData("2")]
        [InlineData("3")]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb)]
        public async Task GivenValidContinuationTokenLimitHeader_WhenSendRequest_TheServerShouldReturnSuccess(string tokenSize)
        {
            var message = new HttpRequestMessage(HttpMethod.Get, "Patient");
            message.Headers.Add(CosmosDbHeaders.CosmosContinuationTokenSize, tokenSize);

            using HttpResponseMessage response = await _client.HttpClient.SendAsync(message);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Theory]
        [InlineData("invalid")]
        [InlineData("5")]
        [InlineData("-1")]
        [InlineData("9999999999999999999999999999")]
        [InlineData("1.0")]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb)]
        public async Task GivenInValidContinuationTokenLimitHeader_WhenSendRequest_ThenBadRequestShouldBeReturned(string tokenSize)
        {
            var message = new HttpRequestMessage(HttpMethod.Get, "Patient");
            message.Headers.Add(CosmosDbHeaders.CosmosContinuationTokenSize, tokenSize);

            using HttpResponseMessage response = await _client.HttpClient.SendAsync(message);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
