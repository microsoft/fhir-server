// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Web;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class CompartmentTests : SearchTestsBase<HttpIntegrationTestFixture<Startup>>, IAsyncLifetime
    {
        private Patient _patient;
        private Observation _observation;
        private Device _device;
        private DeviceComponent _deviceComponent;
        private Encounter _encounter;
        private Condition _condition;
        private readonly string _tagValue;
        private const string TagSearchParameterName = "_tag";
        private string _tagQueryString;

        public CompartmentTests(HttpIntegrationTestFixture<Startup> fixture)
            : base(fixture)
        {
            _tagValue = $"CompartmentTests_{Guid.NewGuid().ToString()}";
            _tagQueryString = $"{TagSearchParameterName}={_tagValue}";
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

        private async Task<T> CreateResourceAsync<T>(T resource)
            where T : Resource
        {
            (resource.Meta = resource.Meta ?? new Meta()).Tag.Add(new Coding(null, _tagValue));
            return await Client.CreateAsync(resource);
        }

        public async Task InitializeAsync()
        {
            // Create various resources.
            _patient = await CreateResourceAsync(Samples.GetJsonSample<Patient>("Patient-f001"));
            _observation = await CreateResourceAsync(Samples.GetJsonSample<Observation>("Observation-For-Patient-f001"));
            _encounter = await CreateResourceAsync(Samples.GetJsonSample<Encounter>("Encounter-For-Patient-f001"));
            _device = await CreateResourceAsync(Samples.GetJsonSample<Device>("Device-d1"));
            _deviceComponent = await CreateResourceAsync(Samples.GetJsonSample<DeviceComponent>("DeviceComponent-For-Device-d1"));
            _condition = await CreateResourceAsync(Samples.GetJsonSample<Condition>("Condition-For-Patient-f001"));
        }

        public async Task DisposeAsync()
        {
            // Delete all relevant resources before leaving the test.
            await Client.DeleteAllResources(ResourceType.Device, _tagQueryString);
            await Client.DeleteAllResources(ResourceType.DeviceComponent, _tagQueryString);
            await Client.DeleteAllResources(ResourceType.Patient, _tagQueryString);
            await Client.DeleteAllResources(ResourceType.Observation, _tagQueryString);
            await Client.DeleteAllResources(ResourceType.Encounter, _tagQueryString);
            await Client.DeleteAllResources(ResourceType.Condition, _tagQueryString);
        }
    }
}
