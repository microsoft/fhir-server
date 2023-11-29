// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net.Http;
using System.Threading;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterBehaviorTests
    {
        private readonly ISearchParameterOperations _searchParameterOperations = Substitute.For<ISearchParameterOperations>();
        private readonly IRawResourceFactory _rawResourceFactory;
        private readonly IResourceWrapperFactory _resourceWrapperFactory;
        private readonly IFhirDataStore _fhirDataStore;

        public SearchParameterBehaviorTests()
        {
            _rawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());
            _resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
            _resourceWrapperFactory
                .Create(Arg.Any<ResourceElement>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(x => CreateResourceWrapper(x.ArgAt<ResourceElement>(0), x.ArgAt<bool>(1)));

            _fhirDataStore = Substitute.For<IFhirDataStore>();
        }

        [Fact]
        public async Task GivenACreateResourceRequest_WhenCreatingAResourceOtherThanSearchParameter_ThenNoCallToAddParameterMade()
        {
            var resource = Samples.GetDefaultObservation().UpdateId("id1");

            var request = new CreateResourceRequest(resource, bundleResourceContext: null);
            var wrapper = CreateResourceWrapper(resource, false);

            var response = new UpsertResourceResponse(new SaveOutcome(new RawResourceElement(wrapper), SaveOutcomeType.Created));

            var behavior = new CreateOrUpdateSearchParameterBehavior<CreateResourceRequest, UpsertResourceResponse>(_searchParameterOperations, _fhirDataStore);
            await behavior.Handle(request, async () => await Task.Run(() => response), CancellationToken.None);

            // Ensure for non-SearchParameter, that we do not call Add SearchParameter
            await _searchParameterOperations.DidNotReceive().AddSearchParameterAsync(Arg.Any<ITypedElement>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenACreateResourceRequest_WhenCreatingASearchParameterResource_ThenAddNewSearchParameterShouldBeCalled()
        {
            var searchParameter = new SearchParameter() { Id = "Id" };
            var resource = searchParameter.ToTypedElement().ToResourceElement();

            var request = new CreateResourceRequest(resource, bundleResourceContext: null);
            var wrapper = CreateResourceWrapper(resource, false);

            var response = new UpsertResourceResponse(new SaveOutcome(new RawResourceElement(wrapper), SaveOutcomeType.Created));

            var behavior = new CreateOrUpdateSearchParameterBehavior<CreateResourceRequest, UpsertResourceResponse>(_searchParameterOperations, _fhirDataStore);
            await behavior.Handle(request, async () => await Task.Run(() => response), CancellationToken.None);

            await _searchParameterOperations.Received().AddSearchParameterAsync(Arg.Any<ITypedElement>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenADeleteResourceRequest_WhenDeletingAResourceOtherThanSearchParameter_ThenNoCallToDeleteParameterMade()
        {
            var resource = Samples.GetDefaultObservation().UpdateId("id1");

            var key = new ResourceKey("Observation", "id1");
            var request = new DeleteResourceRequest(key, DeleteOperation.SoftDelete);
            var wrapper = CreateResourceWrapper(resource, false);

            _fhirDataStore.GetAsync(key, Arg.Any<CancellationToken>()).Returns(wrapper);

            var response = new DeleteResourceResponse(key);

            var behavior = new DeleteSearchParameterBehavior<DeleteResourceRequest, DeleteResourceResponse>(_searchParameterOperations, _fhirDataStore);
            await behavior.Handle(request, async () => await Task.Run(() => response), CancellationToken.None);

            // Ensure for non-SearchParameter, that we do not call Add SearchParameter
            await _searchParameterOperations.DidNotReceive().DeleteSearchParameterAsync(Arg.Any<RawResource>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenADeleteResourceRequest_WhenDeletingASearchParameterResource_TheDeleteSearchParameterShouldBeCalled()
        {
            var searchParameter = new SearchParameter() { Id = "Id" };
            var resource = searchParameter.ToTypedElement().ToResourceElement();

            var key = new ResourceKey("SearchParameter", "Id");
            var request = new DeleteResourceRequest(key, DeleteOperation.SoftDelete);
            var wrapper = CreateResourceWrapper(resource, false);

            _fhirDataStore.GetAsync(key, Arg.Any<CancellationToken>()).Returns(wrapper);

            var response = new DeleteResourceResponse(key);

            var behavior = new DeleteSearchParameterBehavior<DeleteResourceRequest, DeleteResourceResponse>(_searchParameterOperations, _fhirDataStore);
            await behavior.Handle(request, async () => await Task.Run(() => response), CancellationToken.None);

            await _searchParameterOperations.Received().DeleteSearchParameterAsync(Arg.Any<RawResource>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenADeleteResourceRequest_WhenDeletingAnAlreadyDeletedSearchParameterResource_TheDeleteSearchParameterShouldNotBeCalled()
        {
            var searchParameter = new SearchParameter() { Id = "Id" };
            var resource = searchParameter.ToTypedElement().ToResourceElement();

            var key = new ResourceKey("SearchParameter", "Id");
            var request = new DeleteResourceRequest(key, DeleteOperation.SoftDelete);
            var wrapper = CreateResourceWrapper(resource, true);

            _fhirDataStore.GetAsync(key, Arg.Any<CancellationToken>()).Returns(wrapper);

            var response = new DeleteResourceResponse(key);

            var behavior = new DeleteSearchParameterBehavior<DeleteResourceRequest, DeleteResourceResponse>(_searchParameterOperations, _fhirDataStore);
            await behavior.Handle(request,  async () => await Task.Run(() => response), CancellationToken.None);

            await _searchParameterOperations.DidNotReceive().DeleteSearchParameterAsync(Arg.Any<RawResource>(), Arg.Any<CancellationToken>());
        }

        private ResourceWrapper CreateResourceWrapper(ResourceElement resource, bool isDeleted)
        {
            return new ResourceWrapper(
                resource,
                _rawResourceFactory.Create(resource, keepMeta: true),
                new ResourceRequest(HttpMethod.Post, "http://fhir"),
                isDeleted,
                null,
                null,
                null,
                null);
        }
    }
}
