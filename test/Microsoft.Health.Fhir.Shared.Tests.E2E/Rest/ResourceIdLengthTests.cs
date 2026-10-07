// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class ResourceIdLengthTests :
        IClassFixture<HttpIntegrationTestFixture<ResourceIdLengthTests.StandardIdStartup>>,
        IClassFixture<HttpIntegrationTestFixture<ResourceIdLengthTests.LongIdStartup>>
    {
        private readonly HttpIntegrationTestFixture<StandardIdStartup> _standardFixture;
        private readonly HttpIntegrationTestFixture<LongIdStartup> _longFixture;

        public ResourceIdLengthTests(
            HttpIntegrationTestFixture<StandardIdStartup> standardFixture,
            HttpIntegrationTestFixture<LongIdStartup> longFixture)
        {
            _standardFixture = standardFixture;
            _longFixture = longFixture;
        }

        [Theory]
        [InlineData(false, 64, true)]
        [InlineData(false, 65, false)]
        [InlineData(false, 128, false)]
        [InlineData(true, 64, true)]
        [InlineData(true, 65, true)]
        [InlineData(true, 128, true)]
        [InlineData(true, 129, false)]
        public async Task GivenTheLongResourceIdsFlag_WhenUsingTheHttpApi_ThenTheSelectedLimitIsApplied(bool useLongResourceIds, int length, bool valid)
        {
            // Arrange
            Assert.SkipWhen(
                !_standardFixture.IsUsingInProcTestServer || !_longFixture.IsUsingInProcTestServer,
                "Requires in-process servers with distinct resource id configurations.");
            HttpClient client = useLongResourceIds ? _longFixture.HttpClient : _standardFixture.HttpClient;
            string id = Guid.NewGuid().ToString("N") + new string('a', length - 32);
            string path = $"Patient/{id}";
            using var content = new StringContent($$"""{"resourceType":"Patient","id":"{{id}}","active":true}""", Encoding.UTF8, "application/fhir+json");

            // Act
            using HttpResponseMessage write = await client.PutAsync(path, content);
            using HttpResponseMessage read = await client.GetAsync(path);

            // Assert
            if (valid)
            {
                Assert.Equal(HttpStatusCode.Created, write.StatusCode);
                Assert.Equal(HttpStatusCode.OK, read.StatusCode);
                Assert.Equal(id, JsonNode.Parse(await read.Content.ReadAsStringAsync())["id"].GetValue<string>());

                using HttpResponseMessage search = await client.GetAsync($"Patient?_id={id}");
                Assert.Equal(HttpStatusCode.OK, search.StatusCode);
                JsonNode bundle = JsonNode.Parse(await search.Content.ReadAsStringAsync());
                Assert.Equal(id, Assert.Single(bundle["entry"].AsArray())["resource"]["id"].GetValue<string>());

                using HttpResponseMessage delete = await client.DeleteAsync(path);
                Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
                using HttpResponseMessage deletedRead = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.Gone, deletedRead.StatusCode);
            }
            else
            {
                Assert.Equal(HttpStatusCode.BadRequest, write.StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, read.StatusCode);
                using HttpResponseMessage delete = await client.DeleteAsync(path);
                Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
            }
        }

        [RequiresIsolatedDatabase]
        public class StandardIdStartup : StartupBaseForCustomProviders
        {
            public StandardIdStartup(IConfiguration configuration)
                : base(new ConfigurationBuilder()
                    .AddConfiguration(configuration)
                    .AddInMemoryCollection(new Dictionary<string, string> { ["FhirServer:CoreFeatures:UseLongResourceIds"] = "false" })
                    .Build())
            {
            }
        }

        [RequiresIsolatedDatabase]
        public class LongIdStartup : StartupBaseForCustomProviders
        {
            public LongIdStartup(IConfiguration configuration)
                : base(new ConfigurationBuilder()
                    .AddConfiguration(configuration)
                    .AddInMemoryCollection(new Dictionary<string, string> { ["FhirServer:CoreFeatures:UseLongResourceIds"] = "true" })
                    .Build())
            {
            }
        }
    }
}
