// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Microsoft.Health.Test.Utilities;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.Rest
{
    [Trait(Traits.Category, Categories.Context)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class ContextTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public ContextTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenCorrelationHeader_WhenSendRequest_TheServerShouldReturnSameCorrelationHeader()
        {
            var message = new HttpRequestMessage(HttpMethod.Get, "/Patient");
            var id = Guid.NewGuid().ToString();
            message.Headers.Add(KnownHeaders.CorrelationId, id);

            using HttpResponseMessage response = await _client.HttpClient.SendAsync(message);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(id, response.Headers.GetValues(KnownHeaders.CorrelationId));
            Assert.DoesNotContain(id, response.Headers.GetValues(KnownHeaders.RequestId));
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenNoCorrelationHeader_WhenSendRequest_TheServerShouldReturnCorrelationSameAsRequestId()
        {
            var message = new HttpRequestMessage(HttpMethod.Get, "/Patient");

            using HttpResponseMessage response = await _client.HttpClient.SendAsync(message);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(response.Headers.GetValues(KnownHeaders.RequestId), response.Headers.GetValues(KnownHeaders.CorrelationId));
        }
    }
}
