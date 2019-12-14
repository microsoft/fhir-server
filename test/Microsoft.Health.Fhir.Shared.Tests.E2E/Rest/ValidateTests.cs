// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.Category, Categories.Validate)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class ValidateTests
    {
        private readonly HttpClient _client;
        private readonly JsonSerializer _serializer;

        public ValidateTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.HttpClient;
            _serializer = JsonSerializer.Create();
        }

        [Theory]
        [InlineData("Patient/$validate", "")]
        [InlineData("Observation/$validate", "")]
        public async Task GivenAValidateRequest_WhenTheResourceIsValid_ThenAnOkMessageIsReturned(string path, string payload)
        {
            HttpResponseMessage response = await _client.SendAsync(GenerateValidateMessage(path, payload));

            var contentStream = await response.Content.ReadAsStreamAsync();
            var resource = _serializer.Deserialize(new JsonTextReader(new StreamReader(contentStream)));


        }

        [Theory]
        [InlineData("Patient/$validate", "")]
        [InlineData("Observation/$validate", "")]
        public async Task GivenAValidateRequest_WhenTheResourceIsInvalid_ThenADetailedErrorIsReturned(string path, string payload)
        {
            HttpResponseMessage response = await _client.SendAsync(GenerateValidateMessage(path, payload));
        }

        private HttpRequestMessage GenerateValidateMessage(string path, string body)
        {
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
            };

            request.RequestUri = new Uri(_client.BaseAddress, path);
            request.Content = new StringContent(body, Encoding.UTF8, ContentType.JSON_CONTENT_HEADER);

            return request;
        }
    }
}
