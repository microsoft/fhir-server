// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Mode
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class SdkModeRuntimeSurfaceTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private const string IdentifierSystem = "http://microsoft.com/fhir/ignixa-sdk-tests/sdk-mode-runtime-surface";

        private readonly TestFhirClient _client;

        public SdkModeRuntimeSurfaceTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenSelectedSdkMode_WhenRunningCrudSearchAndProjection_ThenRuntimeSurfaceSucceeds()
        {
            var patient = new Patient
            {
                Active = true,
                Identifier =
                {
                    new Identifier(IdentifierSystem, Guid.NewGuid().ToString()),
                },
                Name =
                {
                    new HumanName
                    {
                        Family = "SdkMode",
                        Given = new[] { "Runtime" },
                    },
                },
            };

            FhirResponse<Patient> createResponse = null;
            var deleteVerified = false;

            try
            {
                createResponse = await _client.CreateAsync(patient);
                Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

                using FhirResponse<Patient> readResponse = await _client.ReadAsync<Patient>(ResourceType.Patient, createResponse.Resource.Id);
                Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
                Assert.True(readResponse.Resource.Active);

                using FhirResponse<Bundle> searchResponse = await _client.SearchAsync(ResourceType.Patient, $"_id={createResponse.Resource.Id}&_elements=active");
                Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);

                Patient projectedPatient = searchResponse.Resource.Entry
                    .Select(entry => entry.Resource)
                    .OfType<Patient>()
                    .Single(resource => resource.Id == createResponse.Resource.Id);

                Assert.True(projectedPatient.Active);

                createResponse.Resource.Active = false;
                using FhirResponse<Patient> updateResponse = await _client.UpdateAsync(createResponse.Resource);
                Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
                Assert.False(updateResponse.Resource.Active);

                using FhirResponse deleteResponse = await _client.DeleteAsync(createResponse.Resource);
                Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
                deleteVerified = true;
            }
            finally
            {
                if (!deleteVerified && createResponse?.Resource?.Id != null)
                {
                    try
                    {
                        using FhirResponse deleteResponse = await _client.DeleteAsync(createResponse.Resource);
                    }
                    catch (FhirClientException)
                    {
                    }
                    finally
                    {
                        createResponse.Dispose();
                    }
                }
                else
                {
                    createResponse?.Dispose();
                }
            }
        }
    }
}
