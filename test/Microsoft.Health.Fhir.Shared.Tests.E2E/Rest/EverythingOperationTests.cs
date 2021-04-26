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

            await ExecuteAndValidateBundle(searchUrl, true, 4, Fixture.Patient, Fixture.Organization, Fixture.Device, Fixture.Observation, Fixture.Appointment, Fixture.Encounter);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
        public async Task GivenAPatientEverythingOperationWithNonExistentId_WhenSearched_ThenResourcesInScopeShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.NonExistentPatient.Id}/$everything";

            await ExecuteAndValidateBundle(searchUrl, true, 4, Fixture.DeviceOfNonExistentPatient, Fixture.ObservationOfNonExistentPatient);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
        public async Task GivenAEverythingOperationWithUnsupportedType_WhenSearched_ThenBadRequestShouldBeReturned()
        {
            string searchUrl = "Observation/bar/$everything";

            using FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.SearchAsync(searchUrl));

            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
        public async Task GivenTypeSpecified_WhenAllValid_ThenResourcesOfValidTypesShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?_type=Patient,Observation";

            await ExecuteAndValidateBundle(searchUrl, true, 4, Fixture.Patient, Fixture.Observation);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
        public async Task GivenTypeSpecified_WhenAllInvalid_ThenOperationOutcomeShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?_type=foo";
            string[] expectedDiagnosticsOneWrongType = { string.Format(Core.Resources.InvalidTypeParameter, "'foo'") };
            OperationOutcome.IssueType[] expectedCodeTypes = { OperationOutcome.IssueType.NotSupported };
            OperationOutcome.IssueSeverity[] expectedIssueSeverities = { OperationOutcome.IssueSeverity.Warning };

            Bundle bundle = await Client.SearchAsync(searchUrl);
            Assert.Contains("_type=foo", bundle.Link[0].Url);
            OperationOutcome outcome = GetAndValidateOperationOutcome(bundle);
            ValidateOperationOutcome(expectedDiagnosticsOneWrongType, expectedIssueSeverities, expectedCodeTypes, outcome);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
        public async Task GivenTypeSpecified_WhenSomeInvalid_ThenResourcesOfValidTypesAndProperOperationOutcomeShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?_type=Patient,Device,foo";
            string[] expectedDiagnostics = { string.Format(Core.Resources.InvalidTypeParameter, "'foo'") };
            OperationOutcome.IssueType[] expectedCodeTypes = { OperationOutcome.IssueType.NotSupported };
            OperationOutcome.IssueSeverity[] expectedIssueSeverities = { OperationOutcome.IssueSeverity.Warning };

            Bundle bundle = await Client.SearchAsync(searchUrl);
            Assert.Contains("_type=Patient,Device,foo", bundle.Link[0].Url);
            OperationOutcome outcome = GetAndValidateOperationOutcome(bundle);
            ValidateBundle(bundle, Fixture.Patient, Fixture.Device, outcome);
            ValidateOperationOutcome(expectedDiagnostics, expectedIssueSeverities, expectedCodeTypes, outcome);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
        public async Task GivenStartOrEndSpecified_WhenSearched_ThenResourcesOfSpecifiedRangeShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?end=2010";

            await ExecuteAndValidateBundle(searchUrl, true, 4, Fixture.Patient, Fixture.Organization, Fixture.Device, Fixture.Appointment, Fixture.Encounter);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
        public async Task GivenSinceSpecified_WhenSearched_ThenResourcesOfSpecifiedRangeShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?_since=3000";

            await ExecuteAndValidateBundle(searchUrl);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
        public async Task GivenMultipleInputParametersSpecified_WhenAllValid_ThenResourcesOfSpecifiedRangeShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?_type=Patient,Observation,Encounter&start=2010&_since=2010";

            await ExecuteAndValidateBundle(searchUrl, true, 4, Fixture.Patient, Fixture.Observation);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
        public async Task GivenMultipleInputParametersSpecified_WhenSomeInvalid_ThenInvalidInputParametersDoNotTakeEffect()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?_type=Patient,Observation,Encounter&start=2010&_since=2010&foo=bar";

            await ExecuteAndValidateBundle(searchUrl, true, 4, Fixture.Patient, Fixture.Observation);
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
