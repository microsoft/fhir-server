// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Smart
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    [FhirStorageTestsFixtureArgumentSets(DataStore.All)]

    public class SmartSearchTests : IClassFixture<FhirStorageTestsFixture>, IAsyncLifetime
    {
        private readonly FhirStorageTestsFixture _fixture;
        private readonly IFhirStorageTestHelper _testHelper;
        private IFhirOperationDataStore _fhirOperationDataStore;
        private IScoped<IFhirDataStore> _scopedDataStore;
        private IFhirStorageTestHelper _fhirStorageTestHelper;
        private SearchParameterDefinitionManager _searchParameterDefinitionManager;
        private ITypedElementToSearchValueConverterManager _typedElementToSearchValueConverterManager;
        private ISearchIndexer _searchIndexer;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();
        private ISupportedSearchParameterDefinitionManager _supportedSearchParameterDefinitionManager;
        private SearchParameterStatusManager _searchParameterStatusManager;

        private IScoped<ISearchService> _searchService;

        private RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly IDataStoreSearchParameterValidator _dataStoreSearchParameterValidator = Substitute.For<IDataStoreSearchParameterValidator>();

        public SmartSearchTests(FhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            _testHelper = _fixture.TestHelper;
        }

        public async Task InitializeAsync()
        {
            if (ModelInfoProvider.Instance.Version == FhirSpecification.R4 ||
                ModelInfoProvider.Instance.Version == FhirSpecification.R4B)
            {
                _dataStoreSearchParameterValidator.ValidateSearchParameter(default, out Arg.Any<string>()).ReturnsForAnyArgs(x =>
                {
                    x[1] = null;
                    return true;
                });

                _searchParameterSupportResolver.IsSearchParameterSupported(Arg.Any<SearchParameterInfo>()).Returns((true, false));

                _contextAccessor = _fixture.FhirRequestContextAccessor;

                _fhirOperationDataStore = _fixture.OperationDataStore;
                _fhirStorageTestHelper = _fixture.TestHelper;
                _scopedDataStore = _fixture.DataStore.CreateMockScope();

                _searchParameterDefinitionManager = _fixture.SearchParameterDefinitionManager;
                _supportedSearchParameterDefinitionManager = _fixture.SupportedSearchParameterDefinitionManager;

                _typedElementToSearchValueConverterManager = await CreateFhirTypedElementToSearchValueConverterManagerAsync();

                _searchIndexer = new TypedElementSearchIndexer(
                    _supportedSearchParameterDefinitionManager,
                    _typedElementToSearchValueConverterManager,
                    Substitute.For<IReferenceToElementResolver>(),
                    ModelInfoProvider.Instance,
                    NullLogger<TypedElementSearchIndexer>.Instance);

                ResourceWrapperFactory wrapperFactory = Mock.TypeWithArguments<ResourceWrapperFactory>(
                    new RawResourceFactory(new FhirJsonSerializer()),
                    new FhirRequestContextAccessor(),
                    _searchIndexer,
                    _searchParameterDefinitionManager,
                    Deserializers.ResourceDeserializer);

                _searchParameterStatusManager = _fixture.SearchParameterStatusManager;

                _searchService = _fixture.SearchService.CreateMockScope();

                _contextAccessor = _fixture.FhirRequestContextAccessor;

                var smartBundle = Samples.GetJsonSample<Bundle>("SmartPatientA");
                foreach (var entry in smartBundle.Entry)
                {
                    await PutResource(entry.Resource);
                }

                smartBundle = Samples.GetJsonSample<Bundle>("SmartPatientB");
                foreach (var entry in smartBundle.Entry)
                {
                    await PutResource(entry.Resource);
                }

                smartBundle = Samples.GetJsonSample<Bundle>("SmartPatientC");
                foreach (var entry in smartBundle.Entry)
                {
                    await PutResource(entry.Resource);
                }

                smartBundle = Samples.GetJsonSample<Bundle>("SmartCommon");
                foreach (var entry in smartBundle.Entry)
                {
                    await PutResource(entry.Resource);
                }

                await PutResource(Samples.GetJsonSample<Medication>("Medication"));
                await PutResource(Samples.GetJsonSample<Organization>("Organization"));
                await PutResource(Samples.GetJsonSample<Location>("Location-example-hq"));
            }
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        [SkippableFact]
        public async Task GivenScopesWithReadForAllResources_WhenRevIncludeObservations_PatientAndObservationReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // try to query both the Patient resource and the Observation resource using revinclude
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_id", "smart-patient-A"));
            query.Add(new Tuple<string, string>("_revinclude", "Observation:subject"));

            var scopeRestriction = new ScopeRestriction(KnownResourceTypes.All, Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);

            // assert that only the patient is returned
            Assert.Collection<SearchResultEntry>(
                results.Results,
                e => Assert.Equal("Patient", e.Resource.ResourceTypeName),
                e2 => Assert.Equal("Observation", e2.Resource.ResourceTypeName),
                e3 => Assert.Equal("Observation", e3.Resource.ResourceTypeName));
        }

        [SkippableFact]
        public async Task GivenScopesForPatientAndObservation_WhenRevIncludeObservations_PatientAndObservationReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // try to query both the Patient resource and the Observation resource using revinclude
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_id", "smart-patient-A"));
            query.Add(new Tuple<string, string>("_revinclude", "Observation:subject"));

            var scopeRestriction = new ScopeRestriction(KnownResourceTypes.Patient, Core.Features.Security.DataActions.Read, "patient");
            var scopeRestriction2 = new ScopeRestriction(KnownResourceTypes.Observation, Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction, scopeRestriction2 });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);

            // assert that only the patient is returned
            Assert.Collection<SearchResultEntry>(
                results.Results,
                e => Assert.Equal("Patient", e.Resource.ResourceTypeName),
                e2 => Assert.Equal("Observation", e2.Resource.ResourceTypeName),
                e3 => Assert.Equal("Observation", e3.Resource.ResourceTypeName));
        }

        [SkippableFact]
        public async Task GivenScopesForPatientAndObservation_WhenIncludeObservations_PatientAndObservationReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // try to query both the Patient resource and the Observation resource using revinclude
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_id", "smart-observation-A1"));
            query.Add(new Tuple<string, string>("_include", "Observation:subject"));

            var scopeRestriction = new ScopeRestriction(KnownResourceTypes.Patient, Core.Features.Security.DataActions.Read, "patient");
            var scopeRestriction2 = new ScopeRestriction(KnownResourceTypes.Observation, Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction, scopeRestriction2 });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);

            // assert that only the patient is returned
            Assert.Collection<SearchResultEntry>(
                results.Results,
                e => Assert.Equal("Observation", e.Resource.ResourceTypeName),
                e2 => Assert.Equal("Patient", e2.Resource.ResourceTypeName));
        }

        [SkippableFact]
        public async Task GivenScopesForObservation_WhenIncludePatient_OnlyObservationResourcesReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // try to query both the Patient resource and the Observation resource using revinclude
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_id", "smart-observation-A1"));
            query.Add(new Tuple<string, string>("_include", "Observation:subject"));

            var scopeRestriction = new ScopeRestriction(KnownResourceTypes.Observation, Core.Features.Security.DataActions.Read, "patient");
            var scopeRestriction2 = new ScopeRestriction(KnownResourceTypes.Medication, Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction, scopeRestriction2 });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);

            // assert that only the patient is returned
            Assert.DoesNotContain(results.Results, x => x.Resource.ResourceTypeName == "Patient");
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Observation");
        }

        [SkippableFact]
        public async Task GivenScopesWithReadForPatient_WhenRevIncludeObservations_OnlyPatientResourcesReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // try to query both the Patient resource and the Observation resource using revinclude
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_id", "smart-patient-A"));
            query.Add(new Tuple<string, string>("_revinclude", "Observation:subject"));

            var scopeRestriction = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);

            // assert that only the patient is returned
            Assert.DoesNotContain(results.Results, x => x.Resource.ResourceTypeName == "Observation");
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Patient");
        }

        [SkippableFact]
        public async Task GivenScopesWithReadForObservation_WhenChainedSearchWithPatientName_ThrowsInvalidSearchException()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // Query the Observation resources where they refer to Patients with the name "SMARTGivenName1"
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("subject:Patient.name", "SMARTGivenName1"));

            var scopeRestriction = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            await Assert.ThrowsAsync<InvalidSearchOperationException>(() =>
                _searchService.Value.SearchAsync("Observation", query, CancellationToken.None));
        }

        [SkippableFact]
        public async Task GivenScopesWithReadForObservationAndPatient_WhenChainedSearchWithPatientName_ThenObservationResourceReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // Query the Observation resources where they refer to Patients with the name "SMARTGivenName1"
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("subject:Patient.name", "SMARTGivenName1"));

            var scopeRestriction = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Read, "patient");
            var scopeRestriction2 = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction, scopeRestriction2 });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var result = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);

            Assert.Collection(
                result.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r2 => Assert.Equal("smart-observation-A2", r2.Resource.ResourceId));
        }

        [SkippableFact]
        public async Task GivenScopesWithReadForPatient_WhenRevChainedSearchWithObservationCode_ThrowsInvalidSearchException()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // Query Patient resources where there are Observations referring to the Patient, which have the code "4548-4"
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_has:Observation:subject:code", "4548-4"));

            var scopeRestriction = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            await Assert.ThrowsAsync<InvalidSearchOperationException>(
                () => _searchService.Value.SearchAsync("Patient", query, CancellationToken.None));
        }

        [SkippableFact]
        public async Task GivenScopesWithReadForPatient_WhenRevChainedSearchWithObservationPatientCode_ThrowsInvalidSearchException()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // Query Patient resources where there are Observations referring to the Patient, which have the code "4548-4"
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_has:Observation:patient:code", "4548-4"));

            var scopeRestriction = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            await Assert.ThrowsAsync<InvalidSearchOperationException>(() =>
                _searchService.Value.SearchAsync("Patient", query, CancellationToken.None));
        }

        [SkippableFact]
        public async Task GivenScopesWithReadForAllResources_WhenRevChainedSearchWithObservationCode_PatientResourcesReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // Query Patient resources where there are Observations referring to the Patient, which have the code "4548-4"
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_has:Observation:subject:code", "4548-4"));

            var scopeRestriction = new ScopeRestriction(KnownResourceTypes.All, Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);

            Assert.Collection(
                results.Results,
                r => Assert.True(r.Resource.ResourceId == "smart-patient-A"));
        }

        [SkippableFact]
        public async Task GivenScopesWithReadForPatient_WhenObservationRequested_NoResultsReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var query = new List<Tuple<string, string>>();

            var scopeRestriction = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);

            Assert.Empty(results.Results);
        }

        [SkippableFact]
        public async Task GivenFhirUserClaimPatient_WhenPatientInOtherCompartmentRequestedUsingSearch_NoResultsReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_id", "smart-patient-B"));

            var scopeRestriction = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);

            Assert.Empty(results.Results);
        }

        [SkippableFact]
        public async Task GivenFhirUserClaimPatient_WhenPatientInOtherCompartmentRequested_NoResultsReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var scopeRestriction = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => _fixture.GetResourceHandler.Handle(new Core.Messages.Get.GetResourceRequest(new ResourceKey("Patient", "smart-patient-B")), CancellationToken.None));
        }

        [SkippableFact]
        public async Task GivenFhirUserClaimPatient_WhenPatientInSameCompartmentRequested_ResourceIsReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_id", "smart-patient-A"));

            var scopeRestriction = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);

            Assert.Collection(
                results.Results,
                r => Assert.True(r.Resource.ResourceId == "smart-patient-A"));
        }

        [SkippableFact]
        public async Task GivenFhirUserClaimPractitioner_WhenPatientInSameCompartmentRequested_ResourceIsReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_id", "smart-patient-A"));

            var scopeRestriction = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Read, "user");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-practitioner-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Practitioner";

            var results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);

            Assert.Collection(
                results.Results,
                r => Assert.True(r.Resource.ResourceId == "smart-patient-A"));
        }

        [SkippableFact]
        public async Task GivenFhirUserClaimPractitioner_WhenPatientInOtherCompartmentRequested_NoResourceIsReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_id", "smart-patient-C"));

            var scopeRestriction = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Read, "user");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-practitioner-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Practitioner";

            var results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);

            Assert.Empty(results.Results);
        }

        [SkippableFact]
        public async Task GivenFhirUserClaimPatient_WhenAllResourcesRequested_UniversalResourcesAlsoReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_count", "100"));

            var scopeRestriction = new ScopeRestriction("all", Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);

            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.Medication);
            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.Location);
            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.Practitioner);
            Assert.Equal(25, results.Results.Count());
        }

        [SkippableFact]
        public async Task GivenFhirUserClaimPatient_WhenAllPractitionersRequested_PractitionersReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_count", "100"));

            var scopeRestriction = new ScopeRestriction("all", Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var results = await _searchService.Value.SearchAsync("Practitioner", query, CancellationToken.None);

            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.Practitioner);
            Assert.Equal(3, results.Results.Count());
        }

        [SkippableFact]
        public async Task GivenFhirUserClaimSystem_WhenAllResourcesRequested_ThenAllResourcesReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_count", "100"));

            var scopeRestriction = new ScopeRestriction("all", Core.Features.Security.DataActions.Read, "system");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = null;
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = null;

            var results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);

            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.Medication);
            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.Location);
            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.Practitioner);
            Assert.Equal(70, results.Results.Count());
        }

        private async Task<UpsertOutcome> PutResource(Resource resource)
        {
            ResourceElement resourceElement = resource.ToResourceElement();

            var rawResource = new RawResource(resource.ToJson(), FhirResourceFormat.Json, isMetaSet: false);
            var resourceRequest = new ResourceRequest(WebRequestMethods.Http.Put);
            var compartmentIndices = Substitute.For<CompartmentIndices>();
            var searchIndices = _searchIndexer.Extract(resourceElement);
            var wrapper = new ResourceWrapper(resourceElement, rawResource, resourceRequest, false, searchIndices, compartmentIndices, new List<KeyValuePair<string, string>>(), _searchParameterDefinitionManager.GetSearchParameterHashForResourceType("Patient"));
            wrapper.SearchParameterHash = "hash";

            return await _scopedDataStore.Value.UpsertAsync(new ResourceWrapperExtended(wrapper, true, true, null, false), CancellationToken.None);
        }

        private static async Task<FhirTypedElementToSearchValueConverterManager> CreateFhirTypedElementToSearchValueConverterManagerAsync()
        {
            var types = typeof(ITypedElementToSearchValueConverter)
                .Assembly
                .GetTypes()
                .Where(x => typeof(ITypedElementToSearchValueConverter).IsAssignableFrom(x) && !x.IsAbstract && !x.IsInterface);

            var referenceSearchValueParser = new ReferenceSearchValueParser(new FhirRequestContextAccessor());
            var codeSystemResolver = new CodeSystemResolver(ModelInfoProvider.Instance);
            await codeSystemResolver.StartAsync(CancellationToken.None);

            var fhirElementToSearchValueConverters = new List<ITypedElementToSearchValueConverter>();

            foreach (Type type in types)
            {
                // Filter out the extension converter because it will be added to the converter dictionary in the converter manager's constructor
                if (type.Name != nameof(FhirTypedElementToSearchValueConverterManager.ExtensionConverter))
                {
                    var x = (ITypedElementToSearchValueConverter)Mock.TypeWithArguments(type, referenceSearchValueParser, codeSystemResolver);
                    fhirElementToSearchValueConverters.Add(x);
                }
            }

            return new FhirTypedElementToSearchValueConverterManager(fhirElementToSearchValueConverters);
        }

        private void ConfigureFhirRequestContext(
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            ICollection<ScopeRestriction> scopes)
        {
            var accessControlContext = new AccessControlContext()
            {
                ApplyFineGrainedAccessControl = true,
            };

            foreach (var scope in scopes)
            {
                accessControlContext.AllowedResourceActions.Add(scope);
            }

            contextAccessor.RequestContext.AccessControlContext.Returns(accessControlContext);
        }
    }
}
