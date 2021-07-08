// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Search;
using Xunit;

namespace Microsoft.Health.Fhir.R4.Tests.E2E.GraphQl
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class GraphQlTests : SearchTestsBase<HttpIntegrationTestFixture>
    {
        public GraphQlTests(HttpIntegrationTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        public async void GivenQueryByGet_AskingForPatientSchema_ThenPatientSchemaShouldBeReturned()
        {
            var baseUrl = "graphql/graphql?sdl";
            using var message = new HttpRequestMessage(HttpMethod.Get, baseUrl);
            message.Headers.Host = "patient.graphql";

            using HttpResponseMessage response = await Client.HttpClient.SendAsync(message);

            response.EnsureSuccessStatusCode();
        }

        [Fact]
        public async void GivenQueryByGet_AskingForTypesSchema_ThenTypesSchemaShouldBeReturned()
        {
            var baseUrl = "graphql/graphql?sdl";
            using var message = new HttpRequestMessage(HttpMethod.Get, baseUrl);
            message.Headers.Host = "types.graphql";

            using HttpResponseMessage response = await Client.HttpClient.SendAsync(message);

            response.EnsureSuccessStatusCode();
        }
    }
}
