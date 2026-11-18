// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Web;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public sealed class RemoteResourceIdFixture : IAsyncLifetime
    {
        private readonly TestFhirServerFactory _factory;

        public RemoteResourceIdFixture(TestFhirServerFactory factory)
        {
            _factory = factory;
        }

        public HttpClient HttpClient { get; private set; }

        public async ValueTask InitializeAsync()
        {
            string configured = Environment.GetEnvironmentVariable("ResourceIdProfile");
            if (configured == null)
            {
                return;
            }

            Assert.True(configured == nameof(ResourceIdProfile.Standard64) || configured == nameof(ResourceIdProfile.Long128), "ResourceIdProfile must be Standard64 or Long128.");
            string url = Environment.GetEnvironmentVariable($"TestEnvironmentUrl{Constants.TestEnvironmentVariableVersionSuffix}_Sql");
            Assert.True(Uri.TryCreate(url, UriKind.Absolute, out Uri address) && address.Scheme == "https", "A remote profile requires an explicit version-specific HTTPS SQL endpoint.");
            Assert.True(Uri.TryCreate(Environment.GetEnvironmentVariable("ResourceIdExpectedUrl"), UriKind.Absolute, out Uri expectedAddress), "The selected endpoint URL must come from remote setup.");
            Assert.Equal(expectedAddress.AbsoluteUri.TrimEnd('/'), address.AbsoluteUri.TrimEnd('/'));
            TestFhirServer server = await _factory.GetTestFhirServerAsync(DataStore.SqlServer, typeof(Startup));
            Assert.IsType<RemoteTestFhirServer>(server);
            Assert.True(server.SecurityEnabled, "The selected remote endpoint must retain authentication.");
            Assert.Equal(address.AbsoluteUri.TrimEnd('/'), server.BaseAddress.AbsoluteUri.TrimEnd('/'));
            HttpClient = server.GetTestFhirClient(ResourceFormat.Json).HttpClient;

            using HttpResponseMessage health = await HttpClient.GetAsync("health/check");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            JsonNode report = JsonNode.Parse(await health.Content.ReadAsStringAsync());
            Assert.Equal("Healthy", report["overallStatus"].GetValue<string>());
            Assert.Contains(report["details"].AsArray(), d => d["name"].GetValue<string>() == "StorageInitializedHealthCheck" && d["status"].GetValue<string>() == "Healthy");

            using HttpResponseMessage authenticated = await HttpClient.GetAsync("Patient?_count=0");
            Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);

            using HttpResponseMessage metadata = await HttpClient.GetAsync("metadata");
            Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
            JsonNode capability = JsonNode.Parse(await metadata.Content.ReadAsStringAsync());
            Assert.Equal(ModelInfo.Version, capability["fhirVersion"].GetValue<string>());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
