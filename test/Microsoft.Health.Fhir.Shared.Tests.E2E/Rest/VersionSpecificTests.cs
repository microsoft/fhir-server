// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
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

        public VersionSpecificTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.FhirClient;
        }

        private async Task TestCapabilityStatementFhirVersion(string expectedVersion)
        {
            CapabilityStatement capabilityStatement = await _client.ReadAsync<CapabilityStatement>("metadata");

            Assert.NotNull(capabilityStatement.FhirVersionElement);
            Assert.Equal(expectedVersion, capabilityStatement.FhirVersionElement.ObjectValue);
        }
    }
}
