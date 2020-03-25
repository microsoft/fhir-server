// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Core.Internal;
using Microsoft.Health.Fhir.Core;
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
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Common.Mocks;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Tests for storage layer.
    /// </summary>
    [FhirStorageTestsFixtureArgumentSets(DataStore.All)]
    public partial class FhirStorageTests : IClassFixture<FhirStorageTestsFixture>
    {
        private readonly FhirStorageTestsFixture _fixture;
        private readonly CapabilityStatement _conformance;
        private readonly ISearchService _searchService;

        public FhirStorageTests(FhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            _searchService = Substitute.For<ISearchService>();
            IFhirDataStore dataStore = fixture.DataStore;

            _conformance = CapabilityStatementMock.GetMockedCapabilityStatement();

            CapabilityStatementMock.SetupMockResource(_conformance, ResourceType.Observation, null);
            var observationResource = _conformance.Rest[0].Resource.Find(r => r.Type == ResourceType.Observation);
            observationResource.UpdateCreate = true;
            observationResource.Versioning = CapabilityStatement.ResourceVersionPolicy.Versioned;

            CapabilityStatementMock.SetupMockResource(_conformance, ResourceType.Organization, null);
            var organizationResource = _conformance.Rest[0].Resource.Find(r => r.Type == ResourceType.Organization);
            organizationResource.UpdateCreate = true;
            organizationResource.Versioning = CapabilityStatement.ResourceVersionPolicy.NoVersion;

            var provider = Substitute.For<ConformanceProviderBase>();
            provider.GetCapabilityStatementAsync().Returns(_conformance.ToTypedElement().ToResourceElement());

            // TODO: FhirRepository instantiate ResourceDeserializer class directly
            // which will try to deserialize the raw resource. We should mock it as well.
            var rawResourceFactory = Substitute.For<RawResourceFactory>(new FhirJsonSerializer());

            var resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
            resourceWrapperFactory
                .Create(Arg.Any<ResourceElement>(), Arg.Any<bool>())
                .Returns(x =>
                {
                    ResourceElement resource = x.ArgAt<ResourceElement>(0);

                    return new ResourceWrapper(resource, rawResourceFactory.Create(resource), new ResourceRequest(HttpMethod.Post, "http://fhir"), x.ArgAt<bool>(1), null, null, null);
                });

            var collection = new ServiceCollection();

            var resourceIdProvider = new ResourceIdProvider();

            collection.AddSingleton(typeof(IRequestHandler<CreateResourceRequest, UpsertResourceResponse>), new CreateResourceHandler(dataStore, new Lazy<IConformanceProvider>(() => provider), resourceWrapperFactory, resourceIdProvider, new ResourceReferenceResolver(_searchService, new TestQueryStringParser()), DisabledFhirAuthorizationService.Instance));
            collection.AddSingleton(typeof(IRequestHandler<UpsertResourceRequest, UpsertResourceResponse>), new UpsertResourceHandler(dataStore, new Lazy<IConformanceProvider>(() => provider), resourceWrapperFactory, resourceIdProvider, DisabledFhirAuthorizationService.Instance, ModelInfoProvider.Instance));
            collection.AddSingleton(typeof(IRequestHandler<GetResourceRequest, GetResourceResponse>), new GetResourceHandler(dataStore, new Lazy<IConformanceProvider>(() => provider), resourceWrapperFactory, Deserializers.ResourceDeserializer, resourceIdProvider, DisabledFhirAuthorizationService.Instance));
            collection.AddSingleton(typeof(IRequestHandler<DeleteResourceRequest, DeleteResourceResponse>), new DeleteResourceHandler(dataStore, new Lazy<IConformanceProvider>(() => provider), resourceWrapperFactory, resourceIdProvider, DisabledFhirAuthorizationService.Instance));

            ServiceProvider services = collection.BuildServiceProvider();

            Mediator = new Mediator(type => services.GetService(type));
        }

        protected Mediator Mediator { get; }

        [Fact]
        public async Task GivenAResource_WhenSaving_ThenTheMetaIsUpdated()
        {
            var instant = new DateTimeOffset(DateTimeOffset.Now.Date, TimeSpan.Zero);
            using (Mock.Property(() => ClockResolver.UtcNowFunc, () => instant))
            {
                var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

                Assert.NotNull(saveResult);
                Assert.Equal(SaveOutcomeType.Created, saveResult.Outcome);
                Assert.NotNull(saveResult.Resource);

                Assert.NotNull(saveResult.Resource.Id);
                Assert.NotNull(saveResult.Resource.VersionId);
                Assert.Equal(instant, saveResult.Resource.LastUpdated.GetValueOrDefault());
            }
        }

        [Fact]
        public async Task GivenAResourceId_WhenFetching_ThenTheResponseLoadsCorrectly()
        {
            var saveResult = await Mediator.CreateResourceAsync(Samples.GetJsonSample("Weight"));
            var getResult = await Mediator.GetResourceAsync(new ResourceKey("Observation", saveResult.Id));

            Assert.NotNull(getResult);
            Assert.Equal(saveResult.Id, getResult.Id);

            var observation = getResult.Instance.ToPoco<Observation>();
            Assert.NotNull(observation);
            Assert.NotNull(observation.Value);

            Quantity sq = Assert.IsType<Quantity>(observation.Value);

            Assert.Equal(67, sq.Value);
        }

        [Fact]
        public async Task GivenASavedResource_WhenUpserting_ThenTheExistingResourceIsUpdated()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams").ToPoco();
            newResourceValues.Id = saveResult.Resource.Id;

            var updateResult = await Mediator.UpsertResourceAsync(newResourceValues.ToResourceElement(), WeakETag.FromVersionId(saveResult.Resource.VersionId));

            Assert.NotNull(updateResult);
            Assert.Equal(SaveOutcomeType.Updated, updateResult.Outcome);

            Assert.NotNull(updateResult.Resource);
            Assert.Equal(saveResult.Resource.Id, updateResult.Resource.Id);
        }

        [Theory]
        [InlineData("1")]
        [InlineData("-1")]
        [InlineData("0")]
        [InlineData("InvalidVersion")]
        public async Task GivenANonexistentResource_WhenUpsertingWithCreateEnabledAndIntegerETagHeader_TheServerShouldReturnResourceNotFoundResponse(string versionId)
        {
            SetAllowCreate(true);

            await Assert.ThrowsAsync<ResourceNotFoundException>(async () =>
                await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"), WeakETag.FromVersionId(versionId)));
        }

        [Theory]
        [InlineData("1")]
        [InlineData("-1")]
        [InlineData("0")]
        [InlineData("InvalidVersion")]
        public async Task GivenANonexistentResource_WhenUpsertingWithCreateDisabledAndIntegerETagHeader_TheServerShouldReturnResourceNotFoundResponse(string versionId)
        {
            SetAllowCreate(false);

            await Assert.ThrowsAsync<ResourceNotFoundException>(async () =>
                await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"), WeakETag.FromVersionId(versionId)));
        }

        [Fact]
        public async Task GivenAResource_WhenUpsertingDifferentTypeWithTheSameId_ThenTheExistingResourceIsNotOverridden()
        {
            var weightSample = Samples.GetJsonSample("Weight").ToPoco();
            var patientSample = Samples.GetJsonSample("Patient").ToPoco();

            var exampleId = Guid.NewGuid().ToString();

            weightSample.Id = exampleId;
            patientSample.Id = exampleId;

            await Mediator.UpsertResourceAsync(weightSample.ToResourceElement());
            await Mediator.UpsertResourceAsync(patientSample.ToResourceElement());

            var fetchedResult1 = await Mediator.GetResourceAsync(new ResourceKey<Observation>(exampleId));
            var fetchedResult2 = await Mediator.GetResourceAsync(new ResourceKey<Patient>(exampleId));

            Assert.Equal(weightSample.Id, fetchedResult1.Id);
            Assert.Equal(patientSample.Id, fetchedResult2.Id);

            Assert.Equal(weightSample.TypeName, fetchedResult1.InstanceType);
            Assert.Equal(patientSample.TypeName, fetchedResult2.InstanceType);
        }

        [Fact]
        public async Task GivenANonexistentResource_WhenUpsertingWithCreateDisabled_ThenAMethodNotAllowedExceptionIsThrown()
        {
            SetAllowCreate(false);
            var ex = await Assert.ThrowsAsync<MethodNotAllowedException>(() => Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight")));

            Assert.Equal(Resources.ResourceCreationNotAllowed, ex.Message);
        }

        [Fact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.CosmosDb)]
        public async Task GivenANonexistentResourceAndCosmosDb_WhenUpsertingWithCreateDisabledAndInvalidETagHeader_ThenAResourceNotFoundIsThrown()
        {
            SetAllowCreate(false);

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"), WeakETag.FromVersionId("invalidVersion")));
        }

        [Fact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.CosmosDb)]
        public async Task GivenANonexistentResourceAndCosmosDb_WhenUpsertingWithCreateEnabledAndInvalidETagHeader_ThenResourceNotFoundIsThrown()
        {
            SetAllowCreate(true);

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"), WeakETag.FromVersionId("invalidVersion")));
        }

        [Fact]
        public async Task GivenASavedResource_WhenUpsertingWithNoETagHeader_ThenTheExistingResourceIsUpdated()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams").ToPoco();
            newResourceValues.Id = saveResult.Resource.Id;

            var updateResult = await Mediator.UpsertResourceAsync(newResourceValues.ToResourceElement());

            Assert.NotNull(updateResult);
            Assert.Equal(SaveOutcomeType.Updated, updateResult.Outcome);

            Assert.NotNull(updateResult.Resource);
            Assert.Equal(saveResult.Resource.Id, updateResult.Resource.Id);
        }

        [Fact]
        public async Task GivenASavedResource_WhenConcurrentlyUpsertingWithNoETagHeader_ThenTheExistingResourceIsUpdated()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams").Instance.ToPoco<Resource>();
            newResourceValues.Id = saveResult.Resource.Id;

            var list = new List<Task<SaveOutcome>>();

            Resource CloneResource(int value)
            {
                var newResource = (Observation)newResourceValues.DeepCopy();
                newResource.Value = new Quantity(value, "kg");
                return newResource;
            }

            var itemsToCreate = 10;
            for (int i = 0; i < itemsToCreate; i++)
            {
                list.Add(Mediator.UpsertResourceAsync(CloneResource(i).ToResourceElement()));
            }

            await Task.WhenAll(list);

            foreach (var item in list)
            {
                Assert.Equal(SaveOutcomeType.Updated, item.Result.Outcome);
            }

            var allObservations = list.Select(x => ((Quantity)x.Result.Resource.Instance.ToPoco<Observation>().Value).Value.GetValueOrDefault()).Distinct();
            Assert.Equal(itemsToCreate, allObservations.Count());
        }

        [Fact]
        public async Task GivenAResourceWithNoHistory_WhenFetchingByVersionId_ThenReadWorksCorrectly()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var result = await Mediator.GetResourceAsync(new ResourceKey(saveResult.Resource.InstanceType, saveResult.Resource.Id, saveResult.Resource.VersionId));

            Assert.NotNull(result);
            Assert.Equal(saveResult.Resource.Id, result.Id);
        }

        [Fact]
        public async Task UpdatingAResource_ThenWeCanAccessHistoricValues()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams")
                .UpdateId(saveResult.Resource.Id);

            var updateResult = await Mediator.UpsertResourceAsync(newResourceValues, WeakETag.FromVersionId(saveResult.Resource.VersionId));

            var getV1Result = await Mediator.GetResourceAsync(new ResourceKey<Observation>(saveResult.Resource.Id, saveResult.Resource.VersionId));

            Assert.NotNull(getV1Result);
            Assert.Equal(saveResult.Resource.Id, getV1Result.Id);
            Assert.Equal(updateResult.Resource.Id, getV1Result.Id);

            var oldObservation = getV1Result.ToPoco<Observation>();
            Assert.NotNull(oldObservation);
            Assert.NotNull(oldObservation.Value);

            Quantity sq = Assert.IsType<Quantity>(oldObservation.Value);

            Assert.Equal(67, sq.Value);
        }

        [Fact]
        public async Task UpdatingAResourceWithNoHistory_ThenWeCannotAccessHistoricValues()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetDefaultOrganization());

            var newResourceValues = Samples.GetDefaultOrganization()
                .UpdateId(saveResult.Resource.Id);

            var updateResult = await Mediator.UpsertResourceAsync(newResourceValues, WeakETag.FromVersionId(saveResult.Resource.VersionId));

            await Assert.ThrowsAsync<ResourceNotFoundException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Organization>(saveResult.Resource.Id, saveResult.Resource.VersionId)));
        }

        [Fact]
        public async Task WhenDeletingAResource_ThenWeGetResourceGone()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var deletedResourceKey = await Mediator.DeleteResourceAsync(new ResourceKey("Observation", saveResult.Resource.Id), false);

            Assert.NotEqual(saveResult.Resource.VersionId, deletedResourceKey.ResourceKey.VersionId);

            await Assert.ThrowsAsync<ResourceGoneException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Observation>(saveResult.Resource.Id)));
        }

        [Fact]
        public async Task WhenDeletingAResourceThatNeverExisted_ThenReadingTheResourceReturnsNotFound()
        {
            string id = "missingid";

            var deletedResourceKey = await Mediator.DeleteResourceAsync(new ResourceKey("Observation", id), false);

            Assert.Null(deletedResourceKey.ResourceKey.VersionId);

            await Assert.ThrowsAsync<ResourceNotFoundException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Observation>(id)));
        }

        [Fact]
        public async Task WhenDeletingAResourceForASecondTime_ThenWeDoNotGetANewVersion()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var resourceKey = new ResourceKey("Observation", saveResult.Resource.Id);

            await Mediator.DeleteResourceAsync(resourceKey, false);

            var deletedResourceKey2 = await Mediator.DeleteResourceAsync(resourceKey, false);

            Assert.Null(deletedResourceKey2.ResourceKey.VersionId);

            await Assert.ThrowsAsync<ResourceGoneException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Observation>(saveResult.Resource.Id)));
        }

        [Fact]
        public async Task WhenHardDeletingAResource_ThenWeGetResourceNotFound()
        {
            object snapshotToken = await _fixture.TestHelper.GetSnapshotToken();

            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var deletedResourceKey = await Mediator.DeleteResourceAsync(new ResourceKey("Observation", saveResult.Resource.Id), true);

            Assert.Null(deletedResourceKey.ResourceKey.VersionId);

            // Subsequent get should result in NotFound.
            await Assert.ThrowsAsync<ResourceNotFoundException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Observation>(saveResult.Resource.Id)));

            // Subsequent version get should result in NotFound.
            await Assert.ThrowsAsync<ResourceNotFoundException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Observation>(saveResult.Resource.Id, saveResult.Resource.VersionId)));

            await _fixture.TestHelper.ValidateSnapshotTokenIsCurrent(snapshotToken);
        }

        [Fact]
        public async Task WhenHardDeletingAResource_ThenHistoryShouldBeDeleted()
        {
            object snapshotToken = await _fixture.TestHelper.GetSnapshotToken();

            var createResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            string resourceId = createResult.Resource.Id;

            var deleteResult = await Mediator.DeleteResourceAsync(new ResourceKey("Observation", resourceId), false);
            var updateResult = await Mediator.UpsertResourceAsync(createResult.Resource);

            // Hard-delete the resource.
            var deletedResourceKey = await Mediator.DeleteResourceAsync(new ResourceKey("Observation", resourceId), true);

            Assert.Null(deletedResourceKey.ResourceKey.VersionId);

            // Subsequent get should result in NotFound.
            await Assert.ThrowsAsync<ResourceNotFoundException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Observation>(resourceId)));

            // Subsequent version get should result in NotFound.
            foreach (string versionId in new[] { createResult.Resource.VersionId, deleteResult.ResourceKey.VersionId, updateResult.Resource.VersionId })
            {
                await Assert.ThrowsAsync<ResourceNotFoundException>(
                    () => Mediator.GetResourceAsync(new ResourceKey<Observation>(resourceId, versionId)));
            }

            await _fixture.TestHelper.ValidateSnapshotTokenIsCurrent(snapshotToken);
        }

        [Fact]
        public async Task GivenAResourceSavedInRepository_AccessingANonValidVersion_ThenGetsNotFound()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            await Assert.ThrowsAsync<ResourceNotFoundException>(
                async () => { await Mediator.GetResourceAsync(new ResourceKey<Observation>(saveResult.Resource.Id, Guid.NewGuid().ToString())); });
        }

        [Fact]
        public async Task WhenGettingNonExistentResource_ThenNotFoundIsThrown()
        {
            await Assert.ThrowsAsync<ResourceNotFoundException>(
                async () => { await Mediator.GetResourceAsync(new ResourceKey<Observation>(Guid.NewGuid().ToString())); });
        }

        [Fact]
        public async Task WhenDeletingSpecificVersion_ThenMethodNotAllowedIsThrown()
        {
            await Assert.ThrowsAsync<MethodNotAllowedException>(
                async () => { await Mediator.DeleteResourceAsync(new ResourceKey<Observation>(Guid.NewGuid().ToString(), Guid.NewGuid().ToString()), false); });
        }

        [Fact]
        public async Task GivenADeletedResource_WhenUpsertingWithValidETagHeader_ThenTheDeletedResourceIsRevived()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
            var deletedResourceKey = await Mediator.DeleteResourceAsync(new ResourceKey("Observation", saveResult.Resource.Id), false);

            Assert.NotEqual(saveResult.Resource.VersionId, deletedResourceKey.ResourceKey.VersionId);
            await Assert.ThrowsAsync<ResourceGoneException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Observation>(saveResult.Resource.Id)));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams").ToPoco();
            newResourceValues.Id = saveResult.Resource.Id;

            var updateResult = await Mediator.UpsertResourceAsync(newResourceValues.ToResourceElement(), deletedResourceKey.WeakETag);

            Assert.NotNull(updateResult);
            Assert.Equal(SaveOutcomeType.Updated, updateResult.Outcome);

            Assert.NotNull(updateResult.Resource);
            Assert.Equal(saveResult.Resource.Id, updateResult.Resource.Id);
        }

        [Fact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenATransactionHandler_WhenATransactionIsCommitted_ThenTheResourceShouldBeCreated()
        {
            string createdId = string.Empty;

            using (ITransactionScope transactionScope = _fixture.TransactionHandler.BeginTransaction())
            {
                SaveOutcome saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
                createdId = saveResult.Resource.Id;

                Assert.NotEqual(string.Empty, createdId);

                transactionScope.Complete();
            }

            ResourceElement getResult = await Mediator.GetResourceAsync(new ResourceKey<Observation>(createdId));

            Assert.Equal(createdId, getResult.Id);
        }

        [Fact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenACompletedTransaction_WhenStartingASecondTransactionCommitted_ThenTheResourceShouldBeCreated()
        {
            string createdId1;
            string createdId2;

            using (ITransactionScope transactionScope = _fixture.TransactionHandler.BeginTransaction())
            {
                SaveOutcome saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
                createdId1 = saveResult.Resource.Id;

                Assert.NotEqual(string.Empty, createdId1);

                transactionScope.Complete();
            }

            using (ITransactionScope transactionScope = _fixture.TransactionHandler.BeginTransaction())
            {
                SaveOutcome saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
                createdId2 = saveResult.Resource.Id;

                Assert.NotEqual(string.Empty, createdId2);

                transactionScope.Complete();
            }

            ResourceElement getResult1 = await Mediator.GetResourceAsync(new ResourceKey<Observation>(createdId1));
            Assert.Equal(createdId1, getResult1.Id);

            ResourceElement getResult2 = await Mediator.GetResourceAsync(new ResourceKey<Observation>(createdId2));
            Assert.Equal(createdId2, getResult2.Id);
        }

        [Fact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenATransactionHandler_WhenATransactionIsNotCommitted_ThenNothingShouldBeCreated()
        {
            string createdId = string.Empty;

            using (_ = _fixture.TransactionHandler.BeginTransaction())
            {
                SaveOutcome saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
                createdId = saveResult.Resource.Id;

                Assert.NotEqual(string.Empty, createdId);
            }

            await Assert.ThrowsAsync<ResourceNotFoundException>(
                async () => { await Mediator.GetResourceAsync(new ResourceKey<Observation>(createdId)); });
        }

        [Fact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenATransactionHandler_WhenATransactionFailsFailedRequest_ThenNothingShouldCommit()
        {
            string createdId = string.Empty;
            string randomNotFoundId = Guid.NewGuid().ToString();

            await Assert.ThrowsAsync<ResourceNotFoundException>(
                async () =>
                {
                    using (ITransactionScope transactionScope = _fixture.TransactionHandler.BeginTransaction())
                    {
                        SaveOutcome saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
                        createdId = saveResult.Resource.Id;

                        Assert.NotEqual(string.Empty, createdId);

                        await Mediator.GetResourceAsync(new ResourceKey<Observation>(randomNotFoundId));

                        transactionScope.Complete();
                    }
                });

            await Assert.ThrowsAsync<ResourceNotFoundException>(
                async () => { await Mediator.GetResourceAsync(new ResourceKey<Observation>(createdId)); });
        }

        private async Task ExecuteAndVerifyException<TException>(Func<Task> action)
            where TException : Exception
        {
            await Assert.ThrowsAsync<TException>(action);
        }

        private void SetAllowCreate(bool allowCreate)
        {
            var observation = _conformance.Rest[0].Resource.Find(r => r.Type == ResourceType.Observation);
            observation.UpdateCreate = allowCreate;
            observation.Versioning = CapabilityStatement.ResourceVersionPolicy.Versioned;
        }
    }
}
