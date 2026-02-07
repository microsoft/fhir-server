// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net.Http;
using System.Threading;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
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
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();

        public SearchParameterBehaviorTests()
        {
            // Instead of using Substitute.For<RawResourceFactory>, create a mock of IRawResourceFactory
            _rawResourceFactory = Substitute.For<IRawResourceFactory>();

            // Set up the Create method with specific argument matchers
            _rawResourceFactory.Create(
                Arg.Any<ResourceElement>(),
                Arg.Any<bool>(),
                Arg.Any<bool>())
                .Returns(callInfo =>
                {
                    // Implementation for Create method
                    var resource = callInfo.ArgAt<ResourceElement>(0);
                    var keepMeta = callInfo.ArgAt<bool>(1);
                    var keepVersion = callInfo.ArgAt<bool>(2);

                    // Return a mock RawResource
                    return new RawResource("mock data", FhirResourceFormat.Json, keepMeta);
                });

            _resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
            _resourceWrapperFactory
                .Create(Arg.Any<ResourceElement>(), Arg.Any<bool>(), Arg.Any<bool>())
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
            await behavior.Handle(request, async (ct) => await Task.Run(() => response), CancellationToken.None);

            // Ensure for non-SearchParameter, that we do not call Add SearchParameter
            await _searchParameterOperations.DidNotReceive().AddSearchParameterAsync(Arg.Any<ITypedElement>(), Arg.Any<CancellationToken>());
            await _searchParameterOperations.DidNotReceive().AddSearchParameterStatusAsync(Arg.Any<ITypedElement>(), Arg.Any<CancellationToken>());
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
            await behavior.Handle(request, async (ct) => await Task.Run(() => response), CancellationToken.None);

            await _searchParameterOperations.Received().AddSearchParameterAsync(Arg.Any<ITypedElement>(), Arg.Any<CancellationToken>());
            await _searchParameterOperations.Received().AddSearchParameterStatusAsync(Arg.Any<ITypedElement>(), Arg.Any<CancellationToken>());
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

            var behavior = new DeleteSearchParameterBehavior<DeleteResourceRequest, DeleteResourceResponse>(_searchParameterOperations, _fhirDataStore, _searchParameterDefinitionManager);
            await behavior.Handle(request, async (ct) => await Task.Run(() => response), CancellationToken.None);

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

            var behavior = new DeleteSearchParameterBehavior<DeleteResourceRequest, DeleteResourceResponse>(_searchParameterOperations, _fhirDataStore, _searchParameterDefinitionManager);
            await behavior.Handle(request, async (ct) => await Task.Run(() => response), CancellationToken.None);

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

            var behavior = new DeleteSearchParameterBehavior<DeleteResourceRequest, DeleteResourceResponse>(_searchParameterOperations, _fhirDataStore, _searchParameterDefinitionManager);
            await behavior.Handle(request,  async (ct) => await Task.Run(() => response), CancellationToken.None);

            await _searchParameterOperations.DidNotReceive().DeleteSearchParameterAsync(Arg.Any<RawResource>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenADeleteResourceRequest_WhenDeletingASystemDefinedSearchParameterResource_ThenDeleteSearchParameterShouldNotBeCalled()
        {
            var searchParameter = new SearchParameter() { Id = "system-param", Url = "http://hl7.org/fhir/SearchParameter/system-param" };
            var resource = searchParameter.ToTypedElement().ToResourceElement();

            var key = new ResourceKey("SearchParameter", "system-param");
            var request = new DeleteResourceRequest(key, DeleteOperation.SoftDelete);

            // Set up a system-defined search parameter in the definition manager
            var searchParameterInfo = new SearchParameterInfo("system-param", "system-param", Microsoft.Health.Fhir.ValueSets.SearchParamType.String, new System.Uri("http://hl7.org/fhir/SearchParameter/system-param"))
            {
                IsSystemDefined = true,
            };

            _searchParameterDefinitionManager.AllSearchParameters.Returns(new[] { searchParameterInfo });

            var response = new DeleteResourceResponse(key);

            var behavior = new DeleteSearchParameterBehavior<DeleteResourceRequest, DeleteResourceResponse>(_searchParameterOperations, _fhirDataStore, _searchParameterDefinitionManager);

            // Should throw MethodNotAllowedException for system-defined parameter
            await Assert.ThrowsAsync<MethodNotAllowedException>(async () =>
                await behavior.Handle(request, async (ct) => await Task.Run(() => response), CancellationToken.None));

            // Verify DeleteSearchParameterAsync was never called
            await _searchParameterOperations.DidNotReceive().DeleteSearchParameterAsync(Arg.Any<RawResource>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnUpsertResourceRequest_WhenSearchParameterDoesNotExist_ThenAddSearchParameterShouldBeCalled()
        {
            var searchParameter = new SearchParameter() { Id = "NewId", Url = "http://example.com/new-param" };
            var resource = searchParameter.ToTypedElement().ToResourceElement();

            var key = new ResourceKey("SearchParameter", "NewId");
            var request = new UpsertResourceRequest(resource, bundleResourceContext: null);
            var wrapper = CreateResourceWrapper(resource, false);

            // Simulate ResourceNotFoundException when trying to get the previous version
            _fhirDataStore.GetAsync(key, Arg.Any<CancellationToken>()).Returns<ResourceWrapper>(x => throw new ResourceNotFoundException("Resource not found"));

            var response = new UpsertResourceResponse(new SaveOutcome(new RawResourceElement(wrapper), SaveOutcomeType.Created));

            var behavior = new CreateOrUpdateSearchParameterBehavior<UpsertResourceRequest, UpsertResourceResponse>(_searchParameterOperations, _fhirDataStore);
            await behavior.Handle(request, async (ct) => await Task.Run(() => response), CancellationToken.None);

            // Should call AddSearchParameterAsync since the resource doesn't exist
            await _searchParameterOperations.Received().AddSearchParameterAsync(Arg.Any<ITypedElement>(), Arg.Any<CancellationToken>());
            await _searchParameterOperations.Received().AddSearchParameterStatusAsync(Arg.Any<ITypedElement>(), Arg.Any<CancellationToken>());
            await _searchParameterOperations.DidNotReceive().UpdateSearchParameterAsync(Arg.Any<ITypedElement>(), Arg.Any<RawResource>(), Arg.Any<CancellationToken>());
            await _searchParameterOperations.DidNotReceive().UpdateSearchParameterStatusAsync(Arg.Any<ITypedElement>(), Arg.Any<RawResource>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnUpsertResourceRequest_WhenSearchParameterExists_ThenUpdateSearchParameterShouldBeCalled()
        {
            var oldSearchParameter = new SearchParameter() { Id = "ExistingId", Url = "http://example.com/existing-param", Version = "1" };
            var newSearchParameter = new SearchParameter() { Id = "ExistingId", Url = "http://example.com/existing-param", Version = "2" };

            var oldResource = oldSearchParameter.ToTypedElement().ToResourceElement();
            var newResource = newSearchParameter.ToTypedElement().ToResourceElement();

            var key = new ResourceKey("SearchParameter", "ExistingId");
            var request = new UpsertResourceRequest(newResource, bundleResourceContext: null);
            var oldWrapper = CreateResourceWrapper(oldResource, false);
            var newWrapper = CreateResourceWrapper(newResource, false);

            // Return existing resource when GetAsync is called
            _fhirDataStore.GetAsync(key, Arg.Any<CancellationToken>()).Returns(oldWrapper);

            var response = new UpsertResourceResponse(new SaveOutcome(new RawResourceElement(newWrapper), SaveOutcomeType.Updated));

            var behavior = new CreateOrUpdateSearchParameterBehavior<UpsertResourceRequest, UpsertResourceResponse>(_searchParameterOperations, _fhirDataStore);
            await behavior.Handle(request, async (ct) => await Task.Run(() => response), CancellationToken.None);

            // Should call UpdateSearchParameterAsync since the resource exists
            await _searchParameterOperations.DidNotReceive().AddSearchParameterAsync(Arg.Any<ITypedElement>(), Arg.Any<CancellationToken>());
            await _searchParameterOperations.DidNotReceive().AddSearchParameterStatusAsync(Arg.Any<ITypedElement>(), Arg.Any<CancellationToken>());
            await _searchParameterOperations.Received().UpdateSearchParameterAsync(Arg.Any<ITypedElement>(), Arg.Any<RawResource>(), Arg.Any<CancellationToken>());
            await _searchParameterOperations.Received().UpdateSearchParameterStatusAsync(Arg.Any<ITypedElement>(), Arg.Any<RawResource>(), Arg.Any<CancellationToken>());
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
