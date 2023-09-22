// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;
using System.Reflection;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json.Linq;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Provides R4 specific tests.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Conformance)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public partial class BinaryTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public BinaryTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        public async Task GivenCurrentTestAssembly_WhenConnectingToE2EService_EnsureTestsAndServiceAreRunningTheSameFhirVersion()
        {
            HttpResponseMessage response = await _client.HttpClient.GetAsync("metadata");

            string json = await response.Content.ReadAsStringAsync();
            JObject jsonObj = JObject.Parse(json);

            string currentServiceVersion = jsonObj["software"]["version"].ToString();

            Version currentVersion = Assembly.GetAssembly(typeof(BinaryTests)).GetName().Version;
            string currentE2ETestVersion = $"{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}";

            Assert.True(
                string.Equals(currentServiceVersion, currentE2ETestVersion, System.StringComparison.OrdinalIgnoreCase),
                userMessage: $"The current E2E test version ({currentE2ETestVersion}) is different than the one running in the service under test ({currentServiceVersion}).");
        }
    }
}
