// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Abstractions.Features.Transactions;
using Microsoft.Health.Core.Internal;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
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
        private readonly CapabilityStatement _capabilityStatement;
        private readonly ResourceDeserializer _deserializer;
        private readonly FhirJsonParser _fhirJsonParser;
        private readonly IFhirDataStore _dataStore;
        private readonly SearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ConformanceProviderBase _conformanceProvider;
        private readonly ISearchIndexer _searchIndexer = Substitute.For<ISearchIndexer>();
        private const string ContentUpdated = "Updated resource content";

        public FhirStorageTests(FhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            _capabilityStatement = fixture.CapabilityStatement;
            _deserializer = fixture.Deserializer;
            _dataStore = fixture.DataStore;
            _fhirJsonParser = fixture.JsonParser;
            _conformanceProvider = fixture.ConformanceProvider;
            _searchParameterDefinitionManager = fixture.SearchParameterDefinitionManager;
            Mediator = fixture.Mediator;
        }

        protected Mediator Mediator { get; }

        [Fact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
        public async Task TimeTravel()
        {
            await _fixture.SqlHelper.ExecuteSqlCmd("DELETE FROM dbo.Resource"); // remove all data

            // add resource
            var type = "Patient";
            var patient = (Patient)Samples.GetJsonSample(type).ToPoco();
            patient.Id = Guid.NewGuid().ToString();
            await Mediator.UpsertResourceAsync(patient.ToResourceElement());

            await Task.Delay(100); // avoid time -> surrogate id -> time round trip error
            var till = DateTime.UtcNow;

            var results = await _fixture.SearchService.SearchAsync(type, new List<Tuple<string, string>>(), CancellationToken.None);
            Assert.Single(results.Results);
            var resource = results.Results.First().Resource;
            Assert.Equal("1", resource.Version);

            // add till and check that resource is returned
            var queryParameters = new[] { Tuple.Create(KnownQueryParameterNames.LastUpdated, $"le{new PartialDateTime(till)}") };
            results = await _fixture.SearchService.SearchAsync(type, queryParameters, CancellationToken.None);
            Assert.Single(results.Results);
            resource = results.Results.First().Resource;
            Assert.Equal("1", resource.Version);
            Assert.False(resource.IsHistory); // current

            await UpdateResource(patient); // update resource

            // !!! pre time travel behavior
            // same parameters
            // resource is not returned because it became "invisible" in the time interval requested
            results = await _fixture.SearchService.SearchAsync(type, queryParameters, CancellationToken.None);
            Assert.Empty(results.Results);

            // add magic parameters
            var maxId = _fixture.SearchService.GetSurrogateId(till);
            var range = (await _fixture.SearchService.GetSurrogateIdRanges(type, 0, maxId, 100, 1, true, CancellationToken.None)).First();
            queryParameters = new[]
            {
                Tuple.Create(KnownQueryParameterNames.Type, type),
                Tuple.Create(KnownQueryParameterNames.GlobalEndSurrogateId, maxId.ToString()),
                Tuple.Create(KnownQueryParameterNames.EndSurrogateId, range.EndId.ToString()),
                Tuple.Create(KnownQueryParameterNames.GlobalStartSurrogateId, "0"),
                Tuple.Create(KnownQueryParameterNames.StartSurrogateId, range.StartId.ToString()),
            };

            // !!! time travel behavior
            results = await _fixture.SearchService.SearchAsync(type, queryParameters, CancellationToken.None);
            Assert.Single(results.Results);
            resource = results.Results.First().Resource;
            Assert.Equal("1", resource.Version);
            Assert.False(resource.IsHistory); // it is returned as current but is marked as history in the database ???

            // current resource
            results = await _fixture.SearchService.SearchAsync(type, new List<Tuple<string, string>>(), CancellationToken.None);
            Assert.Single(results.Results);
            resource = results.Results.First().Resource;
            Assert.Equal("2", resource.Version);
            Assert.False(resource.IsHistory); // current
        }

        private async Task UpdateResource(Patient patient)
        {
            var oldId = patient.Id;
            await _fixture.SqlHelper.ExecuteSqlCmd($"UPDATE dbo.Resource SET IsHistory = 1 WHERE ResourceId = '{oldId}' AND Version = 1");
            var newId = Guid.NewGuid().ToString();
            patient.Id = newId;
            await Mediator.UpsertResourceAsync(patient.ToResourceElement()); // there is no control to keep history, so insert as new and update to old
            await _fixture.SqlHelper.ExecuteSqlCmd($"UPDATE dbo.Resource SET ResourceId = '{oldId}', Version = 2 WHERE ResourceId = '{newId}'");
        }

        [Fact]
        public async Task GivenAResource_WhenSaving_ThenTheMetaIsUpdated()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
            Assert.NotNull(saveResult);
            Assert.Equal(SaveOutcomeType.Created, saveResult.Outcome);
            var deserializedResource = saveResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);
            Assert.NotNull(deserializedResource);
            Assert.True((DateTimeOffset.UtcNow - deserializedResource.LastUpdated.GetValueOrDefault()).TotalMilliseconds < 100);
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

            if (wrapper.RawResource.IsMetaSet)
            {
                Observation observation = _fhirJsonParser.Parse<Observation>(wrapper.RawResource.Data);
                Assert.Equal("2", observation.VersionId);
            }
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
        public async Task GivenASavedResource_WhenUpserting_ThenRawResourceVersionIsSetOrMetaSetIsSetToFalse()
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

            Assert.NotEqual(wrapper.Version, versionId);
            var deserialized = _fhirJsonParser.Parse<Observation>(wrapper.RawResource.Data);

            Assert.Equal(wrapper.RawResource.IsMetaSet ? "2" : "1", deserialized.VersionId);
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

            newResourceValues.ToPoco<Organization>().Text = new Narrative
            {
                Status = Narrative.NarrativeStatus.Generated,
                Div = $"<div>{ContentUpdated}</div>",
            };
            var updateResult = await Mediator.UpsertResourceAsync(newResourceValues, WeakETag.FromVersionId(saveResult.RawResourceElement.VersionId));

            await Assert.ThrowsAsync<ResourceNotFoundException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Organization>(saveResult.RawResourceElement.Id, saveResult.RawResourceElement.VersionId)));
        }

        [Fact]
        public async Task WhenDeletingAResource_ThenWeGetResourceGone()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var deletedResourceKey = await Mediator.DeleteResourceAsync(new ResourceKey("Observation", saveResult.RawResourceElement.Id), DeleteOperation.SoftDelete);

            Assert.NotEqual(saveResult.RawResourceElement.VersionId, deletedResourceKey.ResourceKey.VersionId);

            await Assert.ThrowsAsync<ResourceGoneException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Observation>(saveResult.RawResourceElement.Id)));
        }

        [Fact]
        public async Task WhenDeletingAResourceThatNeverExisted_ThenReadingTheResourceReturnsNotFound()
        {
            string id = "missingid";

            var deletedResourceKey = await Mediator.DeleteResourceAsync(new ResourceKey("Observation", id), DeleteOperation.SoftDelete);

            Assert.Null(deletedResourceKey.ResourceKey.VersionId);

            await Assert.ThrowsAsync<ResourceNotFoundException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Observation>(id)));
        }

        [Fact]
        public async Task WhenDeletingAResourceForASecondTime_ThenWeDoNotGetANewVersion()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var resourceKey = new ResourceKey("Observation", saveResult.RawResourceElement.Id);

            await Mediator.DeleteResourceAsync(resourceKey, DeleteOperation.SoftDelete);

            var deletedResourceKey2 = await Mediator.DeleteResourceAsync(resourceKey, DeleteOperation.SoftDelete);

            Assert.Null(deletedResourceKey2.ResourceKey.VersionId);

            await Assert.ThrowsAsync<ResourceGoneException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Observation>(saveResult.RawResourceElement.Id)));
        }

        [Fact]
        public async Task WhenHardDeletingAResource_ThenWeGetResourceNotFound()
        {
            object snapshotToken = await _fixture.TestHelper.GetSnapshotToken();

            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var deletedResourceKey = await Mediator.DeleteResourceAsync(new ResourceKey("Observation", saveResult.RawResourceElement.Id), DeleteOperation.HardDelete);

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

            var deleteResult = await Mediator.DeleteResourceAsync(new ResourceKey("Observation", resourceId), DeleteOperation.SoftDelete);
            var updateResult = await Mediator.UpsertResourceAsync(deserializedResult);

            // Hard-delete the resource.
            var deletedResourceKey = await Mediator.DeleteResourceAsync(new ResourceKey("Observation", resourceId), DeleteOperation.HardDelete);

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
                async () => { await Mediator.DeleteResourceAsync(new ResourceKey<Observation>(Guid.NewGuid().ToString(), Guid.NewGuid().ToString()), DeleteOperation.SoftDelete); });
        }

        [Fact]
        public async Task GivenADeletedResource_WhenUpsertingWithValidETagHeader_ThenTheDeletedResourceIsRevived()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
            var deletedResourceKey = await Mediator.DeleteResourceAsync(new ResourceKey("Observation", saveResult.RawResourceElement.Id), DeleteOperation.SoftDelete);

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
        public async Task GivenAnUpdatedResource_WhenUpdatingSearchParameterIndexAsync_ThenResourceMetadataIsUnchanged()
        {
            ResourceElement patientResource = CreatePatientResourceElement("Patient", Guid.NewGuid().ToString());
            SaveOutcome upsertResult = await Mediator.UpsertResourceAsync(patientResource);

            SearchParameter searchParam = null;
            const string searchParamName = "newSearchParam";

            try
            {
                searchParam = await CreatePatientSearchParam(searchParamName, SearchParamType.String, "Patient.name");
                ISearchValue searchValue = new StringSearchValue(searchParamName);

                (ResourceWrapper original, ResourceWrapper updated) = await CreateUpdatedWrapperFromExistingPatient(upsertResult, searchParam, searchValue);

                await _dataStore.UpdateSearchParameterIndicesAsync(updated, WeakETag.FromVersionId(original.Version), CancellationToken.None);

                // Get the reindexed resource from the database
                var resourceKey1 = new ResourceKey(upsertResult.RawResourceElement.InstanceType, upsertResult.RawResourceElement.Id, upsertResult.RawResourceElement.VersionId);
                ResourceWrapper reindexed = await _dataStore.GetAsync(resourceKey1, CancellationToken.None);

                VerifyReindexedResource(original, reindexed);
            }
            finally
            {
                if (searchParam != null)
                {
                    _searchParameterDefinitionManager.DeleteSearchParameter(searchParam.ToTypedElement());
                    await _fixture.TestHelper.DeleteSearchParameterStatusAsync(searchParam.Url, CancellationToken.None);
                }
            }
        }

        [Fact]
        public async Task GivenAnUpdatedResourceWithWrongWeakETag_WhenUpdatingSearchParameterIndexAsync_ThenExceptionIsThrown()
        {
            ResourceElement patientResource = CreatePatientResourceElement("Patient", Guid.NewGuid().ToString());
            SaveOutcome upsertResult = await Mediator.UpsertResourceAsync(patientResource);

            SearchParameter searchParam1 = null;
            const string searchParamName1 = "newSearchParam1";

            SearchParameter searchParam2 = null;
            const string searchParamName2 = "newSearchParam2";

            try
            {
                searchParam1 = await CreatePatientSearchParam(searchParamName1, SearchParamType.String, "Patient.name");
                ISearchValue searchValue1 = new StringSearchValue(searchParamName1);

                (ResourceWrapper original, ResourceWrapper updatedWithSearchParam1) = await CreateUpdatedWrapperFromExistingPatient(upsertResult, searchParam1, searchValue1);

                var deserializedResource = _fhirJsonParser.Parse<Patient>(original.RawResource.Data);
                UpdatePatient(deserializedResource);
                await _dataStore.UpsertAsync(UpdatePatientResourceWrapper(deserializedResource), WeakETag.FromVersionId(original.Version), allowCreate: false, keepHistory: false, CancellationToken.None);

                // Let's update the resource again with new information
                searchParam2 = await CreatePatientSearchParam(searchParamName2, SearchParamType.Token, "Patient.gender");
                ISearchValue searchValue2 = new TokenSearchValue("system", "code", "text");

                // Create the updated wrapper from the original resource that has the outdated version
                (_, ResourceWrapper updatedWithSearchParam2) = await CreateUpdatedWrapperFromExistingPatient(upsertResult, searchParam2, searchValue2, original);

                // Attempt to reindex the resource
                await Assert.ThrowsAsync<PreconditionFailedException>(() => _dataStore.UpdateSearchParameterIndicesAsync(updatedWithSearchParam2, WeakETag.FromVersionId(original.Version), CancellationToken.None));
            }
            finally
            {
                if (searchParam1 != null)
                {
                    _searchParameterDefinitionManager.DeleteSearchParameter(searchParam1.ToTypedElement());
                    await _fixture.TestHelper.DeleteSearchParameterStatusAsync(searchParam1.Url, CancellationToken.None);
                }

                if (searchParam2 != null)
                {
                    _searchParameterDefinitionManager.DeleteSearchParameter(searchParam2.ToTypedElement());
                    await _fixture.TestHelper.DeleteSearchParameterStatusAsync(searchParam2.Url, CancellationToken.None);
                }
            }
        }

        [Fact]
        public async Task GivenADeletedResource_WhenUpdatingSearchParameterIndexAsync_ThenExceptionIsThrown()
        {
            ResourceElement patientResource = CreatePatientResourceElement("Patient", Guid.NewGuid().ToString());
            SaveOutcome upsertResult = await Mediator.UpsertResourceAsync(patientResource);

            SearchParameter searchParam = null;
            const string searchParamName = "newSearchParam";

            try
            {
                searchParam = await CreatePatientSearchParam(searchParamName, SearchParamType.String, "Patient.name");
                ISearchValue searchValue = new StringSearchValue(searchParamName);

                // Update the resource wrapper, adding the new search parameter
                (ResourceWrapper original, ResourceWrapper updated) = await CreateUpdatedWrapperFromExistingPatient(upsertResult, searchParam, searchValue);

                ResourceWrapper deletedWrapper = CreateDeletedWrapper(original);
                await _dataStore.UpsertAsync(deletedWrapper, WeakETag.FromVersionId(deletedWrapper.Version), allowCreate: true, keepHistory: false, CancellationToken.None);

                // Attempt to reindex the version of the resource that hasn't been deleted
                await Assert.ThrowsAsync<PreconditionFailedException>(() => _dataStore.UpdateSearchParameterIndicesAsync(updated, WeakETag.FromVersionId(updated.Version), CancellationToken.None));
            }
            finally
            {
                if (searchParam != null)
                {
                    _searchParameterDefinitionManager.DeleteSearchParameter(searchParam.ToTypedElement());
                    await _fixture.TestHelper.DeleteSearchParameterStatusAsync(searchParam.Url, CancellationToken.None);
                }
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenAValidResource_WhenUpdatingAResourceWithSameDataImmaterialKeepHistoryValue_ServerShouldNotCreateANewVersionAndReturnOk(bool keepHistory)
        {
            // Upserting a resource twice with no data change
            UpsertOutcome createResult = await _dataStore.UpsertAsync(CreateObservationResourceWrapper(Guid.NewGuid().ToString()), null, allowCreate: true, keepHistory: keepHistory, CancellationToken.None);
            UpsertOutcome upsertResult = await _dataStore.UpsertAsync(CreateObservationResourceWrapper(createResult.Wrapper.ResourceId), null, allowCreate: true, keepHistory: keepHistory, CancellationToken.None);

            Assert.NotNull(createResult);
            Assert.NotNull(upsertResult);

            var createResource = new RawResourceElement(createResult.Wrapper);
            var updateResource = new RawResourceElement(upsertResult.Wrapper);

            Assert.Equal(createResult.Wrapper.ResourceId, upsertResult.Wrapper.ResourceId);
            Assert.Equal(createResult.Wrapper.Version, upsertResult.Wrapper.Version);

            // With current "o" format for date we only store upto 3 digits for millisconds
            // CreateResult.LastUpdated has date as 2008-10-31T17:04:32:3210000
            // upsertResult.lastUpdated will return what is stored in DB 2008-10-31T17:04:32:321 mismatching the milliseconds value
            // Hence comparing milliseconds separately. s Format Specifier 2008-10-31T17:04:32
            Assert.Equal(createResource.LastUpdated.Value.ToString("s"), updateResource.LastUpdated.Value.ToString("s"));
            Assert.Equal(createResource.LastUpdated.Value.Millisecond, updateResource.LastUpdated.Value.Millisecond);
            Assert.Equal(createResult.Wrapper.LastModified.ToString("s"), createResult.Wrapper.LastModified.ToString("s"));
            Assert.Equal(createResult.Wrapper.LastModified.Millisecond, createResult.Wrapper.LastModified.Millisecond);
        }

        [Fact]
        public async Task GivenUpdatedResources_WhenBulkUpdatingSearchParameterIndicesAsync_ThenResourceMetadataIsUnchanged()
        {
            ResourceElement patientResource1 = CreatePatientResourceElement("Patient1", Guid.NewGuid().ToString());
            SaveOutcome upsertResult1 = await Mediator.UpsertResourceAsync(patientResource1);

            ResourceElement patientResource2 = CreatePatientResourceElement("Patient2", Guid.NewGuid().ToString());
            SaveOutcome upsertResult2 = await Mediator.UpsertResourceAsync(patientResource2);

            SearchParameter searchParam = null;
            const string searchParamName = "newSearchParam";

            try
            {
                searchParam = await CreatePatientSearchParam(searchParamName, SearchParamType.String, "Patient.name");
                ISearchValue searchValue = new StringSearchValue(searchParamName);

                (ResourceWrapper original1, ResourceWrapper updated1) = await CreateUpdatedWrapperFromExistingPatient(upsertResult1, searchParam, searchValue);
                (ResourceWrapper original2, ResourceWrapper updated2) = await CreateUpdatedWrapperFromExistingPatient(upsertResult2, searchParam, searchValue);

                var resources = new List<ResourceWrapper> { updated1, updated2 };

                await _dataStore.BulkUpdateSearchParameterIndicesAsync(resources, CancellationToken.None);

                // Get the reindexed resources from the database
                var resourceKey1 = new ResourceKey(upsertResult1.RawResourceElement.InstanceType, upsertResult1.RawResourceElement.Id, upsertResult1.RawResourceElement.VersionId);
                ResourceWrapper reindexed1 = await _dataStore.GetAsync(resourceKey1, CancellationToken.None);

                var resourceKey2 = new ResourceKey(upsertResult2.RawResourceElement.InstanceType, upsertResult2.RawResourceElement.Id, upsertResult2.RawResourceElement.VersionId);
                ResourceWrapper reindexed2 = await _dataStore.GetAsync(resourceKey2, CancellationToken.None);

                VerifyReindexedResource(original1, reindexed1);
                VerifyReindexedResource(original2, reindexed2);
            }
            finally
            {
                if (searchParam != null)
                {
                    _searchParameterDefinitionManager.DeleteSearchParameter(searchParam.ToTypedElement());
                    await _fixture.TestHelper.DeleteSearchParameterStatusAsync(searchParam.Url, CancellationToken.None);
                }
            }
        }

        [Fact]
        public async Task GivenUpdatedResourcesWithWrongWeakETag_WhenBulkUpdatingSearchParameterIndicesAsync_ThenExceptionIsThrown()
        {
            ResourceElement patientResource1 = CreatePatientResourceElement("Patient1", Guid.NewGuid().ToString());
            SaveOutcome upsertResult1 = await Mediator.UpsertResourceAsync(patientResource1);

            ResourceElement patientResource2 = CreatePatientResourceElement("Patient2", Guid.NewGuid().ToString());
            SaveOutcome upsertResult2 = await Mediator.UpsertResourceAsync(patientResource2);

            SearchParameter searchParam1 = null;
            const string searchParamName1 = "newSearchParam1";

            SearchParameter searchParam2 = null;
            const string searchParamName2 = "newSearchParam2";

            try
            {
                searchParam1 = await CreatePatientSearchParam(searchParamName1, SearchParamType.String, "Patient.name");
                ISearchValue searchValue1 = new StringSearchValue(searchParamName1);

                (ResourceWrapper original1, ResourceWrapper updated1) = await CreateUpdatedWrapperFromExistingPatient(upsertResult1, searchParam1, searchValue1);
                (ResourceWrapper original2, ResourceWrapper updated2) = await CreateUpdatedWrapperFromExistingPatient(upsertResult2, searchParam1, searchValue1);

                var deserializedResource = _fhirJsonParser.Parse<Patient>(original1.RawResource.Data);
                UpdatePatient(deserializedResource);
                await _dataStore.UpsertAsync(UpdatePatientResourceWrapper(deserializedResource), WeakETag.FromVersionId(original1.Version), allowCreate: false, keepHistory: false, CancellationToken.None);

                deserializedResource = _fhirJsonParser.Parse<Patient>(original2.RawResource.Data);
                UpdatePatient(deserializedResource);
                await _dataStore.UpsertAsync(UpdatePatientResourceWrapper(deserializedResource), WeakETag.FromVersionId(original2.Version), allowCreate: false, keepHistory: false, CancellationToken.None);

                // Let's update the resources again with new information
                searchParam2 = await CreatePatientSearchParam(searchParamName2, SearchParamType.Token, "Patient.gender");
                ISearchValue searchValue2 = new TokenSearchValue("system", "code", "text");

                // Create the updated wrappers using the original resource and its outdated version
                UpdatePatient(upsertResult1.RawResourceElement.ToPoco<Patient>(Deserializers.ResourceDeserializer));
                UpdatePatient(upsertResult2.RawResourceElement.ToPoco<Patient>(Deserializers.ResourceDeserializer));
                (_, ResourceWrapper updated1WithSearchParam2) = await CreateUpdatedWrapperFromExistingPatient(upsertResult1, searchParam2, searchValue2, original1);
                (_, ResourceWrapper updated2WithSearchParam2) = await CreateUpdatedWrapperFromExistingPatient(upsertResult2, searchParam2, searchValue2, original2);

                var resources = new List<ResourceWrapper> { updated1WithSearchParam2, updated2WithSearchParam2 };

                // Attempt to reindex resources with the old versions
                await Assert.ThrowsAsync<PreconditionFailedException>(() => _dataStore.BulkUpdateSearchParameterIndicesAsync(resources, CancellationToken.None));
            }
            finally
            {
                if (searchParam1 != null)
                {
                    _searchParameterDefinitionManager.DeleteSearchParameter(searchParam1.ToTypedElement());
                    await _fixture.TestHelper.DeleteSearchParameterStatusAsync(searchParam1.Url, CancellationToken.None);
                }

                if (searchParam2 != null)
                {
                    _searchParameterDefinitionManager.DeleteSearchParameter(searchParam2.ToTypedElement());
                    await _fixture.TestHelper.DeleteSearchParameterStatusAsync(searchParam2.Url, CancellationToken.None);
                }
            }
        }

        [Fact]
        public async Task GivenDeletedResource_WhenBulkUpdatingSearchParameterIndicesAsync_ThenExceptionIsThrown()
        {
            ResourceElement patientResource1 = CreatePatientResourceElement("Patient1", Guid.NewGuid().ToString());
            SaveOutcome upsertResult1 = await Mediator.UpsertResourceAsync(patientResource1);

            ResourceElement patientResource2 = CreatePatientResourceElement("Patient2", Guid.NewGuid().ToString());
            SaveOutcome upsertResult2 = await Mediator.UpsertResourceAsync(patientResource2);

            SearchParameter searchParam = null;
            const string searchParamName = "newSearchParam";

            try
            {
                searchParam = await CreatePatientSearchParam(searchParamName, SearchParamType.String, "Patient.name");
                ISearchValue searchValue = new StringSearchValue(searchParamName);

                // Update the resource wrappers, adding the new search parameter
                (ResourceWrapper original1, ResourceWrapper updated1) = await CreateUpdatedWrapperFromExistingPatient(upsertResult1, searchParam, searchValue);
                (_, ResourceWrapper updated2) = await CreateUpdatedWrapperFromExistingPatient(upsertResult2, searchParam, searchValue);

                // Delete one of the two resources
                ResourceWrapper deletedWrapper = CreateDeletedWrapper(original1);
                await _dataStore.UpsertAsync(deletedWrapper, WeakETag.FromVersionId(deletedWrapper.Version), allowCreate: true, keepHistory: false, CancellationToken.None);

                var resources = new List<ResourceWrapper> { updated1, updated2 };

                // Attempt to reindex both resources, one of which has since been deleted and has a version that is out of date.
                await Assert.ThrowsAsync<PreconditionFailedException>(() => _dataStore.BulkUpdateSearchParameterIndicesAsync(resources, CancellationToken.None));
            }
            finally
            {
                if (searchParam != null)
                {
                    _searchParameterDefinitionManager.DeleteSearchParameter(searchParam.ToTypedElement());
                    await _fixture.TestHelper.DeleteSearchParameterStatusAsync(searchParam.Url, CancellationToken.None);
                }
            }
        }

        [Fact(Skip = "Not valid test in Merge Resources design.")]
        [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenResourceWrapperWithEmptyRawResource_WhenUpserting_ThenExceptionisThrown()
        {
            var wrapper = CreateObservationResourceWrapper("obsId1", true);
            await Assert.ThrowsAsync<ServiceUnavailableException>(() => _fixture.DataStore.UpsertAsync(wrapper, null, true, true, CancellationToken.None));
        }

        private static void VerifyReindexedResource(ResourceWrapper original, ResourceWrapper replaceResult)
        {
            Assert.Equal(original.ResourceId, replaceResult.ResourceId);
            Assert.Equal(original.Version, replaceResult.Version);
            Assert.Equal(original.ResourceTypeName, replaceResult.ResourceTypeName);
            Assert.Equal(original.LastModified, replaceResult.LastModified);
        }

        private async Task<(ResourceWrapper original, ResourceWrapper updated)> CreateUpdatedWrapperFromExistingPatient(
            SaveOutcome upsertResult,
            SearchParameter searchParam,
            ISearchValue searchValue,
            ResourceWrapper originalWrapper = null,
            string updatedId = null)
        {
            var searchIndex = new SearchIndexEntry(searchParam.ToInfo(), searchValue);
            var searchIndices = new List<SearchIndexEntry> { searchIndex };

            if (originalWrapper == null)
            {
                // Get wrapper from data store directly
                var resourceKey = new ResourceKey(upsertResult.RawResourceElement.InstanceType, upsertResult.RawResourceElement.Id, upsertResult.RawResourceElement.VersionId);

                originalWrapper = await _dataStore.GetAsync(resourceKey, CancellationToken.None);
            }

            // Add new search index entry to existing wrapper
            var updatedWrapper = new ResourceWrapper(
                updatedId ?? originalWrapper.ResourceId,
                originalWrapper.Version,
                originalWrapper.ResourceTypeName,
                originalWrapper.RawResource,
                new ResourceRequest(HttpMethod.Post, null),
                originalWrapper.LastModified,
                deleted: false,
                searchIndices,
                originalWrapper.CompartmentIndices,
                originalWrapper.LastModifiedClaims,
                _searchParameterDefinitionManager.GetSearchParameterHashForResourceType("Patient"));

            return (originalWrapper, updatedWrapper);
        }

        private ResourceWrapper CreateDeletedWrapper(ResourceWrapper originalWrapper)
        {
            return new ResourceWrapper(
                originalWrapper.ResourceId,
                originalWrapper.Version,
                originalWrapper.ResourceTypeName,
                originalWrapper.RawResource,
                new ResourceRequest(HttpMethod.Delete, null),
                originalWrapper.LastModified,
                deleted: true,
                originalWrapper.SearchIndices,
                originalWrapper.CompartmentIndices,
                originalWrapper.LastModifiedClaims,
                originalWrapper.SearchParameterHash);
        }

        private async Task<SearchParameter> CreatePatientSearchParam(string searchParamName, SearchParamType type, string expression)
        {
            var searchParam = new SearchParameter
            {
                Url = $"http://hl7.org/fhir/SearchParameter/Patient-{searchParamName}",
                Type = type,
                Base = new List<ResourceType?> { ResourceType.Patient },
                Expression = expression,
                Name = searchParamName,
                Code = searchParamName,
            };

            _searchParameterDefinitionManager.AddNewSearchParameters(new List<ITypedElement> { searchParam.ToTypedElement() });

            // Add the search parameter to the datastore
            await _fixture.SearchParameterStatusManager.UpdateSearchParameterStatusAsync(new List<string> { searchParam.Url }, SearchParameterStatus.Supported, CancellationToken.None);

            return searchParam;
        }

        private ResourceElement CreatePatientResourceElement(string patientName, string id)
        {
            var json = Samples.GetJson("Patient");
            json = json.Replace("Chalmers", patientName);
            json = json.Replace("\"id\": \"example\"", "\"id\": \"" + id + "\"");
            var rawResource = new RawResource(json, FhirResourceFormat.Json, isMetaSet: false);
            return Deserializers.ResourceDeserializer.DeserializeRaw(rawResource, "v1", DateTimeOffset.UtcNow);
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

        private ResourceWrapper CreateObservationResourceWrapper(string observationId, bool setRawResourceNull = false)
        {
            Observation observationResource = Samples.GetDefaultObservation().ToPoco<Observation>();
            observationResource.Id = observationId;
            observationResource.VersionId = "1";

            var resourceElement = observationResource.ToResourceElement();
            RawResource rawResource;

            if (setRawResourceNull)
            {
                var rawSubstitute = Substitute.For<RawResource>();
                rawResource = rawSubstitute;
            }
            else
            {
                rawResource = new RawResource(observationResource.ToJson(), FhirResourceFormat.Json, isMetaSet: true);
            }

            var resourceRequest = new ResourceRequest(WebRequestMethods.Http.Put);
            var compartmentIndices = Substitute.For<CompartmentIndices>();
            var searchIndices = _searchIndexer.Extract(resourceElement);
            var wrapper = new ResourceWrapper(resourceElement, rawResource, resourceRequest, false, searchIndices, compartmentIndices, new List<KeyValuePair<string, string>>(), _searchParameterDefinitionManager.GetSearchParameterHashForResourceType("Observation"));
            wrapper.SearchParameterHash = "hash";

            return wrapper;
        }

        private ResourceWrapper UpdatePatientResourceWrapper(Patient patientResource)
        {
            var resourceElement = patientResource.ToResourceElement();
            var rawResource = new RawResource(patientResource.ToJson(), FhirResourceFormat.Json, isMetaSet: true);
            var resourceRequest = new ResourceRequest(WebRequestMethods.Http.Put);
            var compartmentIndices = Substitute.For<CompartmentIndices>();
            var searchIndices = _searchIndexer.Extract(resourceElement);
            var wrapper = new ResourceWrapper(resourceElement, rawResource, resourceRequest, false, searchIndices, compartmentIndices, new List<KeyValuePair<string, string>>(), _searchParameterDefinitionManager.GetSearchParameterHashForResourceType("Observation"));
            wrapper.SearchParameterHash = "hash";

            return wrapper;
        }

        private static void UpdatePatient(Patient patientResource)
        {
            patientResource.Text = new Narrative
            {
                Status = Narrative.NarrativeStatus.Generated,
                Div = $"<div>{ContentUpdated}</div>",
            };
        }
    }
}
