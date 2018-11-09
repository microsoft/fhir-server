// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Web;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class CompartmentTests : SearchTestsBase<HttpIntegrationTestFixture<Startup>>
    {
        private readonly Patient _patient;
        private readonly Observation _observation;
        private readonly Device _device;
        private readonly DeviceComponent _deviceComponent;
        private readonly Encounter _encounter;
        private readonly Condition _condition;

        public CompartmentTests(HttpIntegrationTestFixture<Startup> fixture)
            : base(fixture)
        {
            // Delete all relevant resources before starting the test.
            Client.DeleteAllResources(ResourceType.Device, "_id=d1").Wait();
            Client.DeleteAllResources(ResourceType.DeviceComponent, "_id=example").Wait();
            Client.DeleteAllResources(ResourceType.Patient, "_id=f001").Wait();
            Client.DeleteAllResources(ResourceType.Observation, "_id=f001").Wait();
            Client.DeleteAllResources(ResourceType.Encounter, "_id=f002").Wait();
            Client.DeleteAllResources(ResourceType.Condition, "_id=f001").Wait();

            // Create various resources.
            _patient = Client.UpdateAsync(Samples.GetJsonSample<Patient>("Patient-f001")).Result;
            _observation = Client.UpdateAsync(Samples.GetJsonSample<Observation>("Observation-For-Patient-f001")).Result;
            _encounter = Client.UpdateAsync(Samples.GetJsonSample<Encounter>("Encounter-For-Patient-f001")).Result;
            _device = Client.UpdateAsync(Samples.GetJsonSample<Device>("Device-d1")).Result;
            _deviceComponent = Client.UpdateAsync(Samples.GetJsonSample<DeviceComponent>("DeviceComponent-For-Device-d1")).Result;
            _condition = Client.UpdateAsync(Samples.GetJsonSample<Condition>("Condition-For-Patient-f001")).Result;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenADeviceCompartment_WhenRetrievingDeviceComponents_ThenOnlyResourcesMatchingCompartmentShouldBeReturned()
        {
            Bundle bundle = await Client.SearchAsync("Device/d1/DeviceComponent");
            ValidateBundle(bundle, new DeviceComponent[] { _deviceComponent });
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientCompartment_WhenRetrievingObservation_ThenOnlyResourcesMatchingCompartmentShouldBeReturned()
        {
            Bundle bundle = await Client.SearchAsync("Patient/f001/Observation");
            ValidateBundle(bundle, new Observation[] { _observation });
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientCompartment_WhenRetrievingResourcesWithAWildCard_ThenAllResourcesMatchingCompartmentShouldBeReturned()
        {
            Bundle bundle = await Client.SearchAsync("Patient/f001/*");
            ValidateBundle(bundle, new Resource[] { _observation, _encounter, _condition });
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientCompartment_WhenRetrievingResourcesWithAWildCardWithSearchByType_ThenAllResourcesMatchingCompartmentAndTypeSearchedShouldBeReturned()
        {
            Bundle bundle = await Client.SearchAsync("Patient/f001/*?_type=Observation");
            ValidateBundle(bundle, new Resource[] { _observation });
            bundle = await Client.SearchAsync("Patient/f001/*?_type=Encounter");
            ValidateBundle(bundle, new Resource[] { _encounter });
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientCompartment_WhenRetrievingResourcesWithAWildCardWithSearchWithNoMatchingValue_ThenNoResourcesMatchingCompartmentSearchedShouldBeReturned()
        {
            Bundle bundle = await Client.SearchAsync("Patient/f001/*?_type=foo");
            Assert.Empty(bundle.Entry);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientCompartment_WhenSearchingResourcesWithAMatchingValue_ThenResourcesMatchingCompartmentSearchedShouldBeReturned()
        {
            Bundle bundle = await Client.SearchAsync("Patient/f001/Observation?performer=Practitioner/f005");
            ValidateBundle(bundle, new Resource[] { _observation });
        }
    }
}
