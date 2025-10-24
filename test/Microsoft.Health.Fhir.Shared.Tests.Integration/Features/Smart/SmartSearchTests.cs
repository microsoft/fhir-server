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
using DotLiquid.Util;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
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
using Microsoft.Health.Fhir.Core.Features.Resources.Patch;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.Core;
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
                    await UpsertResource(entry.Resource);
                }

                smartBundle = Samples.GetJsonSample<Bundle>("SmartPatientB");
                foreach (var entry in smartBundle.Entry)
                {
                    await UpsertResource(entry.Resource);
                }

                smartBundle = Samples.GetJsonSample<Bundle>("SmartPatientC");
                foreach (var entry in smartBundle.Entry)
                {
                    await UpsertResource(entry.Resource);
                }

                smartBundle = Samples.GetJsonSample<Bundle>("SmartPatientD");
                foreach (var entry in smartBundle.Entry)
                {
                    await UpsertResource(entry.Resource);
                }

                smartBundle = Samples.GetJsonSample<Bundle>("SmartCommon");
                foreach (var entry in smartBundle.Entry)
                {
                    await UpsertResource(entry.Resource);
                }

                await UpsertResource(Samples.GetJsonSample<Medication>("Medication"));
                await UpsertResource(Samples.GetJsonSample<Organization>("Organization"));
                await UpsertResource(Samples.GetJsonSample<Location>("Location-example-hq"));
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

            // assert that only the patient and Observations are returned
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

            // assert that only the patient and Observations are returned
            Assert.Collection<SearchResultEntry>(
                results.Results,
                e => Assert.Equal("Patient", e.Resource.ResourceTypeName),
                e2 => Assert.Equal("Observation", e2.Resource.ResourceTypeName),
                e3 => Assert.Equal("Observation", e3.Resource.ResourceTypeName));
        }

        [SkippableFact]
        public async Task GivenScopesWithReadForPatient_WhenRevIncludeObservationsAndEncounter_OnlyPatientObservationsAndEncounterResourcesReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // try to query both the Patient resource and the Observation resource using revinclude
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_id", "smart-patient-A"));
            query.Add(new Tuple<string, string>("_revinclude", "Observation:subject"));
            query.Add(new Tuple<string, string>("_revinclude", "Encounter:subject"));

            var scopeRestriction = new ScopeRestriction(KnownResourceTypes.Patient, Core.Features.Security.DataActions.Read, "patient");
            var scopeRestriction2 = new ScopeRestriction(KnownResourceTypes.All, Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction, scopeRestriction2 });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);

            // assert that only Patient, Observations and Encounter are returned
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Observation");
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Patient");
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Encounter");
            Assert.DoesNotContain(results.Results, x => x.Resource.ResourceTypeName == "Appointment");
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
        public async Task GivenScopesWithReadForAllResource_WhenRevincludeWithWildCardRequest_ReturnsAllResourcesThatReferenceThePatient()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var scopeRestriction1 = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Read, "patient");
            var scopeRestriction2 = new ScopeRestriction("all", Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction1, scopeRestriction2 });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var query = new List<Tuple<string, string>>() { new Tuple<string, string>("_revinclude", "*:*"), new Tuple<string, string>("_id", "smart-patient-A") };
            var results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);

            // assert that different resources are returned
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Patient");
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Observation");
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Encounter");
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Appointment");
        }

        [SkippableFact]
        public async Task GivenScopesForPatientAndObservation_WhenRevincludeWithWildCardRequest_ReturnsOnlyPatientAndObservation()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var scopeRestriction1 = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Read, "patient");
            var scopeRestriction2 = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction1, scopeRestriction2 });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var query = new List<Tuple<string, string>>() { new Tuple<string, string>("_revinclude", "*:*"), new Tuple<string, string>("_id", "smart-patient-A") };
            var results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);

            // assert that only the patient and Observation is returned
            Assert.True(results.Results.Count() == 3);
            Assert.Collection<SearchResultEntry>(
                results.Results,
                e => Assert.Equal("Patient", e.Resource.ResourceTypeName),
                e2 => Assert.Equal("Observation", e2.Resource.ResourceTypeName),
                e3 => Assert.Equal("Observation", e3.Resource.ResourceTypeName));
        }

        [SkippableFact]
        public async Task GivenScopesForPatientAndObservation_WhenRevIncludeObservations_PatientAndObservationReturned1()
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

            // assert that only the Observation is returned
            Assert.DoesNotContain(results.Results, x => x.Resource.ResourceTypeName == "Patient");
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Observation");
        }

        [SkippableFact]
        public async Task GivenScopesWithReadForAllResource_WhenIncludeWithWildCardRequest_ReturnsCorrectResources()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var scopeRestriction1 = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Read, "patient");
            var scopeRestriction2 = new ScopeRestriction("all", Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction1, scopeRestriction2 });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var query = new List<Tuple<string, string>>() { new Tuple<string, string>("_include", "*:*"), new Tuple<string, string>("_id", "smart-observation-A1") };
            var results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);

            // assert that Patient, Observation and Practitioner resources are returned
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Patient");
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Observation");
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Practitioner");
        }

        [SkippableFact]
        public async Task GivenScopesForPatientAndObservation_WhenIncludeWithWildCardRequest_ReturnsOnlyPatientAndObservation()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var scopeRestriction1 = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Read, "patient");
            var scopeRestriction2 = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction1, scopeRestriction2 });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var query = new List<Tuple<string, string>>() { new Tuple<string, string>("_include", "*:*"), new Tuple<string, string>("_id", "smart-observation-A1") };
            var results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);

            // assert that Patient, and Observation resources are returned and Practitioner is not returned
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Patient");
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Observation");
            Assert.DoesNotContain(results.Results, x => x.Resource.ResourceTypeName == "Practitioner");
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

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => _fixture.GetResourceHandler.Handle(new GetResourceRequest(new ResourceKey("Patient", "smart-patient-B"), bundleResourceContext: null), CancellationToken.None));
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
        public async Task GivenFhirUserClaimPractitioner_WhenCareTeamIsRequested_OnlyCareTeamResourcesInTheSameCompartmentReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var query = new List<Tuple<string, string>>();

            var scopeRestriction = new ScopeRestriction(KnownResourceTypes.CareTeam, Core.Features.Security.DataActions.Read, "user");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-practitioner-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Practitioner";

            var results = await _searchService.Value.SearchAsync("CareTeam", query, CancellationToken.None);

            Assert.Collection(
                            results.Results,
                            r => Assert.True(r.Resource.ResourceId == "smart-careteam-1"));
        }

        [SkippableFact]
        public async Task GivenFhirUserClaimPractitioner_WhenAllResourcesRequested_ResourcesInTheSameComparementAndUniversalResourcesAlsoReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_count", "100"));

            var scopeRestriction = new ScopeRestriction(KnownResourceTypes.All, Core.Features.Security.DataActions.Read, "user");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-practitioner-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Practitioner";

            var results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);

            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.Observation);
            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.Patient);
            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.CareTeam);
            /* As per g10 standards, Resources Organization, Medication, Location and Practitioner are returned from outside of compartment.
            */
            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.Organization);
            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.Medication);
            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.Location);
            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.Practitioner);

            Assert.Contains(
                           results.Results,
                           r => r.Resource.ResourceId == "smart-careteam-1");

            Assert.Contains(
                           results.Results,
                           r => r.Resource.ResourceId == "smart-organization-A1");

            Assert.Contains(
                           results.Results,
                           r => r.Resource.ResourceId == "smart-patient-B");

            Assert.Contains(
                           results.Results,
                           r => r.Resource.ResourceId == "smart-practitioner-A");

            Assert.Contains(
                           results.Results,
                           r => r.Resource.ResourceId == "smart-practitioner-B");

            /*This is true because we are returning universal Organization resources even when they are not part of compartment of requested Practioner.*/
            Assert.Contains(
                           results.Results,
                           r => r.Resource.ResourceId == "smart-organization-B1");

            /*This is true because smart-patient-C doesnt belong to the same compartment requested and not part of universal resources that should be returned.*/
            Assert.DoesNotContain(
                           results.Results,
                           r => r.Resource.ResourceId == "smart-patient-C");
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
            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.Device);
            Assert.Equal(39, results.Results.Count());
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
            Assert.True(results.Results.Count() > 89);
        }

        [SkippableFact]
        public async Task GivenReadScopeOnAllResourcesInACompartment_OnSystemLevelWithPreviouslyUpdatedResources_ReturnsResourcesInThePatientCompartment()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // Test data - Create more than 10 universal resources or compartment resources which have lower resource type id and update them to create historical versions
            // Also create a patient compartment with some other resources, when runing the search, we should get back the compartment resources
            var scopeRestriction1 = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Read, "patient");
            var scopeRestriction2 = new ScopeRestriction("all", Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction1, scopeRestriction2 });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-D";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var results = await _searchService.Value.SearchAsync(null, null, CancellationToken.None);

            Assert.NotEmpty(results.Results);
        }

        [SkippableFact]
        public async Task GivenPatientAccessControlContext_WhenSearchingOwnCompartment_ThenResourcesReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // Patient smart-patient-A searching their own compartment
            var query = new List<Tuple<string, string>>();

            var scopeRestrictionPatient = new ScopeRestriction(KnownResourceTypes.Patient, Core.Features.Security.DataActions.Read, "patient");
            var scopeRestrictionObservation = new ScopeRestriction(KnownResourceTypes.Observation, Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestrictionPatient, scopeRestrictionObservation });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Search the patient's own compartment
            var results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                null, // all resource types
                query,
                CancellationToken.None);

            // Should return resources from smart-patient-A compartment
            Assert.NotEmpty(results.Results);
            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == "Observation" && r.Resource.ResourceId.Contains("smart-observation-A"));
        }

        [SkippableFact]
        public async Task GivenPatientAccessControlContext_WhenSearchingOtherPatientCompartment_ThenNoResourcesReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // Patient smart-patient-A trying to search smart-patient-B's compartment
            var query = new List<Tuple<string, string>>();

            var scopeRestriction = new ScopeRestriction(KnownResourceTypes.Patient, Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Try to search smart-patient-B's compartment (should be restricted)
            var results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-B",
                null, // all resource types
                query,
                CancellationToken.None);

            // Should return no resources due to compartment restrictions
            Assert.Empty(results.Results);
        }

        [SkippableFact]
        public async Task GivenPractitionerAccessControlContext_WhenSearchingPatientInTheirCompartment_ThenResourcesReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // Practitioner smart-practitioner-A searching patient compartment within their care
            var query = new List<Tuple<string, string>>();

            var scopeRestriction = new ScopeRestriction(KnownResourceTypes.Patient, Core.Features.Security.DataActions.Read, "user");
            var scopeRestriction2 = new ScopeRestriction(KnownResourceTypes.Observation, Core.Features.Security.DataActions.Read, "user");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction, scopeRestriction2 });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-practitioner-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Practitioner";

            // Search patient compartment that should be accessible to this practitioner
            var results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                null, // all resource types
                query,
                CancellationToken.None);

            // Should return resources from smart-patient-A compartment since practitioner has access
            Assert.NotEmpty(results.Results);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
        }

        [SkippableFact]
        public async Task GivenPractitionerAccessControlContext_WhenSearchingPatientNotInTheirCompartment_ThenNoResourcesReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // Practitioner smart-practitioner-A trying to search patient compartment outside their care
            var query = new List<Tuple<string, string>>() { new Tuple<string, string>("_count", "100") };

            var scopeRestriction = new ScopeRestriction(KnownResourceTypes.All, Core.Features.Security.DataActions.Read, "user");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-practitioner-C";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Practitioner";

            // Try to search patient compartment that should NOT be accessible to this practitioner
            var results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-B",
                null, // all resource types
                query,
                CancellationToken.None);

            // Should return no resources due to compartment restrictions
            Assert.Empty(results.Results);
        }

        [SkippableFact]
        public async Task GivenPatientAccessControlContext_WhenSearchingSpecificResourceTypeInOwnCompartment_ThenResourcesReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // Patient smart-patient-A searching for Observations in their own compartment
            var query = new List<Tuple<string, string>>();

            var scopeRestriction = new ScopeRestriction(KnownResourceTypes.Observation, Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Search for Observations in the patient's own compartment
            var results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Observation", // specific resource type
                query,
                CancellationToken.None);

            // Should return only Observation resources from smart-patient-A compartment
            Assert.NotEmpty(results.Results);
            Assert.All(results.Results, r => Assert.Equal("Observation", r.Resource.ResourceTypeName));
            Assert.Contains(results.Results, r => r.Resource.ResourceId.Contains("smart-observation-A"));
        }

        [SkippableFact]
        public async Task GivenPractitionerAccessControlContext_WhenSearchingOwnPractitionerCompartment_ThenResourcesReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // Practitioner searching their own practitioner compartment
            var query = new List<Tuple<string, string>>();

            var scopeRestriction = new ScopeRestriction(KnownResourceTypes.Practitioner, Core.Features.Security.DataActions.Read, "user");
            var scopeRestriction2 = new ScopeRestriction(KnownResourceTypes.CareTeam, Core.Features.Security.DataActions.Read, "user");
            var scopeRestriction3 = new ScopeRestriction(KnownResourceTypes.Patient, Core.Features.Security.DataActions.Read, "user");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction, scopeRestriction2, scopeRestriction3 });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-practitioner-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Practitioner";

            // Search the practitioner's own compartment
            var results = await _searchService.Value.SearchCompartmentAsync(
                "Practitioner",
                "smart-practitioner-A",
                null, // all resource types
                query,
                CancellationToken.None);

            // Should return resources from smart-practitioner-A compartment
            Assert.NotEmpty(results.Results);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A");
            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == "CareTeam");
        }

        [SkippableFact]
        public async Task GivenReadScopeOnAllResourcesInACompartment_OnRevincludeWithWildCardRequest_ReturnsAllResourcesThatReferenceThePatientInCompartment()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var scopeRestriction1 = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Read, "patient");
            var scopeRestriction2 = new ScopeRestriction("all", Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction1, scopeRestriction2 });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-D";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var query = new List<Tuple<string, string>>() { new Tuple<string, string>("_revinclude", "*:*"), new Tuple<string, string>("_id", "smart-patient-D") };
            var results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);

            Assert.NotEmpty(results.Results);
            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.Encounter);
            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.Observation);
            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.Patient);
        }

        [SkippableFact]
        public async Task GivenReadScopeOnOnlyEncountersInACompartment_OnRevincludeWithWildCardRequest_ReturnsOnlyEncountersThatReferenceThePatientInCompartment()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var scopeRestriction1 = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Read, "patient");
            var scopeRestriction2 = new ScopeRestriction("Encounter", Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction1, scopeRestriction2 });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-D";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var query = new List<Tuple<string, string>>() { new Tuple<string, string>("_revinclude", "*"), new Tuple<string, string>("_id", "smart-patient-D") };
            var results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);

            Assert.NotEmpty(results.Results);
            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.Encounter);
            Assert.Contains(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.Patient);
            Assert.DoesNotContain(results.Results, r => r.Resource.ResourceTypeName == KnownResourceTypes.Observation);
        }

        private async Task<UpsertOutcome> UpsertResource(Resource resource, string httpMethod = "PUT")
        {
            ResourceElement resourceElement = resource.ToResourceElement();

            var rawResource = new RawResource(resource.ToJson(), FhirResourceFormat.Json, isMetaSet: false);
            var resourceRequest = new ResourceRequest(httpMethod);
            var compartmentIndices = Substitute.For<CompartmentIndices>();
            var searchIndices = _searchIndexer.Extract(resourceElement);
            var wrapper = new ResourceWrapper(resourceElement, rawResource, resourceRequest, false, searchIndices, compartmentIndices, new List<KeyValuePair<string, string>>(), _searchParameterDefinitionManager.GetSearchParameterHashForResourceType("Patient"));
            wrapper.SearchParameterHash = "hash";

            return await _scopedDataStore.Value.UpsertAsync(new ResourceWrapperOperation(wrapper, true, true, null, false, false, bundleResourceContext: null), CancellationToken.None);
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
            ICollection<ScopeRestriction> scopes,
            bool applyFineGrainedAccessControlWithSearchParameters = false)
        {
            var accessControlContext = new AccessControlContext()
            {
                ApplyFineGrainedAccessControl = true,
                ApplyFineGrainedAccessControlWithSearchParameters = applyFineGrainedAccessControlWithSearchParameters,
            };

            foreach (var scope in scopes)
            {
                accessControlContext.AllowedResourceActions.Add(scope);
            }

            contextAccessor.RequestContext.AccessControlContext.Returns(accessControlContext);
        }

        // SMART v2 Granular Scope Tests

        [SkippableFact]
        public async Task GivenSmartV2CreateScope_WhenCreatingPatient_ThenPatientIsCreated()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var scopeRestriction = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Create, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-test";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var newPatient = new Patient
            {
                Id = "smart-v2-create-test",
                Name = new List<HumanName> { new HumanName().WithGiven("TestCreate").AndFamily("SmartV2") },
            };

            var result = await UpsertResource(newPatient);
            Assert.NotNull(result);
            Assert.Equal("smart-v2-create-test", result.Wrapper.ResourceId);
        }

        [SkippableFact]
        public async Task GivenSmartV2ReadScope_WhenReadingPatient_ThenPatientIsReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var scopeRestriction = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Read, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var result = await _fixture.GetResourceHandler.Handle(
                new GetResourceRequest(new ResourceKey("Patient", "smart-patient-A"), bundleResourceContext: null),
                CancellationToken.None);

            Assert.NotNull(result.Resource);
            Assert.Equal("smart-patient-A", result.Resource.Id);
        }

        [SkippableFact]
        public async Task GivenSmartV2SearchScope_WhenSearchingPatients_ThenPatientsAreReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("name", "SMARTGivenName1"));

            var scopeRestriction = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            var results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);

            Assert.NotEmpty(results.Results);
            Assert.All(results.Results, r => Assert.Equal("Patient", r.Resource.ResourceTypeName));
        }

        [SkippableFact]
        public async Task GivenSmartV2UpdateScope_WhenUpdatingPatient_ThenPatientIsUpdated()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var scopeRestriction = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Update, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Create an updated patient resource
            var updatedPatient = new Patient
            {
                Id = "smart-patient-A",
                Name = new List<HumanName> { new HumanName().WithGiven("UpdatedName").AndFamily("Updated") },
            };

            var result = await UpsertResource(updatedPatient);
            Assert.NotNull(result);
        }

        [SkippableFact]
        public async Task GivenSmartV2SearchAndCreateScopes_WhenSearchingWithCreate_ThenBothPermissionsWork()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var scopeRestriction1 = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient");
            var scopeRestriction2 = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Create, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction1, scopeRestriction2 });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-test";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Test search capability
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("name", "SMARTGivenName1"));
            var searchResults = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);

            Assert.False(searchResults.SearchIssues.Any());

            // Test create capability
            var newPatient = new Patient
            {
                Id = "smart-v2-search-create-test",
                Name = new List<HumanName> { new HumanName().WithGiven("SearchCreate").AndFamily("SmartV2") },
            };

            var createResult = await UpsertResource(newPatient);
            Assert.NotNull(createResult);
        }

        [SkippableFact]
        public async Task GivenSmartV2SearchAndUpdateScopes_WhenSearchingWithUpdate_ThenBothPermissionsWork()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            var scopeRestriction1 = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient");
            var scopeRestriction2 = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Update, "patient");

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction1, scopeRestriction2 });
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Test search capability
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_id", "smart-patient-A"));
            var searchResults = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(searchResults.Results);

            // Test update capability
            var updatedPatient = new Patient
            {
                Id = "smart-patient-A",
                Name = new List<HumanName> { new HumanName().WithGiven("SearchUpdate").AndFamily("SmartV2") },
            };

            var updateResult = await UpsertResource(updatedPatient);
            Assert.NotNull(updateResult);
            Assert.Equal("smart-patient-A", updateResult.Wrapper.ResourceId);
        }

        // SMART v2 Granular Scope with Search parameters Tests
        private static SearchParams CreateSearchParams(params (string key, string value)[] items)
        {
            var searchParams = new SearchParams();
            foreach (var item in items)
            {
                searchParams.Add(item.key, item.value);
            }

            return searchParams;
        }

        [SkippableFact]
        public async Task GivenSmartV2PatientSearchForSpecificNameOnlyScope_WhenSearching_ThenResultsAreReturnAsExpected()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            /*
             * scopes = patient/Patient.s?name=SMARTGivenName1
             * Only patient with name=SMARTGivenName1 is allowed in the smart-patient-A compartment
             */
            var scopeRestriction = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("name", "SMARTGivenName1")));
            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction }, true);
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Search for Patient resources - should return smart-patient-A as its name is SMARTGivenName1
            var results = await _searchService.Value.SearchAsync("Patient", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.True(r.Resource.ResourceId == "smart-patient-A"));

            // Search for all resources - should return smart-patient-A as its name is SMARTGivenName1
            results = await _searchService.Value.SearchAsync(null, null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.True(r.Resource.ResourceId == "smart-patient-A"));

            // Search for all resources with type Patient, Observation, Practitioner - should return smart-patient-A as its name is SMARTGivenName1
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.True(r.Resource.ResourceId == "smart-patient-A"));

            // Search for all resources with type parameter as Observation and Practitioner - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient with type parameter as Observation and Practitioner
            // should return smart-patient-A as its name is SMARTGivenName1
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.True(r.Resource.ResourceId == "smart-patient-A"));

            // Search for Patient resources with gender male
            // should return smart-patient-A as its gender is male and name is SMARTGivenName1
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "male"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.True(r.Resource.ResourceId == "smart-patient-A"));

            // Search for Patient resources with gender female
            // should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "female"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient resources with name=NotSMARTGivenName1
            // should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("name", "NotSMARTGivenName1"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observation resources - only patient/Patient.s?name=SMARTGivenName1 is allowed in the smart-patient-A compartment
            // should return nothing
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observation resource with code - only patient/Patient.s?name=SMARTGivenName1 is allowed in the smart-patient-A compartment
            // should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("code", "http://loinc.org|4548-4"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observations in the patient's own compartment - should return nothing
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Observation", // specific resource type
                null,
                CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for  in the patient's own compartment - should return nothing
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Patient", // specific resource type
                null,
                CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for all in the patient's own compartment - should return nothing
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                null, // specific resource type
                null,
                CancellationToken.None);
            Assert.Empty(results.Results);
        }

        [SkippableFact]
        public async Task GivenSmartV2PatientSearchScopeWithSpecificNameAndGender_WhenSearching_ThenResultsAreReturnAsExpected()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            /*
             * scopes = patient/Patient.s?name=SMARTGivenName1&gender=male
             * Only Patient with name=SMARTGivenName1 and gender=male is allowed in the smart-patient-A compartment
             */
            var scopeRestriction = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("name", "SMARTGivenName1"), ("gender", "male")));
            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction }, true);
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Search for Patient resources - should return smart-patient-A as its gender is male and name is SMARTGivenName1
            var results = await _searchService.Value.SearchAsync("Patient", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Single(results.Results);
            Assert.Collection(results.Results, r => Assert.True(r.Resource.ResourceId == "smart-patient-A"));

            // Search for all resources - should return smart-patient-A as its name is SMARTGivenName1
            results = await _searchService.Value.SearchAsync(null, null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.True(r.Resource.ResourceId == "smart-patient-A"));

            // Search for all resources - should return smart-patient-A as its name is SMARTGivenName1
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.True(r.Resource.ResourceId == "smart-patient-A"));

            // Search for all resources with type parameter as Observation and Practitioner - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient with type parameter as Observation and Practitioner - should return smart-patient-A as its name is SMARTGivenName1
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.True(r.Resource.ResourceId == "smart-patient-A"));

            // Search for Patient resources with gender male
            // should return smart-patient-A as its gender is male and name is SMARTGivenName1
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "male"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.True(r.Resource.ResourceId == "smart-patient-A"));

            // Search for Patient resources with gender female
            // should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "female"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient resources with name=NotSMARTGivenName1
            // should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("name", "NotSMARTGivenName1"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observation resources
            // should return nothing
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observation resource with code - only patient/Patient.s?name=SMARTGivenName1 is allowed in the smart-patient-A compartment
            // should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("code", "http://loinc.org|4548-4"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observations in the patient's own compartment - should return nothing
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Observation", // specific resource type
                null,
                CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for  in the patient's own compartment - should return nothing
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Patient", // specific resource type
                null,
                CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for all in the patient's own compartment - should return nothing
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                null, // specific resource type
                null,
                CancellationToken.None);
            Assert.Empty(results.Results);
        }

        [SkippableFact]
        public async Task GivenSmartV2ObservationSearchScopeWithoutAndWithCodeAndStatusFilter_WhenSearching_ThenResultsAreReturnAsExpected()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            /*
             * scopes = patient/Observation.s patient/Observation.s?code=http://loinc.org|4548-4&status=final
             * Only Observations are allowed with no restrictions in the smart-patient-A compartment
             */
            var scopeRestriction1 = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", null);
            var scopeRestriction2 = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-4"), ("status", "final")));
            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction1, scopeRestriction2 }, true);
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Search for Patient resources - should return nothing
            var results = await _searchService.Value.SearchAsync("Patient", null, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for all resources - should return Observation resources linked to smart-patient-A
            results = await _searchService.Value.SearchAsync(null, null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for all resources with type Patient,Observation,Practitioner
            // should return Observation resources linked to smart-patient-A
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for all resources with type parameter as Observation and Practitioner
            // should return Observation resources linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for Observation for specific code
            // should return single Observation resource with matching code linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("code", "http://loinc.org|4548-4"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.True(r.Resource.ResourceId == "smart-observation-A1"));

            // Search for all resources with type parameter Practitioner - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient with type parameter as Patient, Observation and Practitioner - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observation with type parameter as Patient, Observation and Practitioner
            // should return Observation resources linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 2);
            Assert.All(results.Results, r => Assert.Equal("Observation", r.Resource.ResourceTypeName));

            // Search for Patient resources with gender male - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "male"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient resources with gender female - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "female"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient resources with name=NotSMARTGivenName1 - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("name", "NotSMARTGivenName1"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observation resources - should return Observation resources linked to smart-patient-A
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for Observation resource with code - only patient/Patient.s?name=SMARTGivenName1 and gender male is allowed in the smart-patient-A compartment
            // should return Observation resource with matching code linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("code", "http://snomed.info/sct|429858000"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.True(r.Resource.ResourceId == "smart-observation-A2"));

            // Search for Observations in the patient's own compartment - should return Observation resources linked to smart-patient-A
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Observation", // specific resource type
                null,
                CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for  in the patient's own compartment - should return nothing
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Patient", // specific resource type
                null,
                CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for all in the patient's own compartment - should return Observation smart-patient-A as its name is SMARTGivenName1
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                null, // specific resource type
                null,
                CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));
        }

        [SkippableFact]
        public async Task GivenSmartV2PatientAndObservationScopeWithCombinedFilters_WhenSearching_ThenResultsAreReturnAsExpected()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            /*
             * scopes = patient/patient.s patient/Observation.s?http://loinc.org|4548-4&status=final
             * Only Patients and Observations with code and status are allowed in the smart-patient-A compartment
             */
            var scopeRestriction1 = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient", null);
            var scopeRestriction2 = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-4"), ("status", "final")));
            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction1, scopeRestriction2 }, true);
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Search for Observation resources - should return Observation resources linked to smart-patient-A with matching code and status and a patient smart-patient-A
            var results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));

            // Search for Patient resources - should return only Patient resource smart-patient-A
            results = await _searchService.Value.SearchAsync("Patient", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for all resources - should return Observation resources linked to smart-patient-A and a patient smart-patient-A
            results = await _searchService.Value.SearchAsync(null, null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 2);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A");

            // Search for all resources with type Patient,Observation,Practitioner - should return Observation resources linked to smart-patient-A and patient smart-patient-A
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 2);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A");

            // Search for all resources with type parameter as Observation and Practitioner - should return Observation resources linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));

            // Search for Observation with specific code
            // should return single Observation resource with matching code and status linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("code", "http://loinc.org|4548-4"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));

            // Search for Observation with specific code - should return no resource
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("code", "http://snomed.info/sct|429858000"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for all resources with type parameter Practitioner - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient with type parameter as Patient, Observation and Practitioner
            // should return Patient resource smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for Observation with type parameter as Patient, Observation and Practitioner
            // should return Observation resource with matching code and status linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));

            // Search for Patient resources with gender male
            // should return Patient resource smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "male"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for Patient resources with gender female
            // should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "female"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient resources with name=NotSMARTGivenName1
            // should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("name", "NotSMARTGivenName1"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observation resources
            // should return Observation resource linked to smart-patient-A
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));

            // Search for Observations in the patient's own compartment - should return nothing
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Observation", // specific resource type
                null,
                CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");

            // Search for  in the patient's own compartment - should return nothing
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Patient", // specific resource type
                null,
                CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for all in the patient's own compartment - should return nothing
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                null, // specific resource type
                null,
                CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
        }

        [SkippableFact]
        public async Task GivenSmartV2PatientPractionerAndObservationScopeWithFilters_WhenSearching_ThenResultsAreReturnAsExpected()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            /*
             * scopes = patient/patient.s patient/Practitioner.s?gender=male&name=practitionerA patient/Observation.s?http://loinc.org|4548-4&status=final
             * All patients and male Practioners with name practitionerA and Observations with code and final status are allowed
             */
            var scopeRestriction1 = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient", null);
            var scopeRestriction2 = new ScopeRestriction("Practitioner", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("gender", "male"), ("name", "practitionerA")));
            var scopeRestriction3 = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-4"), ("status", "final")));
            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction1, scopeRestriction2, scopeRestriction3 }, true);
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Search for Observation resources - should return Observation resources linked to smart-patient-A with matching code and status and a patient smart-patient-A
            var results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));

            // Search for Patient resources - should return only Patient resource smart-patient-A
            results = await _searchService.Value.SearchAsync("Patient", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for all resources - should return Observation resources linked to smart-patient-A and a patient smart-patient-A
            results = await _searchService.Value.SearchAsync(null, null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 3);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-A");

            // Search for all resources with type Patient,Observation,Practitioner
            // should return Observation resources linked to smart-patient-A and patient smart-patient-A
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 3);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-A");

            // Search for all resources with type parameter as Observation and Practitioner
            // should return Observation resources linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-practitioner-A", r1.Resource.ResourceId));

            // Search for Observation with specific code - should return single Observation resource with matching code and status linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("code", "http://loinc.org|4548-4"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Single(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));

            // Search for Observation with specific code - should return no resource
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("code", "http://snomed.info/sct|429858000"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for all resources with type parameter Practitioner - should return practitioner
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-practitioner-A", r.Resource.ResourceId));

            // Search for Patient with type parameter as Patient, Observation and Practitioner
            // should return Patient resource smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for Observation with type parameter as Patient, Observation and Practitioner
            // should return Observation resource with matching code and status linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));

            // Search for Patient resources with gender male - should return Patient resource smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "male"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for Patient resources with gender female - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "female"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient resources with name=NotSMARTGivenName1 - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("name", "NotSMARTGivenName1"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observation resources - should return Observation resource linked to smart-patient-A
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));

            // Search for Observations in the patient's own compartment - should return Observation resources linked to smart-patient-A
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Observation", // specific resource type
                null,
                CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));

            // Search for  in the patient's own compartment - should return nothing
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Patient", // specific resource type
                null,
                CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for all in the patient's own compartment - should return Observation smart-patient-A as its name is SMARTGivenName1
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                null, // specific resource type
                null,
                CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));
        }

        [SkippableFact]
        public async Task GivenSmartV2PatientSearchForSpecificNameAndObservationCodeFilterScope_WhenSearching_ThenResultsAreReturnAsExpected()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            /*
             * scopes = patient/patient.s?name=SMARTGivenName1 patient/Observation.s?http://loinc.org|4548-4
             * Only Patients with name and Observations with code and status are allowed in the smart-patient-A compartment
             */
            var scopeRestriction1 = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("name", "SMARTGivenName1")));
            var scopeRestriction2 = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-4")));
            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction1, scopeRestriction2 }, true);
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Search for Patient resources
            // should return only Patient resource smart-patient-A
            var results = await _searchService.Value.SearchAsync("Patient", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for Observation resources - only Patients with name and Observations with code and status are allowed in the smart-patient-A compartment
            // should return Observation resources linked to smart-patient-A with matching code and status and a patient smart-patient-A
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));

            // Search for Patient resources - only Patients with name and Observations with code and status are allowed in the smart-patient-A compartment
            // should return only Patient resource smart-patient-A
            results = await _searchService.Value.SearchAsync("Patient", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for all resources - only Patients with name and Observations with code and status are allowed in the smart-patient-A compartment
            // should return Observation resources linked to smart-patient-A and a patient smart-patient-A
            results = await _searchService.Value.SearchAsync(null, null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 2);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A");

            // Search for all resources with type Patient,Observation,Practitioner - only Patients with name and Observations with code and status are allowed in the smart-patient-A compartment
            // should return Observation resources linked to smart-patient-A and patient smart-patient-A
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 2);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A");

            // Search for all resources with type parameter as Observation and Practitioner - only Patients with name and Observations with code and status are allowed in the smart-patient-A compartment
            // should return Observation resources linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));

            // Search for Observation with specific code - only Patients with name and Observations with code and status are allowed in the smart-patient-A compartment
            // should return single Observation resource with matching code and status linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("code", "http://loinc.org|4548-4"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));

            // Search for Observation with specific code - only Patients with name and Observations with code and status are allowed in the smart-patient-A compartment
            // should return no resource
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("code", "http://snomed.info/sct|429858000"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for all resources with type parameter Practitioner - only Patients with name and Observations with code and status are allowed in the smart-patient-A compartment
            // should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient with type parameter as Patient, Observation and Practitioner - only Patients with name and Observations with code and status are allowed in the smart-patient-A compartment
            // should return Patient resource smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for Patient with type parameter as Patient, Observation and Practitioner - only Patients with name and Observations with code and status are allowed in the smart-patient-A compartment
            // should return Patient resource smart-patient-A and Observation
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 2);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A");

            // Search for Observation with type parameter as Patient, Observation and Practitioner - only Patients with name and Observations with code and status are allowed in the smart-patient-A compartment
            // should return Observation resource with matching code and status linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));

            // Search for Patient resources with gender male - only Patients with name and Observations with code and status are allowed in the smart-patient-A compartment
            // should return Patient resource smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "male"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for Patient resources with gender female - only Patients with name and Observations with code and status are allowed in the smart-patient-A compartment
            // should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "female"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient resources with name=NotSMARTGivenName1 - only Patients with name and Observations with code and status are allowed in the smart-patient-A compartment
            // should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("name", "NotSMARTGivenName1"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observation resources - only Patients with name and Observations with code and status are allowed in the smart-patient-A compartment
            // should return Observation resource linked to smart-patient-A
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));

            // Search for Observations in the patient's own compartment - should return Observation resources linked to smart-patient-A
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Observation", // specific resource type
                null,
                CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));

            // Search for  in the patient's own compartment - should return nothing
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Patient", // specific resource type
                null,
                CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for all in the patient's own compartment - should return Observation smart-patient-A as its name is SMARTGivenName1
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                null, // specific resource type
                null,
                CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));
        }

        [SkippableFact]
        public async Task GivenSmartV2PatientSearchWithoutAndWithSpecificNameAndObservationWithoutAndWithCodeFilterScope_WhenSearching_ThenResultsAreReturnAsExpected()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            /*
             * scopes = patient/patient.s patient/patient.s?name=SMARTGivenName1 patient/Observation.s?http://loinc.org|4548-4&status=final patient/Observation.s
             * Only Patients and Observations without any conditions in the smart-patient-A compartment
             */
            var scopeRestriction1 = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", null);
            var scopeRestriction2 = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "XYZ"), ("status", "final")));
            var scopeRestriction3 = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient", null);
            var scopeRestriction4 = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("name", "SMARTGivenName1")));
            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction1, scopeRestriction2, scopeRestriction3, scopeRestriction4 }, true);
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Search for Patient resources - should return only Patient resource smart-patient-A
            var results = await _searchService.Value.SearchAsync("Patient", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for Observation resources
            // should return Observation resources linked to smart-patient-A
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId), r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for Patient resources
            // should return only Patient resource smart-patient-A
            results = await _searchService.Value.SearchAsync("Patient", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for all resources
            // should return Observation resources linked to smart-patient-A and a patient smart-patient-A
            results = await _searchService.Value.SearchAsync(null, null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 3);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A2");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A");

            // Search for all resources with type Patient,Observation,Practitioner
            // should return Observation resources linked to smart-patient-A and patient smart-patient-A
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 3);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A2");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A");

            // Search for all resources with type parameter as Observation and Practitioner
            // should return Observation resources linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for Observation with specific code
            // should return single Observation resource with matching code and status linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("code", "http://loinc.org|4548-4"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));

            // Search for Observation with specific code - should return smart-observation-A2
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("code", "http://snomed.info/sct|429858000"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A2", r.Resource.ResourceId));

            // Search for all resources with type parameter Practitioner - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient with type parameter as Patient, Observation and Practitioner - should return Patient resource smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for Patient with type parameter as Patient, Observation and Practitioner - should return Patient resource smart-patient-A and Observation
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 3);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A2");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A");

            // Search for Observation with type parameter as Patient, Observation and Practitioner
            // should return Observation resources linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for Patient resources with gender male
            // should return Patient resource smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "male"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for Patient resources with gender female
            // should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "female"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient resources with name=NotSMARTGivenName1 - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("name", "NotSMARTGivenName1"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observation resources - should return Observation resource linked to smart-patient-A
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for Observations in the patient's own compartment - should return Observation resources linked to smart-patient-A
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Observation", // specific resource type
                null,
                CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r2 => Assert.Equal("smart-observation-A2", r2.Resource.ResourceId));

            // Search for  in the patient's own compartment - should return nothing
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Patient", // specific resource type
                null,
                CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for all in the patient's own compartment - should return Observation smart-patient-A as its name is SMARTGivenName1
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                null, // specific resource type
                null,
                CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));
        }

        [SkippableFact]
        public async Task GivenSmartV2UniversalCompartmentScope_WhenSearching_ThenResultsAreReturnAsExpected()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            /*
             * scopes = patient/*.s
             * All the resources in the smart-patient-A compartment are allowed
             */
            var scopeRestriction = new ScopeRestriction("all", Core.Features.Security.DataActions.Search, "patient", null);
            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction }, true);
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Search for Patient resources
            // should return only Patient resource smart-patient-A
            var results = await _searchService.Value.SearchAsync("Patient", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for Observation resources
            // should return Observation resources linked to smart-patient-A
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for Patient resources
            // should return only Patient resource smart-patient-A
            results = await _searchService.Value.SearchAsync("Patient", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for all resources
            // should return Observation resources linked to smart-patient-A and a patient smart-patient-A
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_count", "100"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Contains(results.Results, r => r.Resource.ResourceId.Contains("smart-patient-A"));
            Assert.Contains(results.Results, r => r.Resource.ResourceId.Contains("smart-observation-A2"));

            // Search for all resources with type Patient,Observation,Practitioner
            // should return Observation resources linked to smart-patient-A and patient smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 6);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A2");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-A");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-B");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-C");

            // Search for all resources with type parameter as Observation and Practitioner
            // should return Observation resources linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 5);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A2");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-A");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-B");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-C");

            // Search for Observation with specific code
            // should return single Observation resource with matching code and status linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("code", "http://loinc.org|4548-4"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));

            // Search for Observation with specific code
            // should return no resource
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("code", "http://snomed.info/sct|429858000"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A2", r.Resource.ResourceId));

            // Search for all resources with type parameter Practitioner
            // should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r1 => Assert.Equal("smart-practitioner-A", r1.Resource.ResourceId),
                r2 => Assert.Equal("smart-practitioner-B", r2.Resource.ResourceId),
                r3 => Assert.Equal("smart-practitioner-C", r3.Resource.ResourceId));

            // Search for Patient with type parameter as Patient, Observation and Practitioner
            // should return Patient resource smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for Patient with type parameter as Patient, Observation and Practitioner
            // should return Patient resource smart-patient-A and Observation
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 6);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A2");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-A");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-B");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-C");

            // Search for Observation with type parameter as Patient, Observation and Practitioner
            // should return Observation resources linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for Patient resources with gender male - should return Patient resource smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "male"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for Patient resources with gender female - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "female"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient resources with name=NotSMARTGivenName1 - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("name", "NotSMARTGivenName1"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observation resources
            // should return Observation resource linked to smart-patient-A
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for Observations in the patient's own compartment - should return Observation resources linked to smart-patient-A
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Observation", // specific resource type
                null,
                CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r2 => Assert.Equal("smart-observation-A2", r2.Resource.ResourceId));

            // Search for Patient in the patient's own compartment - should return nothing
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Patient", // specific resource type
                null,
                CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for all in the patient's own compartment - should return Observation smart-patient-A as its name is SMARTGivenName1
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_count", "100"));
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                null, // specific resource type
                query,
                CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Observation");
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "MedicationRequest");
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Appointment");
        }

        [SkippableFact]
        public async Task GivenSmartV2UniversalCompartmentScopeAndObservationWithCodeFilterScope_WhenSearching_ThenResultsAreReturnAsExpected()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            /*
             * scopes = patient/*.s patient/Observation.s?http://loinc.org|4548-4&status=final
             * All the resources in the smart-patient-A compartment are allowed
             */
            var scopeRestriction1 = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-4"), ("status", "final")));
            var scopeRestriction2 = new ScopeRestriction("all", Core.Features.Security.DataActions.Search, "patient", null);
            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction1, scopeRestriction2 }, true);
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Search for Patient resources
            // should return only Patient resource smart-patient-A
            var results = await _searchService.Value.SearchAsync("Patient", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for Observation resources
            // should return Observation resources linked to smart-patient-A
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for Patient resources
            // should return only Patient resource smart-patient-A
            results = await _searchService.Value.SearchAsync("Patient", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for all resources
            // should return Observation resources linked to smart-patient-A and a patient smart-patient-A
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_count", "100"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Contains(results.Results, r => r.Resource.ResourceId.Contains("smart-patient-A"));
            Assert.Contains(results.Results, r => r.Resource.ResourceId.Contains("smart-observation-A2"));

            // Search for all resources with type Patient,Observation,Practitioner
            // should return Observation resources linked to smart-patient-A and patient smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 6);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A2");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-A");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-B");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-C");

            // Search for all resources with type parameter as Observation and Practitioner
            // should return Observation resources linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 5);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A2");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-A");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-B");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-C");

            // Search for Observation with specific code
            // should return single Observation resource with matching code and status linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("code", "http://loinc.org|4548-4"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));

            // Search for Observation with specific code
            // should return no resource
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("code", "http://snomed.info/sct|429858000"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A2", r.Resource.ResourceId));

            // Search for all resources with type parameter Practitioner
            // should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r1 => Assert.Equal("smart-practitioner-A", r1.Resource.ResourceId),
                r2 => Assert.Equal("smart-practitioner-B", r2.Resource.ResourceId),
                r3 => Assert.Equal("smart-practitioner-C", r3.Resource.ResourceId));

            // Search for Patient with type parameter as Patient, Observation and Practitioner
            // should return Patient resource smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for Patient with type parameter as Patient, Observation and Practitioner
            // should return Patient resource smart-patient-A and Observation
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 6);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A2");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-A");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-B");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-C");

            // Search for Observation with type parameter as Patient, Observation and Practitioner
            // should return Observation resources linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for Patient resources with gender male - should return Patient resource smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "male"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for Patient resources with gender female - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "female"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient resources with name=NotSMARTGivenName1 - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("name", "NotSMARTGivenName1"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observation resources
            // should return Observation resource linked to smart-patient-A
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for Observation resources
            // should return Observation resource linked to smart-patient-A
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for Observations in the patient's own compartment - should return Observation resources linked to smart-patient-A
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Observation", // specific resource type
                null,
                CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r2 => Assert.Equal("smart-observation-A2", r2.Resource.ResourceId));

            // Search for  in the patient's own compartment - should return nothing
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Patient", // specific resource type
                null,
                CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for all in the patient's own compartment - should return Observation smart-patient-A as its name is SMARTGivenName1
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_count", "100"));
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                null, // specific resource type
                query,
                CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Observation");
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "MedicationRequest");
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Appointment");
        }

        [SkippableFact]
        public async Task GivenSmartV2UniversalCompartmentScopeWithTagAndObservationWithCodeFilterScope_WhenSearching_ThenResultsAreReturnAsExpected()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            /*
             * scopes = patient/*.s?_tag=8d245743-e7ff-425d-b065-33e8886c60e8 patient/Observation.s?http://loinc.org|4548-4&status=final
             * All resources with tag 8d245743-e7ff-425d-b065-33e8886c60e8 in the smart-patient-A compartment. There is only one resource with different tag smart-diagnosticreport-A3-different-tag
             */
            var scopeRestriction1 = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-4"), ("status", "final")));
            var scopeRestriction2 = new ScopeRestriction("all", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("_tag", "8d245743-e7ff-425d-b065-33e8886c60e8")));
            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction1, scopeRestriction2 }, true);
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Search for Patient resources
            // should return only Patient resource smart-patient-A
            var results = await _searchService.Value.SearchAsync("Patient", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for Observation resources
            // should return Observation resources linked to smart-patient-A
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for Patient resources
            // should return only Patient resource smart-patient-A
            results = await _searchService.Value.SearchAsync("Patient", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for all resources
            // should return all resources linked to smart-patient-A except smart-diagnosticreport-A3-different-tag
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_count", "100"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.DoesNotContain(results.Results, r => r.Resource.ResourceId == "smart-diagnosticreport-A3-different-tag");

            // Search for DiagnosticReport resources.
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "DiagnosticReport"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.Equal(2, results.Results.Count());
            Assert.DoesNotContain(results.Results, r => r.Resource.ResourceId == "smart-diagnosticreport-A3-different-tag");

            // Search for all resources with type Patient,Observation,Practitioner
            // should return Observation resources linked to smart-patient-A and patient smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 6);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A2");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-A");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-B");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-C");

            // Search for all resources with type parameter as Observation and Practitioner
            // should return Observation resources linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 5);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A2");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-A");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-B");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-C");

            // Search for Observation with specific code
            // should return single Observation resource with matching code and status linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("code", "http://loinc.org|4548-4"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId));

            // Search for Observation with specific code
            // should return no resource
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("code", "http://snomed.info/sct|429858000"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-observation-A2", r.Resource.ResourceId));

            // Search for all resources with type parameter Practitioner
            // should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 3);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-A");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-B");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-C");

            // Search for Patient with type parameter as Patient, Observation and Practitioner
            // should return Patient resource smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for Patient with type parameter as Patient, Observation and Practitioner
            // should return Patient resource smart-patient-A and Observation
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 6);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A2");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-A");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-B");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-practitioner-C");

            // Search for Observation with type parameter as Patient, Observation and Practitioner
            // should return Observation resources linked to smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for Patient resources with gender male - should return Patient resource smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "male"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r => Assert.Equal("smart-patient-A", r.Resource.ResourceId));

            // Search for Patient resources with gender female - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "female"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient resources with name=NotSMARTGivenName1 - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("name", "NotSMARTGivenName1"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observation resources
            // should return Observation resource linked to smart-patient-A
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for Observation resources
            // should return Observation resource linked to smart-patient-A
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for Observations in the patient's own compartment - should return Observation resources linked to smart-patient-A
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Observation", // specific resource type
                null,
                CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r2 => Assert.Equal("smart-observation-A2", r2.Resource.ResourceId));

            // Search for Patient in the patient's own compartment - should return nothing
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Patient", // specific resource type
                null,
                CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for DiagnosticReport in the patient's own compartment - should return nothing
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "DiagnosticReport", // specific resource type
                null,
                CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.DoesNotContain(results.Results, r => r.Resource.ResourceId == "smart-diagnosticreport-A3-different-tag");

            // Search for all in the patient's own compartment - should return different resources except smart-diagnosticreport-A3-different-tag
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_count", "100"));
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                null, // specific resource type
                query,
                CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Observation");
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "MedicationRequest");
            Assert.Contains(results.Results, x => x.Resource.ResourceTypeName == "Group");
            Assert.DoesNotContain(results.Results, r => r.Resource.ResourceId == "smart-diagnosticreport-A3-different-tag");
        }

        [SkippableFact]
        public async Task GivenSmartV2UniversalCompartmentScopeWithTypeIncludingObservationAndObservationWithCodeFilterScope_WhenSearching_ThenResultsAreReturnAsExpected()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            /*
             * scopes = patient/*.s?_type=Patient,Observation patient/Observation.s?http://loinc.org|4548-4&status=final
             * All Patients and Observations in the smart-patient-A compartment
             */
            var scopeRestriction1 = new ScopeRestriction("all", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("_type", "Patient,Observation")));
            var scopeRestriction2 = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-4"), ("status", "final")));

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction1, scopeRestriction2 }, true);
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Search for Patient resources
            // should return smart-patient-A
            var results = await _searchService.Value.SearchAsync("Patient", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r1 => Assert.Equal("smart-patient-A", r1.Resource.ResourceId));

            // Search for all resources
            // should return smart-patient-A and all the Observation resources linked to smart-patient-A
            results = await _searchService.Value.SearchAsync(null, null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 3);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A2");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A");

            // Search for all resources
            // should return smart-patient-A and all the Observation resources linked to smart-patient-A
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 3);
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A2");
            Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A");

            // Search for all resources with type parameter as Observation and Practitioner
            // should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.True(results.Results.Count() == 2);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for Patient with type parameter as Observation and Practitioner
            // should return smart-patient-A as its name is SMARTGivenName1
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r1 => Assert.Equal("smart-patient-A", r1.Resource.ResourceId));

            // Search for Patient resources with gender male - should return smart-patient-A
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "male"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(results.Results, r1 => Assert.Equal("smart-patient-A", r1.Resource.ResourceId));

            // Search for Encounter resources - should return nothing
            results = await _searchService.Value.SearchAsync("Encounter", null, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient resources with gender female - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "female"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient resources with name=NotSMARTGivenName1 - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("name", "NotSMARTGivenName1"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observation resources - should return Observation
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for Observation resources
            // should return Observation resource linked to smart-patient-A
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // Search for Observations in the patient's own compartment - should return Observation resources linked to smart-patient-A
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Observation", // specific resource type
                null,
                CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r2 => Assert.Equal("smart-observation-A2", r2.Resource.ResourceId));

            // Search for  in the patient's own compartment - should return nothing
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Patient", // specific resource type
                null,
                CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for all in the patient's own compartment - should return Observation resources linked to smart-patient-A
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                null, // specific resource type
                null,
                CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));
        }

        [SkippableFact]
        public async Task GivenSmartV2UniversalCompartmentScopeWithTypeAppointmentAndObservationWithCodeFilterScope_WhenSearching_ThenResultsAreReturnAsExpected()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            /*
             * scopes = patient/*.s?_type=Appointment patient/Observation.s?http://loinc.org|4548-4&status=final
             * All Appointments in the smart-patient-A compartment
             */
            var scopeRestriction1 = new ScopeRestriction("all", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("_type", "Appointment")));
            var scopeRestriction2 = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-4"), ("status", "final")));

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction1, scopeRestriction2 }, true);
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Search for Patient resources - should return nothing
            var results = await _searchService.Value.SearchAsync("Patient", null, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observation resources - should return nothing
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for all resources - should return Appointments only
            results = await _searchService.Value.SearchAsync(null, null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Contains(results.Results, r => r.Resource.ResourceId.Contains("smart-appointment-A1"));

            // Search for all resources - should return nothing
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for all resources with type parameter as Observation and Practitioner - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient with type parameter as Observation and Practitioner - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observation with type parameter as Observation and Practitioner - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Practitioner"));
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observation with type parameter as Patient, Observation, Appointment - should return appointment smart-appointment-A1
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Patient,Observation,Appointment"));
            results = await _searchService.Value.SearchAsync("Appointment", query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Contains(results.Results, r => r.Resource.ResourceId.Contains("smart-appointment-A1"));

            // Search at System level with type parameter as Appointment
            // should return Appointment smart-Appointment-A1
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_type", "Appointment"));
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Contains(results.Results, r => r.Resource.ResourceId.Contains("smart-appointment-A1"));

            // Search for Patient resources with gender male
            // should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "male"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Appointment resources - should return appointment smart-Appointment-A1
            results = await _searchService.Value.SearchAsync("Appointment", null, CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Contains(results.Results, r => r.Resource.ResourceId.Contains("smart-appointment-A1"));

            // Search for Patient resources with gender female - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("gender", "female"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Patient resources with name=NotSMARTGivenName1 - should return nothing
            query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("name", "NotSMARTGivenName1"));
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observation resources - should return nothing
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observation resources
            // should return Observation resource linked to smart-patient-A
            results = await _searchService.Value.SearchAsync("Observation", null, CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for Observations in the patient's own compartment - should return nothing
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Observation", // specific resource type
                null,
                CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for  in the patient's own compartment - should return nothing
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                "Patient", // specific resource type
                null,
                CancellationToken.None);
            Assert.Empty(results.Results);

            // Search for all in the patient's own compartment - should return Appointment smart-appointment-A1
            results = await _searchService.Value.SearchCompartmentAsync(
                "Patient",
                "smart-patient-A",
                null, // specific resource type
                null,
                CancellationToken.None);
            Assert.NotEmpty(results.Results);
            Assert.Contains(results.Results, r => r.Resource.ResourceId.Contains("smart-appointment-A1"));
        }

        [SkippableFact]
        public async Task GivenSmartV2GranularScopeWithCodeAndGenderFilter_WhenSearchingObservationsWithInclude_ThenObservationsAndIncludedPatientsReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            /*
             * Test validates that _include respects scope restrictions with granular scopes
             * scopes = patient/Observation.s?code=http://loinc.org|4548-4 patient/Practitioner.s?gender=female (Observation scope with specific code and female Practitioners ONLY, no Patient scope)
             * Since there is no Patient scope, included Patients should NOT be returned (secure behavior)
             * There are no female practitioners. Only observations with code 4548-4 should be returned, WITHOUT included Patient resources
             */

            try
            {
                var scopeRestriction1 = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-4")));
                var scopeRestriction2 = new ScopeRestriction("Practitioner", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("gender", "female")));
                ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction1, scopeRestriction2 }, true);
                _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
                _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

                // Include with Observation:subject
                // Search for Observation resources with _include=Observation:subject (tries to include Patient)
                // Should return ONLY smart-observation-A1 (which has code 4548-4), NOT the Patient (no Patient scope)
                var query = new List<Tuple<string, string>>();
                query.Add(new Tuple<string, string>("_include", "Observation:subject"));
                var results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);

                Assert.NotEmpty(results.Results);
                Assert.True(results.Results.Count() == 1, $"Expected 1 result (Observation only, no Patient due to missing scope), got {results.Results.Count()}");
                Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1" && r.Resource.ResourceTypeName == "Observation");

                // Verify that Patient is NOT included (because there's no Patient scope)
                // Verify that smart-observation-A2 (with different code) is NOT included
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceTypeName == "Patient");
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceId == "smart-observation-A2");

                // Include with wildcard
                query = new List<Tuple<string, string>>();
                query.Add(new Tuple<string, string>("_include", "*:*"));
                results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);

                Assert.NotEmpty(results.Results);
                Assert.True(results.Results.Count() == 1, $"Expected 1 result (Observation only, no Patient due to missing scope), got {results.Results.Count()}");
                Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1" && r.Resource.ResourceTypeName == "Observation");

                // Verify that Patient is NOT included (because there's no Patient scope)
                // Verify that smart-observation-A2 (with different code) is NOT included and male Practioner is not included
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceTypeName == "Patient");
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceId == "smart-observation-A2");
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceId == "smart-practitioner-A");
            }
            catch (BadRequestException ex)
            {
                Assert.Contains("Include and RevInclude searches do not support SMART V2 finer-grained resource constraints using search parameters.", ex.Message);
            }
        }

        [SkippableFact]
        public async Task GivenSmartV2GranularScopesWithCodeFilterAndPatientScope_WhenSearchingObservationsWithInclude_ThenObservationsAndIncludedPatientsReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            /*
             * Test validates that _include works with granular scopes when BOTH resource types are in scopes
             * scopes = patient/Observation.s?code=http://loinc.org|4548-4 AND patient/Patient.s patient/Practitioner.s?gender=female
             * With both scopes present, included Patients SHOULD be returned
             */

            try
            {
                var observationScope = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-4")));
                var patientScope = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient", null);
                var practitionerScope = new ScopeRestriction("Practitioner", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("gender", "female")));
                ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { observationScope, patientScope, practitionerScope }, true);
                _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
                _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

                // Search for Observation resources with _include=Observation:subject (includes the Patient)
                // Should return smart-observation-A1 (which has code 4548-4) and smart-patient-A (allowed by Patient scope)
                var query = new List<Tuple<string, string>>();
                query.Add(new Tuple<string, string>("_include", "Observation:subject"));
                var results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
                Assert.NotEmpty(results.Results);
                Assert.True(results.Results.Count() == 2, $"Expected 2 results (Observation + Patient), got {results.Results.Count()}");
                Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1" && r.Resource.ResourceTypeName == "Observation");
                Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A" && r.Resource.ResourceTypeName == "Patient");

                // Verify that smart-observation-A2 (with different code) is NOT included
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceId == "smart-observation-A2");

                // Search for Observation with status=final and _include
                // Granular scope limits to code 4548-4, user query adds status=final filter
                query = new List<Tuple<string, string>>();
                query.Add(new Tuple<string, string>("status", "final"));
                query.Add(new Tuple<string, string>("_include", "Observation:subject"));
                results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);

                Assert.NotEmpty(results.Results);

                // Should get smart-observation-A1 (code=4548-4 AND status=final) and smart-patient-A
                Assert.True(results.Results.Count() == 2, $"Expected 2 results (1 Observation + 1 Patient), got {results.Results.Count()}");
                Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1" && r.Resource.ResourceTypeName == "Observation");
                Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A" && r.Resource.ResourceTypeName == "Patient");

                // Verify smart-observation-A2 is NOT included (different code) and male practitioner is not included
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceId == "smart-observation-A2");
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceId == "smart-practitioner-A");

                // Include with wildcard
                query = new List<Tuple<string, string>>();
                query.Add(new Tuple<string, string>("_include", "*:*"));
                results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);

                Assert.True(results.Results.Count() == 2, $"Expected 2 results (Observation + Patient), got {results.Results.Count()}");
                Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1" && r.Resource.ResourceTypeName == "Observation");
                Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A" && r.Resource.ResourceTypeName == "Patient");

                // Verify that smart-observation-A2 (with different code) is NOT included and male Practioner is not included
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceId == "smart-observation-A2");
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceId == "smart-practitioner-A");
            }
            catch (BadRequestException ex)
            {
               Assert.Contains("Include and RevInclude searches do not support SMART V2 finer-grained resource constraints using search parameters.", ex.Message);
            }
        }

        [SkippableFact]
        public async Task GivenSmartV2GranularScopeWithNameFilter_WhenSearchingPatientsWithRevInclude_ThenPatientsAndRevIncludedObservationsReturned()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            /*
             * Test validates that _revinclude works with granular scopes containing search parameters
             * scopes = patient/Patient.s?name=SMARTGivenName1 AND patient/Observation.s?code=http://loinc.org|4548-4&status=final
             * Only patient with name=SMARTGivenName1 and all observations should be returned
             */

            try
            {
                var patientScope = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("name", "SMARTGivenName1")));
                var observationScope = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-4"), ("status", "final")));

                ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { patientScope, observationScope }, true);
                _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
                _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

                // Search for Patient resources with _revinclude=Observation:subject (includes Observations that reference the Patient)
                // Should return smart-patient-A and smart-observation-A1 only
                var query = new List<Tuple<string, string>>();
                query.Add(new Tuple<string, string>("_revinclude", "Observation:subject"));
                var results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);

                Assert.NotEmpty(results.Results);
                Assert.True(results.Results.Count() == 2, $"Expected 2 results (1 Patient + 1 Observations), got {results.Results.Count()}");
                Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A" && r.Resource.ResourceTypeName == "Patient");
                Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1" && r.Resource.ResourceTypeName == "Observation");
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceId == "smart-observation-A2" && r.Resource.ResourceTypeName == "Observation");

                // Revinclude with wildcard
                query = new List<Tuple<string, string>>();
                query.Add(new Tuple<string, string>("_revinclude", "*:*"));
                results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
                Assert.NotEmpty(results.Results);
                Assert.True(results.Results.Count() == 2, $"Expected 2 results (1 Patient + 1 Observations), got {results.Results.Count()}");
                Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-patient-A" && r.Resource.ResourceTypeName == "Patient");
                Assert.Contains(results.Results, r => r.Resource.ResourceId == "smart-observation-A1" && r.Resource.ResourceTypeName == "Observation");
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceId == "smart-observation-A2" && r.Resource.ResourceTypeName == "Observation");
            }
            catch (BadRequestException ex)
            {
                Assert.Contains("Include and RevInclude searches do not support SMART V2 finer-grained resource constraints using search parameters.", ex.Message);
            }
        }

        [SkippableFact]
        public async Task GivenGranularScopesForObservationPatientDiagnosticReport_WhenSearching_ThenResultsAreAsExpected()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // Arrange: Set up scopes
            var scopeObservation = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("status", "final")));
            var scopePatient = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient", null);
            var scopeDiagnosticReport = new ScopeRestriction("DiagnosticReport", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("status", "final")));
            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction> { scopeObservation, scopePatient, scopeDiagnosticReport }, true);
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Act & Assert

            // 1. Should succeed and return 1 Observation for SMARTGivenName1
            var query = new List<Tuple<string, string>> { new("subject:Patient.name", "SMARTGivenName1") };
            var results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.True(results.Results.Count() == 2);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-observation-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-observation-A2", r1.Resource.ResourceId));

            // 2. Should return nothing
            query = new List<Tuple<string, string>>
            {
                new("subject:Patient.name", "SMARTGivenName1"),
                new("code", "http://loinc.org|4548-4"),
                new("status", "registered"),
            };
            results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);
            Assert.Empty(results.Results);

            // 3. Should succeed and return 1 DiagnosticReport
            query = new List<Tuple<string, string>> { new("subject:Patient.organization.address-city", "Seattle") };
            results = await _searchService.Value.SearchAsync("DiagnosticReport", query, CancellationToken.None);
            Assert.True(results.Results.Count() == 2);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-diagnosticreport-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-diagnosticreport-A3-different-tag", r1.Resource.ResourceId));

            // 4. Should succeed and return 1 DiagnosticReport
            query = new List<Tuple<string, string>> { new("subject:Patient._tag", "8d245743-e7ff-425d-b065-33e8886c60e8") };
            results = await _searchService.Value.SearchAsync("DiagnosticReport", query, CancellationToken.None);
            Assert.True(results.Results.Count() == 2);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-diagnosticreport-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-diagnosticreport-A3-different-tag", r1.Resource.ResourceId));

            // 5. Should return Patient
            query = new List<Tuple<string, string>>
            {
                new("_type", "Patient,Device"),
                new("_has:Observation:subject:code", "http://loinc.org|4548-4"),
            };
            results = await _searchService.Value.SearchAsync(null, query, CancellationToken.None);
            Assert.Collection(results.Results, r1 => Assert.Equal("smart-patient-A", r1.Resource.ResourceId));

            // 6. Should return Patient
            query = new List<Tuple<string, string>> { new("_has:Observation:subject:code", "http://loinc.org|4548-4") };
            results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Collection(results.Results, r1 => Assert.Equal("smart-patient-A", r1.Resource.ResourceId));

            // 7. Should return 1 DiagnosticReport
            query = new List<Tuple<string, string>>
            {
                new("subject:Patient._type", "Patient"),
                new("subject:Patient._tag", "8d245743-e7ff-425d-b065-33e8886c60e8"),
            };
            results = await _searchService.Value.SearchAsync("DiagnosticReport", query, CancellationToken.None);
            Assert.True(results.Results.Count() == 2);
            Assert.Collection(
                results.Results,
                r => Assert.Equal("smart-diagnosticreport-A1", r.Resource.ResourceId),
                r1 => Assert.Equal("smart-diagnosticreport-A3-different-tag", r1.Resource.ResourceId));
        }

        [SkippableFact]
        public async Task GivenSmartV2MultipleGranularScopesWithSpecificFilters_WhenSearchingObservationsWithWildcardInclude_ThenCorrectResourcesReturnedExcludingFemaleActitioners()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            /*
             * Test validates complex scenario with multiple granular scopes with specific filters and wildcard include
             * scopes = patient/Observation.s?code=http://loinc.org|55233-1&status=final
             *          patient/Patient.s?name=SMARTGivenName1&gender=male
             *          patient/Practitioner.s?name=SmartPract&gender=female
             * Search: Observation?_include=*
             * Expected: Observation with code 55233-1 AND status=final, Patient with name=SMARTGivenName1 AND gender=male
             *          No Practitioners should be returned (no female practitioner exists matching the scope)
             */

            try
            {
                var observationScope = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-4"), ("status", "final")));
                var patientScope = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("name", "SMARTGivenName1"), ("gender", "male")));
                var practitionerScope = new ScopeRestriction("Practitioner", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("name", "SmartPract"), ("gender", "female")));

                ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { observationScope, patientScope, practitionerScope }, true);
                _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
                _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

                // Search for Observations with wildcard include
                var query = new List<Tuple<string, string>>();
                query.Add(new Tuple<string, string>("_include", "*"));
                var results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);

                // Verify results
                Assert.NotEmpty(results.Results);

                // Should contain Observation with code http://loinc.org|55233-1 and status=final
                // Note: Based on test data, we need to verify which observation has this specific code
                var observationResults = results.Results.Where(r => r.Resource.ResourceTypeName == "Observation").ToList();
                Assert.NotEmpty(observationResults);

                // Should contain Patient with name=SMARTGivenName1 and gender=male (smart-patient-A matches this criteria)
                var patientResults = results.Results.Where(r => r.Resource.ResourceTypeName == "Patient").ToList();
                Assert.NotEmpty(patientResults);
                Assert.Contains(patientResults, r => r.Resource.ResourceId == "smart-patient-A");

                // Should NOT contain any Practitioners (no female practitioner with name=SmartPract exists in test data)
                var practitionerResults = results.Results.Where(r => r.Resource.ResourceTypeName == "Practitioner").ToList();
                Assert.Empty(practitionerResults);

                // Verify that only allowed resource types are returned (Observation and Patient)
                var resourceTypes = results.Results.Select(r => r.Resource.ResourceTypeName).Distinct().ToList();
                Assert.All(resourceTypes, type => Assert.True(
                    type == "Observation" || type == "Patient",
                    $"Unexpected resource type returned: {type}"));

                // Verify no other resource types are included despite wildcard include
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceTypeName == "Encounter");
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceTypeName == "Appointment");
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceTypeName == "MedicationRequest");
            }
            catch (BadRequestException ex)
            {
                Assert.Contains("Include and RevInclude searches do not support SMART V2 finer-grained resource constraints using search parameters.", ex.Message);
            }
        }

        [SkippableFact]
        public async Task GivenSmartV2MultipleGranularScopesWithSpecificFilters_WhenSearchingObservationsWithSpecificIncludes_ThenCorrectResourcesReturnedExcludingFemalePractitioners()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            /*
             * Test validates complex scenario with multiple granular scopes with specific filters and specific includes
             * scopes = patient/Observation.s?code=http://loinc.org|55233-1&status=final
             *          patient/Patient.s?name=SMARTGivenName1&gender=male
             *          patient/Practitioner.s?name=SmartPract&gender=female
             * Search: Observation?_include=Observation:subject&_include=Observation:performer
             * Expected: Observation with code 55233-1 AND status=final, Patient with name=SMARTGivenName1 AND gender=male
             *          No Practitioners should be returned (no female practitioner exists matching the scope)
             */

            try
            {
                var observationScope = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-4"), ("status", "final")));
                var patientScope = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("name", "SMARTGivenName1"), ("gender", "male")));
                var practitionerScope = new ScopeRestriction("Practitioner", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("name", "SmartPract"), ("gender", "female")));

                ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { observationScope, patientScope, practitionerScope }, true);
                _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
                _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

                // Search for Observations with specific includes
                var query = new List<Tuple<string, string>>();
                query.Add(new Tuple<string, string>("_include", "Observation:subject"));
                query.Add(new Tuple<string, string>("_include", "Observation:performer"));
                var results = await _searchService.Value.SearchAsync("Observation", query, CancellationToken.None);

                // Verify results
                Assert.NotEmpty(results.Results);

                // Should contain Observation with code http://loinc.org|55233-1 and status=final
                var observationResults = results.Results.Where(r => r.Resource.ResourceTypeName == "Observation").ToList();
                Assert.NotEmpty(observationResults);

                // Should contain Patient with name=SMARTGivenName1 and gender=male (smart-patient-A matches this criteria)
                var patientResults = results.Results.Where(r => r.Resource.ResourceTypeName == "Patient").ToList();
                Assert.NotEmpty(patientResults);
                Assert.Contains(patientResults, r => r.Resource.ResourceId == "smart-patient-A");

                // Should NOT contain any Practitioners (no female practitioner with name=SmartPract exists in test data)
                var practitionerResults = results.Results.Where(r => r.Resource.ResourceTypeName == "Practitioner").ToList();
                Assert.Empty(practitionerResults);

                // Verify that only allowed resource types are returned (Observation and Patient)
                var resourceTypes = results.Results.Select(r => r.Resource.ResourceTypeName).Distinct().ToList();
                Assert.All(resourceTypes, type => Assert.True(
                    type == "Observation" || type == "Patient",
                    $"Unexpected resource type returned: {type}"));
            }
            catch (BadRequestException ex)
            {
                Assert.Contains("Include and RevInclude searches do not support SMART V2 finer-grained resource constraints using search parameters.", ex.Message);
            }
        }

        [SkippableFact]
        public async Task GivenSmartV2MultipleGranularScopesWithSpecificFilters_WhenSearchingPatientsWithWildcardRevInclude_ThenCorrectResourcesReturnedExcludingSpecificEncounters()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            /*
             * Test validates complex scenario with multiple granular scopes with specific filters and wildcard revinclude
             * scopes = patient/Observation.s?code=http://loinc.org|55233-1&status=final
             *          patient/Patient.s?name=SMARTGivenName1&gender=male
             *          patient/Encounter.s?status=finished&class=IMP
             * Search: Patient?_revinclude=*:*
             * Expected: Patient with name=SMARTGivenName1 AND gender=male,
             *          Observation with code 55233-1 AND status=final that references the patient,
             *          No Encounters should be returned if none match status=finished AND class=IMP
             */

            try
            {
                var observationScope = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-4"), ("status", "final")));
                var patientScope = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("name", "SMARTGivenName1"), ("gender", "male")));

                // Using Encounter with status=finished and class=IMP (inpatient) - adjust based on actual test data
                var encounterScope = new ScopeRestriction("Encounter", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("status", "triaged")));

                ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { observationScope, patientScope, encounterScope }, true);
                _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
                _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

                // Search for Patient with wildcard revinclude
                var query = new List<Tuple<string, string>>();
                query.Add(new Tuple<string, string>("_revinclude", "*:*"));
                var results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);

                // Verify results
                Assert.NotEmpty(results.Results);

                // Should contain Patient with name=SMARTGivenName1 and gender=male (smart-patient-A matches this criteria)
                var patientResults = results.Results.Where(r => r.Resource.ResourceTypeName == "Patient").ToList();
                Assert.NotEmpty(patientResults);
                Assert.Contains(patientResults, r => r.Resource.ResourceId == "smart-patient-A");

                // Should contain only Observations that match the scope filters (code 55233-1 and status=final)
                var observationResults = results.Results.Where(r => r.Resource.ResourceTypeName == "Observation").ToList();

                // Observations matching the scope should be present

                // Check Encounter results - should only include those with status=finished AND class=IMP
                var encounterResults = results.Results.Where(r => r.Resource.ResourceTypeName == "Encounter").ToList();

                // If no encounters match both criteria, this should be empty
                Assert.Empty(encounterResults);

                // Verify no resource types outside of scopes are included
                var allowedTypes = new[] { "Patient", "Observation", "Encounter" };
                var actualTypes = results.Results.Select(r => r.Resource.ResourceTypeName).Distinct().ToList();
                foreach (var type in actualTypes)
                {
                    Assert.Contains(type, allowedTypes);
                }

                // Verify specific exclusions - resources that would normally be included with wildcard but are filtered by scopes
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceTypeName == "Appointment");
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceTypeName == "MedicationRequest");
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceTypeName == "Practitioner");
            }
            catch (BadRequestException ex)
            {
                Assert.Contains("Include and RevInclude searches do not support SMART V2 finer-grained resource constraints using search parameters.", ex.Message);
            }
        }

        [SkippableFact]
        public async Task GivenSmartV2MultipleGranularScopesWithSpecificFilters_WhenSearchingPatientsWithSpecificRevIncludes_ThenCorrectResourcesReturnedExcludingSpecificEncounters()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            /*
             * Test validates complex scenario with multiple granular scopes with specific filters and specific revincludes
             * scopes = patient/Observation.s?code=http://loinc.org|55233-1&status=final
             *          patient/Patient.s?name=SMARTGivenName1&gender=male
             *          patient/Encounter.s?status=finished&class=IMP
             * Search: Patient?_revinclude=Observation:subject&_revinclude=Encounter:subject
             * Expected: Patient with name=SMARTGivenName1 AND gender=male,
             *          Observation with code 55233-1 AND status=final that references the patient,
             *          No Encounters should be returned if none match status=finished AND class=IMP
             */

            try
            {
                var observationScope = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-4"), ("status", "final")));
                var patientScope = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("name", "SMARTGivenName1"), ("gender", "male")));

                // Using Encounter with restrictive filters to potentially exclude encounters
                var encounterScope = new ScopeRestriction("Encounter", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("status", "triaged")));

                ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { observationScope, patientScope, encounterScope }, true);
                _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
                _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

                // Search for Patient with specific revincludes for Observation and Encounter
                var query = new List<Tuple<string, string>>();
                query.Add(new Tuple<string, string>("_revinclude", "Observation:subject"));
                query.Add(new Tuple<string, string>("_revinclude", "Encounter:subject"));
                var results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);

                // Verify results
                Assert.NotEmpty(results.Results);

                // Should contain Patient with name=SMARTGivenName1 and gender=male (smart-patient-A matches this criteria)
                var patientResults = results.Results.Where(r => r.Resource.ResourceTypeName == "Patient").ToList();
                Assert.NotEmpty(patientResults);
                Assert.Contains(patientResults, r => r.Resource.ResourceId == "smart-patient-A");

                // Should contain only Observations that match the scope filters (code 55233-1 and status=final)
                var observationResults = results.Results.Where(r => r.Resource.ResourceTypeName == "Observation").ToList();
                Assert.Single(observationResults);

                // Verify observations match the scope criteria
                // Should only contain Encounters that match the scope filters (status=finished AND class=IMP)
                var encounterResults = results.Results.Where(r => r.Resource.ResourceTypeName == "Encounter").ToList();

                // If no encounters match the restrictive criteria, this should be empty
                Assert.Empty(encounterResults);

                // Verify only explicitly requested resource types are included
                var allowedTypes = new[] { "Patient", "Observation", "Encounter" };
                var actualTypes = results.Results.Select(r => r.Resource.ResourceTypeName).Distinct().ToList();
                foreach (var type in actualTypes)
                {
                    Assert.Contains(type, allowedTypes);
                }

                // Verify no other resource types are included
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceTypeName == "Appointment");
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceTypeName == "MedicationRequest");
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceTypeName == "Practitioner");
            }
            catch (BadRequestException ex)
            {
                Assert.Contains("Include and RevInclude searches do not support SMART V2 finer-grained resource constraints using search parameters.", ex.Message);
            }
        }

        [SkippableFact]
        public async Task GivenSmartV2MultipleGranularScopesWithSpecificFilters_WhenSearchingPatientsWithSpecificRevIncludes_ThenCorrectResourcesReturnedIncludingSpecificEncounters()
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            /*
             * Test validates complex scenario with multiple granular scopes with specific filters and specific revincludes
             * scopes = patient/Observation.s?code=http://loinc.org|55233-1&status=final
             *          patient/Patient.s?name=SMARTGivenName1&gender=male
             *          patient/Encounter.s?status=finished&class=IMP
             * Search: Patient?_revinclude=Observation:subject&_revinclude=Encounter:subject
             * Expected: Patient with name=SMARTGivenName1 AND gender=male,
             *          Observation with code 55233-1 AND status=final that references the patient,
             *          No Encounters should be returned if none match status=finished AND class=IMP
             */

            try
            {
                var observationScope = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-4"), ("status", "final")));
                var observationScope2 = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-9")));
                var patientScope = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("name", "SMARTGivenName1"), ("gender", "male")));

                // Using Encounter with restrictive filters to potentially exclude encounters
                var encounterScope = new ScopeRestriction("Encounter", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("status", "finished")));

                ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { observationScope, observationScope2, patientScope, encounterScope }, true);
                _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
                _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

                // Search for Patient with specific revincludes for Observation and Encounter
                var query = new List<Tuple<string, string>>();
                query.Add(new Tuple<string, string>("_revinclude", "Observation:subject"));
                query.Add(new Tuple<string, string>("_revinclude", "Encounter:subject"));
                var results = await _searchService.Value.SearchAsync("Patient", query, CancellationToken.None);

                // Verify results
                Assert.NotEmpty(results.Results);

                // Should contain Patient with name=SMARTGivenName1 and gender=male (smart-patient-A matches this criteria)
                var patientResults = results.Results.Where(r => r.Resource.ResourceTypeName == "Patient").ToList();
                Assert.NotEmpty(patientResults);
                Assert.Contains(patientResults, r => r.Resource.ResourceId == "smart-patient-A");

                // Should contain only Observations that match the scope filters (code 55233-1 and status=final)
                var observationResults = results.Results.Where(r => r.Resource.ResourceTypeName == "Observation").ToList();
                Assert.Single(observationResults);
                var observationA2result = observationResults.Where(o => o.Resource.ResourceId == "smart-observation-A2");
                Assert.Empty(observationA2result);

                // Should only contain Encounters that match the scope filters (status=finished AND class=IMP)
                var encounterResults = results.Results.Where(r => r.Resource.ResourceTypeName == "Encounter").ToList();

                Assert.NotEmpty(encounterResults);
                Assert.Single(encounterResults);
                Assert.Contains(encounterResults, e => e.Resource.ResourceId == "smart-encounter-A1");

                // Verify only explicitly requested resource types are included
                var allowedTypes = new[] { "Patient", "Observation", "Encounter" };
                var actualTypes = results.Results.Select(r => r.Resource.ResourceTypeName).Distinct().ToList();
                foreach (var type in actualTypes)
                {
                    Assert.Contains(type, allowedTypes);
                }

                // Verify no other resource types are included
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceTypeName == "Appointment");
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceTypeName == "MedicationRequest");
                Assert.DoesNotContain(results.Results, r => r.Resource.ResourceTypeName == "Practitioner");
            }
            catch (BadRequestException ex)
            {
                Assert.Contains("Include and RevInclude searches do not support SMART V2 finer-grained resource constraints using search parameters.", ex.Message);
            }
        }
    }
}
