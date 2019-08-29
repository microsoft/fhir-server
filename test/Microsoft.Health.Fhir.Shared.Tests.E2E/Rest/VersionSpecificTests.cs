// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;
using FhirClient = Microsoft.Health.Fhir.Tests.E2E.Common.FhirClient;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Provides version specific tests.
    /// </summary>
    [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
    public partial class VersionSpecificTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly FhirClient _client;
        private readonly IModelInfoProvider _provider;

        public VersionSpecificTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.FhirClient;
            _provider = fixture.Provider;
        }

        private async Task TestCapabilityStatementFhirVersion(string expectedVersion)
        {
            CapabilityStatement capabilityStatement = await _client.ReadAsync<CapabilityStatement>("metadata");

            Assert.NotNull(capabilityStatement.FhirVersionElement);
            Assert.Equal(expectedVersion, capabilityStatement.FhirVersionElement.ObjectValue);
        }

        private void TestSupportedVersion(string expectedVersion)
        {
            var version = _provider.SupportedVersion.ToString();

            Assert.Equal(expectedVersion, version);
        }
    }
}
