// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Core.Features.Resources.Create;
using Microsoft.Health.Fhir.Core.Features.Resources.Delete;
using Microsoft.Health.Fhir.Core.Features.Resources.Get;
using Microsoft.Health.Fhir.Core.Features.Resources.Upsert;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.Mocks;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources
{
    /// <summary>
    /// Tests Resource Handlers
    /// </summary>
    public partial class ResourceHandlerTests
    {
        private readonly IFhirDataStore _fhirDataStore;
        private readonly IConformanceProvider _conformanceProvider;
        private readonly IRawResourceFactory _rawResourceFactory;
        private readonly IResourceWrapperFactory _resourceWrapperFactory;
        private readonly CapabilityStatement _conformanceStatement;
        private readonly IMediator _mediator;
        private readonly ISearchService _searchService;
        private readonly ResourceIdProvider _resourceIdProvider;
        private IFhirAuthorizationService _authorizationService;

        public ResourceHandlerTests()
        {
            _fhirDataStore = Substitute.For<IFhirDataStore>();
            _conformanceProvider = Substitute.For<ConformanceProviderBase>();
            _searchService = Substitute.For<ISearchService>();

            // TODO: FhirRepository instantiate ResourceDeserializer class directly
            // which will try to deserialize the raw resource. We should mock it as well.
            _rawResourceFactory = Substitute.For<RawResourceFactory>(new FhirJsonSerializer());
            _resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
            _resourceWrapperFactory
                .Create(Arg.Any<ResourceElement>(), Arg.Any<bool>())
                .Returns(x => CreateResourceWrapper(x.ArgAt<ResourceElement>(0), x.ArgAt<bool>(1)));

            _conformanceStatement = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(_conformanceStatement, ResourceType.Observation, null);
            var observationResource = _conformanceStatement.Rest.First().Resource.Find(x => x.Type == ResourceType.Observation);
            observationResource.ReadHistory = false;
            observationResource.UpdateCreate = true;
            observationResource.ConditionalCreate = true;
            observationResource.ConditionalUpdate = true;
            observationResource.Versioning = CapabilityStatement.ResourceVersionPolicy.Versioned;

            CapabilityStatementMock.SetupMockResource(_conformanceStatement, ResourceType.Patient, null);
            var patientResource = _conformanceStatement.Rest.First().Resource.Find(x => x.Type == ResourceType.Patient);
            patientResource.ReadHistory = true;
            patientResource.UpdateCreate = true;
            patientResource.ConditionalCreate = true;
            patientResource.ConditionalUpdate = true;
            patientResource.Versioning = CapabilityStatement.ResourceVersionPolicy.VersionedUpdate;

            _conformanceProvider.GetCapabilityStatementAsync().Returns(_conformanceStatement.ToTypedElement().ToResourceElement());
            var lazyConformanceProvider = new Lazy<IConformanceProvider>(() => _conformanceProvider);

            var collection = new ServiceCollection();

            // an auth service that allows all.
            _authorizationService = Substitute.For<IFhirAuthorizationService>();
            _authorizationService.CheckAccess(Arg.Any<DataActions>()).Returns(ci => ci.Arg<DataActions>());

            var referenceResolver = new ResourceReferenceResolver(_searchService, new TestQueryStringParser());
            _resourceIdProvider = new ResourceIdProvider();
            collection.Add(x => _mediator).Singleton().AsSelf();
            collection.Add(x => new CreateResourceHandler(_fhirDataStore, lazyConformanceProvider, _resourceWrapperFactory, _resourceIdProvider, referenceResolver, _authorizationService)).Singleton().AsSelf().AsImplementedInterfaces();
            collection.Add(x => new UpsertResourceHandler(_fhirDataStore, lazyConformanceProvider, _resourceWrapperFactory, _resourceIdProvider, _authorizationService)).Singleton().AsSelf().AsImplementedInterfaces();
            collection.Add(x => new ConditionalCreateResourceHandler(_fhirDataStore, lazyConformanceProvider, _resourceWrapperFactory, _searchService, x.GetService<IMediator>(), _resourceIdProvider, _authorizationService)).Singleton().AsSelf().AsImplementedInterfaces();
            collection.Add(x => new ConditionalUpsertResourceHandler(_fhirDataStore, lazyConformanceProvider, _resourceWrapperFactory, _searchService, x.GetService<IMediator>(), _resourceIdProvider, _authorizationService)).Singleton().AsSelf().AsImplementedInterfaces();
            collection.Add(x => new GetResourceHandler(_fhirDataStore, lazyConformanceProvider, _resourceWrapperFactory, Deserializers.ResourceDeserializer, _resourceIdProvider, _authorizationService)).Singleton().AsSelf().AsImplementedInterfaces();
            collection.Add(x => new DeleteResourceHandler(_fhirDataStore, lazyConformanceProvider, _resourceWrapperFactory, _resourceIdProvider, _authorizationService)).Singleton().AsSelf().AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(type => provider.GetService(type));
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenCreatingAResourceWithAnId_ThenTheIdShouldBeIgnoredAndAnIdShouldBeAssigned()
        {
            var resource = Samples.GetDefaultObservation()
                .UpdateId("id1");

            var wrapper = CreateResourceWrapper(resource, false);

            _fhirDataStore.UpsertAsync(Arg.Any<ResourceWrapper>(), Arg.Any<WeakETag>(), true, true, Arg.Any<CancellationToken>()).Returns(new UpsertOutcome(wrapper, SaveOutcomeType.Created));

            resource = await _mediator.CreateResourceAsync(resource);

            Assert.NotNull(resource.Id);
            Assert.NotEqual("id1", resource.Id);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenSavingAResourceWithNoId_ThenAnIdShouldBeAssigned()
        {
            var resource = Samples.GetDefaultObservation()
                .UpdateId(null);

            _fhirDataStore.UpsertAsync(Arg.Any<ResourceWrapper>(), Arg.Any<WeakETag>(), true, true, Arg.Any<CancellationToken>())
                .Returns(x => new UpsertOutcome(x.ArgAt<ResourceWrapper>(0), SaveOutcomeType.Created));

            resource = (await _mediator.UpsertResourceAsync(resource)).Resource;

            Assert.NotNull(resource.Id);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenCreatingAResourceAndResourceIdProviderHasValue_ThenTheIdShouldBeUsedFromResourceIdProvider()
        {
            var resource = Samples.GetDefaultObservation()
                .UpdateId("id1");

            _resourceIdProvider.Create = () => "id2";

            var wrapper = CreateResourceWrapper(resource, false);

            _fhirDataStore.UpsertAsync(Arg.Any<ResourceWrapper>(), Arg.Any<WeakETag>(), true, true, Arg.Any<CancellationToken>()).Returns(new UpsertOutcome(wrapper, SaveOutcomeType.Created));

            resource = await _mediator.CreateResourceAsync(resource);

            Assert.NotNull(resource.Id);
            Assert.Equal("id2", resource.Id);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenSavingAResource_ThenLastUpdatedShouldBeSet()
        {
            var resource = Samples.GetDefaultObservation();
            DateTime baseDate = DateTimeOffset.Now.Date;
            var instant = new DateTimeOffset(baseDate.AddTicks((6 * TimeSpan.TicksPerMillisecond) + (long)(0.7 * TimeSpan.TicksPerMillisecond)), TimeSpan.Zero);

            using (Mock.Property(() => Clock.UtcNowFunc, () => instant))
            {
                _fhirDataStore.UpsertAsync(Arg.Any<ResourceWrapper>(), Arg.Any<WeakETag>(), true, true, Arg.Any<CancellationToken>())
                    .Returns(x => new UpsertOutcome(x.ArgAt<ResourceWrapper>(0), SaveOutcomeType.Created));

                resource = (await _mediator.UpsertResourceAsync(resource)).Resource;

                Assert.Equal(new DateTimeOffset(baseDate.AddMilliseconds(6), TimeSpan.Zero), resource.LastUpdated);
            }
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenSavingAResource_ThenVersionShouldBeSet()
        {
            var resource = Samples.GetDefaultObservation();

            ResourceWrapper CreateWrapper(ResourceWrapper wrapper)
            {
                var newResource = Samples.GetDefaultObservation().ToPoco();
                newResource.Id = wrapper.ResourceId;
                newResource.VersionId = "version1";
                return CreateResourceWrapper(newResource.ToResourceElement(), false);
            }

            _fhirDataStore.UpsertAsync(Arg.Any<ResourceWrapper>(), Arg.Any<WeakETag>(), true, true, Arg.Any<CancellationToken>())
                .Returns(x => new UpsertOutcome(CreateWrapper(x.ArgAt<ResourceWrapper>(0)), SaveOutcomeType.Created));

            var outcome = await _mediator.UpsertResourceAsync(resource);

            Assert.Equal("version1", outcome.Resource.VersionId);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenSavingAResourceWithoutETagAndETagIsRequired_ThenPreconditionFailExceptionIsThrown()
        {
            _conformanceStatement.Rest.First().Resource.Find(x => x.Type == ResourceType.Patient).UpdateCreate = false;

            var resource = Samples.GetDefaultPatient();

            await Assert.ThrowsAsync<PreconditionFailedException>(async () => await _mediator.UpsertResourceAsync(resource, null));
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenSavingAResourceWithoutETagAndETagIsRequiredWithUpdateCreate_ThenPreconditionFailExceptionIsThrown()
        {
            var resource = Samples.GetDefaultPatient();

            // Documented for completeness, but under this situation arguably this request should succeed.
            // Versioned-update + UpdateCreate; When no If-Match header is provided, the request should allow create but not update
            await Assert.ThrowsAsync<PreconditionFailedException>(async () => await _mediator.UpsertResourceAsync(resource, null));
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenSavingAResourceWithoutETag_ThenPreconditionFailExceptionIsThrown()
        {
            var resource = Samples.GetDefaultObservation();

            ResourceWrapper CreateWrapper(ResourceWrapper wrapper)
            {
                var newResource = Samples.GetDefaultObservation().ToPoco();
                newResource.Id = wrapper.ResourceId;
                newResource.VersionId = "version1";
                return CreateResourceWrapper(newResource.ToResourceElement(), false);
            }

            _fhirDataStore.UpsertAsync(Arg.Any<ResourceWrapper>(), Arg.Any<WeakETag>(), true, true, Arg.Any<CancellationToken>())
                .Returns(x => new UpsertOutcome(CreateWrapper(x.ArgAt<ResourceWrapper>(0)), SaveOutcomeType.Updated));

            var outcome = await _mediator.UpsertResourceAsync(resource, null);

            Assert.NotNull(outcome);
            Assert.Equal("version1", outcome.Resource.VersionId);
        }

        [Fact]
        public async Task GivenAFhirMediator_GettingAnResourceThatDoesntExist_ThenANotFoundExceptionIsThrown()
        {
            await Assert.ThrowsAsync<ResourceNotFoundException>(async () => await _mediator.GetResourceAsync(new ResourceKey<Observation>("id1")));

            await _fhirDataStore.Received().GetAsync(Arg.Any<ResourceKey>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAFhirMediator_GettingAnResourceThatIsDeleted_ThenAGoneExceptionIsThrown()
        {
            var observation = Samples.GetDefaultObservation()
                .UpdateId("id1");

            _fhirDataStore.GetAsync(Arg.Is<ResourceKey>(x => x.Id == "id1"), Arg.Any<CancellationToken>()).Returns(CreateResourceWrapper(observation, true));

            await Assert.ThrowsAsync<ResourceGoneException>(async () => await _mediator.GetResourceAsync(new ResourceKey<Observation>("id1")));

            await _fhirDataStore.Received().GetAsync(Arg.Any<ResourceKey>(), Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenAFhirMediator_WhenDeletingAResourceByVersionId_ThenMethodNotAllowedIsThrown(bool hardDelete)
        {
            await Assert.ThrowsAsync<MethodNotAllowedException>(async () => await _mediator.DeleteResourceAsync(new ResourceKey<Observation>("id1", "version1"), hardDelete));
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenDeletingAResourceThatIsAlreadyDeleted_ThenDoNothing()
        {
            _fhirDataStore.UpsertAsync(Arg.Any<ResourceWrapper>(), null, true, true, Arg.Any<CancellationToken>()).Returns(default(UpsertOutcome));

            var resourceKey = new ResourceKey<Observation>("id1");
            ResourceKey resultKey = (await _mediator.DeleteResourceAsync(resourceKey, false)).ResourceKey;

            Assert.Equal(resourceKey.Id, resultKey.Id);
            Assert.Equal("Observation", resultKey.ResourceType);
            Assert.Null(resultKey.VersionId);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenHardDeletingWithSufficientPermissions_ThenItWillBeHardDeleted()
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>()).Returns(DataActions.Delete | DataActions.HardDelete);

            ResourceKey resourceKey = new ResourceKey<Observation>("id1");

            ResourceKey resultKey = (await _mediator.DeleteResourceAsync(resourceKey, true)).ResourceKey;

            await _fhirDataStore.Received(1).HardDeleteAsync(resourceKey, Arg.Any<CancellationToken>());

            Assert.NotNull(resultKey);
            Assert.Equal(resourceKey.Id, resultKey.Id);
            Assert.Null(resultKey.VersionId);
        }

        [Theory]
        [InlineData(DataActions.Delete)]
        [InlineData(DataActions.HardDelete)]
        public async Task GivenAFhirMediator_WhenHardDeletingWithInsufficientPermissions_ThenFails(DataActions permittedActions)
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>()).Returns(permittedActions);

            ResourceKey resourceKey = new ResourceKey<Observation>("id1");

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => _mediator.DeleteResourceAsync(resourceKey, true));
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenConfiguredWithoutReadHistory_ThenReturnMethodNotAllowedForOldVersionObservation()
        {
            var observation = Samples.GetDefaultObservation()
                .UpdateId("readDataObservation");

            var history = CreateMockResourceWrapper(observation, false);
            history.IsHistory.Returns(true);

            _fhirDataStore.GetAsync(Arg.Is<ResourceKey>(x => x.Id == "readDataObservation" && x.VersionId == "history"), Arg.Any<CancellationToken>()).Returns(history);

            await Assert.ThrowsAsync<MethodNotAllowedException>(async () =>
            {
                await _mediator.GetResourceAsync(new ResourceKey("Observation", "readDataObservation", "history"));
            });
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenConfiguredWithoutReadHistory_ThenReturnObservationForLatestVersion()
        {
            var observation = Samples.GetDefaultObservation()
                .UpdateId("readDataObservation");

            var latest = CreateMockResourceWrapper(observation, false);
            latest.IsHistory.Returns(false);

            _fhirDataStore.GetAsync(Arg.Is<ResourceKey>(x => x.Id == "readDataObservation"), Arg.Any<CancellationToken>()).Returns(latest);

            var result = await _mediator.GetResourceAsync(new ResourceKey("Observation", "readDataObservation", "latest"));

            Assert.NotNull(result);
            Assert.Equal(observation.Id, result.Id);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenConfiguredWithReadHistory_ThenReturnsPatientWithOldVersion()
        {
            var birthDateProp = "Patient.BirthDate";
            var genderDateProp = "Patient.Gender";

            var patient = Samples.GetDefaultPatient();

            var history = CreateMockResourceWrapper(patient, false);
            history.IsHistory.Returns(true);

            _fhirDataStore.GetAsync(Arg.Is<ResourceKey>(x => x.Id == "readDataPatient" && x.VersionId == "history"), Arg.Any<CancellationToken>()).Returns(history);

            var result = await _mediator.GetResourceAsync(new ResourceKey("Patient", "readDataPatient", "history"));

            Assert.NotNull(result);
            Assert.Equal(patient.Scalar<string>(birthDateProp), result.Scalar<string>(birthDateProp));
            Assert.Equal(patient.Scalar<string>(genderDateProp), result.Scalar<string>(genderDateProp));
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenConfiguredWithoutReadHistory_ThenReturnsPatientWithLatestVersion()
        {
            var birthDateProp = "Patient.BirthDate";
            var genderDateProp = "Patient.Gender";

            var patient = Samples.GetDefaultPatient();

            var latest = CreateMockResourceWrapper(patient, false);
            latest.IsHistory.Returns(false);

            _fhirDataStore.GetAsync(Arg.Is<ResourceKey>(x => x.Id == "readDataPatient"), Arg.Any<CancellationToken>()).Returns(latest);

            ResourceElement result = await _mediator.GetResourceAsync(new ResourceKey("Patient", "readDataPatient", "latest"));

            Assert.NotNull(result);
            Assert.Equal(patient.Scalar<string>(birthDateProp), result.Scalar<string>(birthDateProp));
            Assert.Equal(patient.Scalar<string>(genderDateProp), result.Scalar<string>(genderDateProp));
        }

        private ResourceWrapper CreateResourceWrapper(ResourceElement resource, bool isDeleted)
        {
            return new ResourceWrapper(
                resource,
                _rawResourceFactory.Create(resource),
                new ResourceRequest(HttpMethod.Post, "http://fhir"),
                isDeleted,
                null,
                null,
                null);
        }

        private ResourceWrapper CreateMockResourceWrapper(ResourceElement resource, bool isDeleted)
        {
            return Substitute.For<ResourceWrapper>(
                resource,
                _rawResourceFactory.Create(resource),
                new ResourceRequest(HttpMethod.Put, "http://fhir"),
                isDeleted,
                null,
                null,
                null);
        }
    }
}
