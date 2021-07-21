// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Search;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class EverythingOperationTests : SearchTestsBase<EverythingOperationTestFixture>
    {
        public EverythingOperationTests(EverythingOperationTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
        public async Task GivenAPatientEverythingOperationWithId_WhenSearched_ThenResourcesInScopeShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything";

            FhirResponse<Bundle> firstBundle = await Client.SearchAsync(searchUrl);
            ValidateBundle(firstBundle, Fixture.Patient, Fixture.Organization);

            var nextLink = firstBundle.Resource.NextLink.ToString();
            FhirResponse<Bundle> secondBundle = await Client.SearchAsync(nextLink);
            ValidateBundle(secondBundle, Fixture.Observation, Fixture.Appointment, Fixture.Encounter);

            nextLink = secondBundle.Resource.NextLink.ToString();
            FhirResponse<Bundle> thirdBundle = await Client.SearchAsync(nextLink);
            ValidateBundle(thirdBundle, Fixture.Device);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
        public async Task GivenAPatientEverythingOperationWithNonExistentId_WhenSearched_ThenResourcesInScopeShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.NonExistentPatient.Id}/$everything";

            await ExecuteAndValidateBundle(searchUrl, true, 1, Fixture.ObservationOfNonExistentPatient, Fixture.DeviceOfNonExistentPatient);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
        public async Task GivenAEverythingOperationWithUnsupportedType_WhenSearched_ThenNotFoundShouldBeReturned()
        {
            string searchUrl = "Observation/bar/$everything";

            using FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.SearchAsync(searchUrl));

            Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
        public async Task GivenTypeSpecified_WhenAllValid_ThenResourcesOfValidTypesShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?_type=Patient,Observation";

            await ExecuteAndValidateBundle(searchUrl, true, 1, Fixture.Patient, Fixture.Observation);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
        public async Task GivenTypeSpecified_WhenAllInvalid_ThenAnEmptyBundleShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?_type=foo";

            await ExecuteAndValidateBundle(searchUrl);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
        public async Task GivenTypeSpecified_WhenSomeInvalid_ThenResourcesOfValidTypesShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?_type=Patient,Device,foo";

            await ExecuteAndValidateBundle(searchUrl, true, 1, Fixture.Patient, Fixture.Device);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
        public async Task GivenStartOrEndSpecified_WhenSearched_ThenResourcesOfSpecifiedRangeShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?end=2010";

            await ExecuteAndValidateBundle(searchUrl, true, 2, Fixture.Patient, Fixture.Organization, Fixture.Appointment);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
        public async Task GivenSinceSpecified_WhenSearched_ThenResourcesOfSpecifiedRangeShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?_since=3000";

            await ExecuteAndValidateBundle(searchUrl);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("Zm9v")]
        [InlineData("eyJQaGFzZSI6MSwgIkludGVybmFsQ29udGludWF0aW9uVG9rZW4iOiAiWm05diJ9")]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
        public async Task GivenContinuationTokenSpecified_WhenInvalid_ThenBadRequestShouldBeReturned(string continuationToken)
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?ct={continuationToken}";

            using FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.SearchAsync(searchUrl));

            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
        public async Task GivenMultipleInputParametersSpecified_WhenAllValid_ThenResourcesOfSpecifiedRangeShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?_type=Patient,Observation,Encounter&start=2010&_since=2010";

            await ExecuteAndValidateBundle(searchUrl, true, 1, Fixture.Patient, Fixture.Observation);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
        public async Task GivenMultipleInputParametersSpecified_WhenSomeInvalid_ThenInvalidInputParametersDoNotTakeEffect()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?_type=Patient,Observation,Encounter&start=2010&_since=2010&foo=bar";

            await ExecuteAndValidateBundle(searchUrl, true, 1, Fixture.Patient, Fixture.Observation);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        public async Task GivenAEverythingOperationWithSqlServer_WhenSearched_ThenMethodNotAllowedShouldBeReturned()
        {
            string searchUrl = "Patient/bar/$everything";

            using FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.SearchAsync(searchUrl));

            Assert.Equal(HttpStatusCode.MethodNotAllowed, ex.StatusCode);
        }
    }
}
