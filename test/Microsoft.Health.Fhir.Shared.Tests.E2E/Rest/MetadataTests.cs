// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class MetadataTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public MetadataTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenInvalidFormatParameter_WhenGettingMetadata_TheServerShouldReturnNotAcceptable()
        {
            using FhirException ex = await Assert.ThrowsAsync<FhirException>(async () => await _client.ReadAsync<CapabilityStatement>("metadata?_format=blah"));
            Assert.Equal(HttpStatusCode.NotAcceptable, ex.StatusCode);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("")]
        [InlineData("1")]
        [InlineData("0")]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenInvalidPrettyParameter_WhenGettingMetadata_TheServerShouldReturnBadRequest(string value)
        {
            using FhirException ex = await Assert.ThrowsAsync<FhirException>(async () => await _client.ReadAsync<CapabilityStatement>($"metadata?_pretty={value}"));
            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }
    }
}
