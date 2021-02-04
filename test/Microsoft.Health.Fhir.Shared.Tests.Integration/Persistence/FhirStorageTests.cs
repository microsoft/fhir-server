// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Health.Abstractions.Features.Transactions;
using Microsoft.Health.Core.Internal;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
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
        private readonly CapabilityStatement _capabilityStatement;
        private readonly ResourceDeserializer _deserializer;
        private readonly FhirJsonParser _fhirJsonParser;
        private readonly IFhirDataStore _dataStore;
        private ConformanceProviderBase _conformanceProvider;

        public FhirStorageTests(FhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            _capabilityStatement = fixture.CapabilityStatement;
            _deserializer = fixture.Deserializer;
            _dataStore = fixture.DataStore;
            _fhirJsonParser = fixture.JsonParser;
            _conformanceProvider = fixture.ConformanceProvider;
            Mediator = fixture.Mediator;
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
                var deserializedResource = saveResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);
                Assert.NotNull(deserializedResource);

                Assert.NotNull(deserializedResource);
                Assert.NotNull(deserializedResource);
                Assert.Equal(instant, deserializedResource.LastUpdated.GetValueOrDefault());
            }
        }

        [Fact]
        public async Task GivenAResourceId_WhenFetching_ThenTheResponseLoadsCorrectly()
        {
            var saveResult = await Mediator.CreateResourceAsync(Samples.GetJsonSample("Weight"));
            var getResult = (await Mediator.GetResourceAsync(new ResourceKey("Observation", saveResult.Id))).ToResourceElement(_deserializer);

            Assert.NotNull(getResult);
            Assert.Equal(saveResult.Id, getResult.Id);

            var observation = getResult.ToPoco<Observation>();
            Assert.NotNull(observation);
            Assert.NotNull(observation.Value);

            Quantity sq = Assert.IsType<Quantity>(observation.Value);

            Assert.Equal(67, sq.Value);
        }

        [Fact]
        public async Task GivenASavedResource_WhenUpsertIsAnUpdate_ThenTheExistingResourceIsUpdated()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams").ToPoco();
            newResourceValues.Id = saveResult.RawResourceElement.Id;

            var updateResult = await Mediator.UpsertResourceAsync(newResourceValues.ToResourceElement(), WeakETag.FromVersionId(saveResult.RawResourceElement.VersionId));
            var deserializedResource = updateResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);

            Assert.NotNull(deserializedResource);
            Assert.Equal(SaveOutcomeType.Updated, updateResult.Outcome);

            var wrapper = await _fixture.DataStore.GetAsync(new ResourceKey("Observation", deserializedResource.Id), CancellationToken.None);

            Assert.NotNull(wrapper);
            Assert.False(wrapper.RawResource.IsMetaSet);
        }

        [Fact]
        public async Task GivenAResource_WhenUpserting_ThenTheNewResourceHasMetaSet()
        {
            var instant = new DateTimeOffset(DateTimeOffset.Now.Date, TimeSpan.Zero);
            using (Mock.Property(() => ClockResolver.UtcNowFunc, () => instant))
            {
                var versionId = Guid.NewGuid().ToString();
                var resource = Samples.GetJsonSample("Weight").UpdateVersion(versionId);
                var saveResult = await Mediator.UpsertResourceAsync(resource);

                Assert.NotNull(saveResult);
                Assert.Equal(SaveOutcomeType.Created, saveResult.Outcome);

                var deserializedResource = saveResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);

                Assert.NotNull(deserializedResource);

                var wrapper = await _fixture.DataStore.GetAsync(new ResourceKey("Observation", deserializedResource.Id), CancellationToken.None);
                Assert.NotNull(wrapper);
                Assert.True(wrapper.RawResource.IsMetaSet);
                Assert.NotEqual(wrapper.Version, versionId);

                var deserialized = _fhirJsonParser.Parse<Observation>(wrapper.RawResource.Data);
                Assert.NotEqual(versionId, deserialized.VersionId);
            }
        }

        [Fact]
        public async Task GivenASavedResource_WhenUpserting_ThenMetaSetIsSetToFalse()
        {
            var versionId = Guid.NewGuid().ToString();
            var resource = Samples.GetJsonSample("Weight").UpdateVersion(versionId);
            var saveResult = await Mediator.UpsertResourceAsync(resource);

            var newResourceValues = Samples.GetJsonSample("WeightInGrams").ToPoco();
            newResourceValues.Id = saveResult.RawResourceElement.Id;

            var updateResult = await Mediator.UpsertResourceAsync(newResourceValues.ToResourceElement(), WeakETag.FromVersionId(saveResult.RawResourceElement.VersionId));

            Assert.NotNull(updateResult);
            Assert.Equal(SaveOutcomeType.Updated, updateResult.Outcome);
            var deserializedResource = updateResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);

            Assert.NotNull(deserializedResource);
            Assert.Equal(saveResult.RawResourceElement.Id, updateResult.RawResourceElement.Id);

            var wrapper = await _fixture.DataStore.GetAsync(new ResourceKey("Observation", deserializedResource.Id), CancellationToken.None);

            Assert.NotNull(wrapper);
            Assert.False(wrapper.RawResource.IsMetaSet);
            Assert.NotEqual(wrapper.Version, versionId);

            var deserialized = _fhirJsonParser.Parse<Observation>(wrapper.RawResource.Data);
            Assert.Equal("1", deserialized.VersionId);
        }

        [Theory]
        [InlineData("1")]
        [InlineData("-1")]
        [InlineData("0")]
        [InlineData("InvalidVersion")]
        public async Task GivenANonexistentResource_WhenUpsertingWithCreateEnabledAndIntegerETagHeader_TheServerShouldReturnResourceNotFoundResponse(string versionId)
        {
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
            await SetAllowCreateForOperation(
                false,
                async () =>
                {
                    await Assert.ThrowsAsync<ResourceNotFoundException>(async () =>
                        await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"), WeakETag.FromVersionId(versionId)));
                });
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

            var fetchedResult1 = (await Mediator.GetResourceAsync(new ResourceKey<Observation>(exampleId))).ToResourceElement(_deserializer);
            var fetchedResult2 = (await Mediator.GetResourceAsync(new ResourceKey<Patient>(exampleId))).ToResourceElement(_deserializer);

            Assert.Equal(weightSample.Id, fetchedResult1.Id);
            Assert.Equal(patientSample.Id, fetchedResult2.Id);

            Assert.Equal(weightSample.TypeName, fetchedResult1.InstanceType);
            Assert.Equal(patientSample.TypeName, fetchedResult2.InstanceType);
        }

        [Fact]
        public async Task GivenANonexistentResource_WhenUpsertingWithCreateDisabled_ThenAMethodNotAllowedExceptionIsThrown()
        {
            await SetAllowCreateForOperation(
                false,
                async () =>
                {
                    var ex = await Assert.ThrowsAsync<MethodNotAllowedException>(() => Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight")));

                    Assert.Equal(Resources.ResourceCreationNotAllowed, ex.Message);
                });
        }

        [Fact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.CosmosDb)]
        public async Task GivenANonexistentResourceAndCosmosDb_WhenUpsertingWithCreateDisabledAndInvalidETagHeader_ThenAResourceNotFoundIsThrown()
        {
            await SetAllowCreateForOperation(
                false,
                async () => await Assert.ThrowsAsync<ResourceNotFoundException>(() => Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"), WeakETag.FromVersionId("invalidVersion"))));
        }

        [Fact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.CosmosDb)]
        public async Task GivenANonexistentResourceAndCosmosDb_WhenUpsertingWithCreateEnabledAndInvalidETagHeader_ThenResourceNotFoundIsThrown()
        {
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"), WeakETag.FromVersionId("invalidVersion")));
        }

        [Fact]
        public async Task GivenASavedResource_WhenUpsertingWithNoETagHeader_ThenTheExistingResourceIsUpdated()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams").ToPoco();
            newResourceValues.Id = saveResult.RawResourceElement.Id;

            var updateResult = await Mediator.UpsertResourceAsync(newResourceValues.ToResourceElement());

            Assert.NotNull(updateResult);
            Assert.Equal(SaveOutcomeType.Updated, updateResult.Outcome);
            var deserializedResource = updateResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);

            Assert.NotNull(deserializedResource);
            Assert.Equal(saveResult.RawResourceElement.Id, updateResult.RawResourceElement.Id);
        }

        [Fact]
        public async Task GivenASavedResource_WhenConcurrentlyUpsertingWithNoETagHeader_ThenTheExistingResourceIsUpdated()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams").ToPoco<Resource>();
            newResourceValues.Id = saveResult.RawResourceElement.Id;

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

            var deserializedList = new List<Observation>();

            foreach (var item in list)
            {
                Assert.Equal(SaveOutcomeType.Updated, item.Result.Outcome);

                deserializedList.Add(item.Result.RawResourceElement.ToPoco<Observation>(Deserializers.ResourceDeserializer));
            }

            var allObservations = deserializedList.Select(x => ((Quantity)x.Value).Value.GetValueOrDefault()).Distinct();
            Assert.Equal(itemsToCreate, allObservations.Count());
        }

        [Fact]
        public async Task GivenAResourceWithNoHistory_WhenFetchingByVersionId_ThenReadWorksCorrectly()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
            var deserialized = saveResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);
            var result = (await Mediator.GetResourceAsync(new ResourceKey(deserialized.InstanceType, deserialized.Id, deserialized.VersionId))).ToResourceElement(_deserializer);

            Assert.NotNull(result);
            Assert.Equal(deserialized.Id, result.Id);
        }

        [Fact]
        public async Task UpdatingAResource_ThenWeCanAccessHistoricValues()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams")
                .UpdateId(saveResult.RawResourceElement.Id);

            var updateResult = await Mediator.UpsertResourceAsync(newResourceValues, WeakETag.FromVersionId(saveResult.RawResourceElement.VersionId));

            var getV1Result = (await Mediator.GetResourceAsync(new ResourceKey<Observation>(saveResult.RawResourceElement.Id, saveResult.RawResourceElement.VersionId))).ToResourceElement(_deserializer);

            Assert.NotNull(getV1Result);
            Assert.Equal(saveResult.RawResourceElement.Id, getV1Result.Id);
            Assert.Equal(updateResult.RawResourceElement.Id, getV1Result.Id);

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
                .UpdateId(saveResult.RawResourceElement.Id);

            var updateResult = await Mediator.UpsertResourceAsync(newResourceValues, WeakETag.FromVersionId(saveResult.RawResourceElement.VersionId));

            await Assert.ThrowsAsync<ResourceNotFoundException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Organization>(saveResult.RawResourceElement.Id, saveResult.RawResourceElement.VersionId)));
        }

        [Fact]
        public async Task WhenDeletingAResource_ThenWeGetResourceGone()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var deletedResourceKey = await Mediator.DeleteResourceAsync(new ResourceKey("Observation", saveResult.RawResourceElement.Id), false);

            Assert.NotEqual(saveResult.RawResourceElement.VersionId, deletedResourceKey.ResourceKey.VersionId);

            await Assert.ThrowsAsync<ResourceGoneException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Observation>(saveResult.RawResourceElement.Id)));
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

            var resourceKey = new ResourceKey("Observation", saveResult.RawResourceElement.Id);

            await Mediator.DeleteResourceAsync(resourceKey, false);

            var deletedResourceKey2 = await Mediator.DeleteResourceAsync(resourceKey, false);

            Assert.Null(deletedResourceKey2.ResourceKey.VersionId);

            await Assert.ThrowsAsync<ResourceGoneException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Observation>(saveResult.RawResourceElement.Id)));
        }

        [Fact]
        public async Task WhenHardDeletingAResource_ThenWeGetResourceNotFound()
        {
            object snapshotToken = await _fixture.TestHelper.GetSnapshotToken();

            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var deletedResourceKey = await Mediator.DeleteResourceAsync(new ResourceKey("Observation", saveResult.RawResourceElement.Id), true);

            Assert.Null(deletedResourceKey.ResourceKey.VersionId);

            // Subsequent get should result in NotFound.
            await Assert.ThrowsAsync<ResourceNotFoundException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Observation>(saveResult.RawResourceElement.Id)));

            // Subsequent version get should result in NotFound.
            await Assert.ThrowsAsync<ResourceNotFoundException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Observation>(saveResult.RawResourceElement.Id, saveResult.RawResourceElement.VersionId)));

            await _fixture.TestHelper.ValidateSnapshotTokenIsCurrent(snapshotToken);
        }

        [Fact]
        public async Task WhenHardDeletingAResource_ThenHistoryShouldBeDeleted()
        {
            object snapshotToken = await _fixture.TestHelper.GetSnapshotToken();

            var createResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
            var deserializedResult = createResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);
            string resourceId = createResult.RawResourceElement.Id;

            var deleteResult = await Mediator.DeleteResourceAsync(new ResourceKey("Observation", resourceId), false);
            var updateResult = await Mediator.UpsertResourceAsync(deserializedResult);

            // Hard-delete the resource.
            var deletedResourceKey = await Mediator.DeleteResourceAsync(new ResourceKey("Observation", resourceId), true);

            Assert.Null(deletedResourceKey.ResourceKey.VersionId);

            // Subsequent get should result in NotFound.
            await Assert.ThrowsAsync<ResourceNotFoundException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Observation>(resourceId)));

            // Subsequent version get should result in NotFound.
            foreach (string versionId in new[] { createResult.RawResourceElement.VersionId, deleteResult.ResourceKey.VersionId, updateResult.RawResourceElement.VersionId })
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
                async () => { await Mediator.GetResourceAsync(new ResourceKey<Observation>(saveResult.RawResourceElement.Id, Guid.NewGuid().ToString())); });
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
            var deletedResourceKey = await Mediator.DeleteResourceAsync(new ResourceKey("Observation", saveResult.RawResourceElement.Id), false);

            Assert.NotEqual(saveResult.RawResourceElement.VersionId, deletedResourceKey.ResourceKey.VersionId);
            await Assert.ThrowsAsync<ResourceGoneException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Observation>(saveResult.RawResourceElement.Id)));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams").ToPoco();
            newResourceValues.Id = saveResult.RawResourceElement.Id;

            var updateResult = await Mediator.UpsertResourceAsync(newResourceValues.ToResourceElement(), deletedResourceKey.WeakETag);

            Assert.NotNull(updateResult);
            Assert.Equal(SaveOutcomeType.Updated, updateResult.Outcome);

            Assert.NotNull(updateResult.RawResourceElement);
            Assert.Equal(saveResult.RawResourceElement.Id, updateResult.RawResourceElement.Id);
        }

        [Fact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenATransactionHandler_WhenATransactionIsCommitted_ThenTheResourceShouldBeCreated()
        {
            string createdId = string.Empty;

            using (ITransactionScope transactionScope = _fixture.TransactionHandler.BeginTransaction())
            {
                SaveOutcome saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
                createdId = saveResult.RawResourceElement.Id;

                Assert.NotEqual(string.Empty, createdId);

                transactionScope.Complete();
            }

            ResourceElement getResult = (await Mediator.GetResourceAsync(new ResourceKey<Observation>(createdId))).ToResourceElement(_deserializer);

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
                createdId1 = saveResult.RawResourceElement.Id;

                Assert.NotEqual(string.Empty, createdId1);

                transactionScope.Complete();
            }

            using (ITransactionScope transactionScope = _fixture.TransactionHandler.BeginTransaction())
            {
                SaveOutcome saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
                createdId2 = saveResult.RawResourceElement.Id;

                Assert.NotEqual(string.Empty, createdId2);

                transactionScope.Complete();
            }

            ResourceElement getResult1 = (await Mediator.GetResourceAsync(new ResourceKey<Observation>(createdId1))).ToResourceElement(_deserializer);
            Assert.Equal(createdId1, getResult1.Id);

            ResourceElement getResult2 = (await Mediator.GetResourceAsync(new ResourceKey<Observation>(createdId2))).ToResourceElement(_deserializer);
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
                createdId = saveResult.RawResourceElement.Id;

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
                        createdId = saveResult.RawResourceElement.Id;

                        Assert.NotEqual(string.Empty, createdId);

                        await Mediator.GetResourceAsync(new ResourceKey<Observation>(randomNotFoundId));

                        transactionScope.Complete();
                    }
                });

            await Assert.ThrowsAsync<ResourceNotFoundException>(
                async () => { await Mediator.GetResourceAsync(new ResourceKey<Observation>(createdId)); });
        }

        [Fact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.CosmosDb)]
        public async Task GivenAnUpdatedResource_WhenUpdateSearchIndexForResourceAsync_ThenResourceGetsUpdated()
        {
            ResourceElement patientResource = Samples.GetJsonSample("Patient");
            SaveOutcome upsertResult = await Mediator.UpsertResourceAsync(patientResource);

            (ResourceWrapper original, ResourceWrapper updated) = await CreateUpdatedWrapperFromExistingResource(upsertResult);

            ResourceWrapper replaceResult = await _dataStore.UpdateSearchIndexForResourceAsync(updated, WeakETag.FromVersionId(original.Version), CancellationToken.None);

            Assert.Equal(original.ResourceId, replaceResult.ResourceId);
            Assert.Equal(original.Version, replaceResult.Version);
            Assert.Equal(original.ResourceTypeName, replaceResult.ResourceTypeName);
            Assert.Equal(original.LastModified, replaceResult.LastModified);
            Assert.NotEqual((original as FhirCosmosResourceWrapper).ETag, (replaceResult as FhirCosmosResourceWrapper).ETag);
        }

        [Fact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.CosmosDb)]
        public async Task GivenAnUpdatedResourceWithWrongWeakETag_WhenUpdateSearchIndexForResourceAsync_ThenExceptionIsThrown()
        {
            ResourceElement patientResource = Samples.GetJsonSample("Patient");
            SaveOutcome upsertResult = await Mediator.UpsertResourceAsync(patientResource);

            (ResourceWrapper originalWrapper, ResourceWrapper updatedWrapper) = await CreateUpdatedWrapperFromExistingResource(upsertResult);
            UpsertOutcome upsertOutcome = await _dataStore.UpsertAsync(updatedWrapper, WeakETag.FromVersionId(originalWrapper.Version), allowCreate: false, keepHistory: false, CancellationToken.None);

            // Let's update the resource again with new information.
            var searchParamInfo = new SearchParameterInfo("newSearchParam2", "newSearchParam2");
            var searchIndex = new SearchIndexEntry(searchParamInfo, new TokenSearchValue("system", "code", "text"));
            var searchIndices = new List<SearchIndexEntry>() { searchIndex };

            updatedWrapper = new ResourceWrapper(
                originalWrapper.ResourceId,
                originalWrapper.Version,
                originalWrapper.ResourceTypeName,
                originalWrapper.RawResource,
                originalWrapper.Request,
                originalWrapper.LastModified,
                deleted: false,
                searchIndices,
                originalWrapper.CompartmentIndices,
                originalWrapper.LastModifiedClaims);

            // Attempt to replace resource with the old weaketag
            await Assert.ThrowsAsync<PreconditionFailedException>(() => _dataStore.UpdateSearchIndexForResourceAsync(updatedWrapper, WeakETag.FromVersionId(originalWrapper.Version), CancellationToken.None));
        }

        [Fact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.CosmosDb)]
        public async Task GivenAnUpdatedResourceWithWrongResourceId_WhenUpdateSearchIndexForResourceAsync_ThenExceptionIsThrown()
        {
            ResourceElement patientResource = Samples.GetJsonSample("Patient");
            SaveOutcome upsertResult = await Mediator.UpsertResourceAsync(patientResource);

            (ResourceWrapper original, ResourceWrapper updated) = await CreateUpdatedWrapperFromExistingResource(upsertResult, Guid.NewGuid().ToString());
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => _dataStore.UpdateSearchIndexForResourceAsync(updated, WeakETag.FromVersionId(original.Version), CancellationToken.None));
        }

        private async Task<(ResourceWrapper original, ResourceWrapper updated)> CreateUpdatedWrapperFromExistingResource(
            SaveOutcome upsertResult,
            string updatedId = null)
        {
            // Get wrapper from data store directly
            ResourceKey resourceKey = new ResourceKey(upsertResult.RawResourceElement.InstanceType, upsertResult.RawResourceElement.Id, upsertResult.RawResourceElement.VersionId);
            FhirCosmosResourceWrapper originalWrapper = (FhirCosmosResourceWrapper)await _dataStore.GetAsync(resourceKey, CancellationToken.None);

            // Add new search index entry to existing wrapper.
            SearchParameterInfo searchParamInfo = new SearchParameterInfo("newSearchParam", "newSearchParam");
            SearchIndexEntry searchIndex = new SearchIndexEntry(searchParamInfo, new NumberSearchValue(12));
            List<SearchIndexEntry> searchIndices = new List<SearchIndexEntry>() { searchIndex };

            var updatedWrapper = new ResourceWrapper(
                updatedId == null ? originalWrapper.Id : updatedId,
                originalWrapper.Version,
                originalWrapper.ResourceTypeName,
                originalWrapper.RawResource,
                originalWrapper.Request,
                originalWrapper.LastModified,
                deleted: false,
                searchIndices,
                originalWrapper.CompartmentIndices,
                originalWrapper.LastModifiedClaims);

            return (originalWrapper, updatedWrapper);
        }

        private async Task ExecuteAndVerifyException<TException>(Func<Task> action)
            where TException : Exception
        {
            await Assert.ThrowsAsync<TException>(action);
        }

        private async Task SetAllowCreateForOperation(bool allowCreate, Func<Task> operation)
        {
            var observation = _capabilityStatement.Rest[0].Resource.Find(r => r.Type == ResourceType.Observation);
            var originalValue = observation.UpdateCreate;
            observation.UpdateCreate = allowCreate;
            observation.Versioning = CapabilityStatement.ResourceVersionPolicy.Versioned;
            _conformanceProvider.ClearCache();

            try
            {
                await operation();
            }
            finally
            {
                observation.UpdateCreate = originalValue;
                _conformanceProvider.ClearCache();
            }
        }
    }
}
