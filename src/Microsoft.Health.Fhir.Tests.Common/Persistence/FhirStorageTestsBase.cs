// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common.Mocks;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Common.Persistence
{
    public abstract class FhirStorageTestsBase
    {
        private readonly CapabilityStatement _conformance;
        private readonly IResourceWrapperFactory _resourceWrapperFactory;

        public FhirStorageTestsBase(IDataStore dataStore)
        {
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
            provider.GetCapabilityStatementAsync().Returns(_conformance);

            // TODO: FhirRepository instantiate ResourceDeserializer class directly
            // which will try to deserialize the raw resource. We should mock it as well.
            var rawResourceFactory = Substitute.For<RawResourceFactory>(new FhirJsonSerializer());

            _resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
            _resourceWrapperFactory
                .Create(Arg.Any<Resource>(), Arg.Any<bool>())
                .Returns(x =>
                {
                    Resource resource = x.ArgAt<Resource>(0);

                    return new ResourceWrapper(resource, rawResourceFactory.Create(resource), new ResourceRequest("http://fhir", HttpMethod.Post), x.ArgAt<bool>(1), null, null, null);
                });

            FhirRepository = new FhirRepository(dataStore, new Lazy<IConformanceProvider>(() => provider), _resourceWrapperFactory);
        }

        protected IFhirRepository FhirRepository { get; }

        [Fact]
        public async Task GivenAResource_WhenSaving_ThenTheMetaIsUpdated()
        {
            var instant = DateTimeOffset.Now;
            using (Mock.Property(() => Clock.UtcNowFunc, () => instant))
            {
                var saveResult = await FhirRepository.UpsertAsync(Samples.GetJsonSample("Weight"));

                Assert.NotNull(saveResult);
                Assert.Equal(SaveOutcomeType.Created, saveResult.Outcome);
                Assert.NotNull(saveResult.Resource);
                Assert.NotNull(saveResult.Resource.Id);
                Assert.NotNull(saveResult.Resource.VersionId);
                Assert.Equal(instant, saveResult.Resource.Meta.LastUpdated.GetValueOrDefault());
            }
        }

        [Fact]
        public async Task GivenAResourceId_WhenFetching_ThenTheResponseLoadsCorrectly()
        {
            var saveResult = await FhirRepository.CreateAsync(Samples.GetJsonSample("Weight"));
            var getResult = await FhirRepository.GetAsync(new ResourceKey("Observation", saveResult.Id));

            Assert.NotNull(getResult);
            Assert.Equal(saveResult.Id, getResult.Id);

            var observation = getResult as Observation;
            Assert.NotNull(observation);
            Assert.NotNull(observation.Value);

            SimpleQuantity sq = Assert.IsType<SimpleQuantity>(observation.Value);

            Assert.Equal(67, sq.Value);
        }

        [Fact]
        public async Task GivenASavedResource_WhenUpserting_ThenTheExistingResourceIsUpdated()
        {
            var saveResult = await FhirRepository.UpsertAsync(Samples.GetJsonSample("Weight"));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams");
            newResourceValues.Id = saveResult.Resource.Id;

            var updateResult = await FhirRepository.UpsertAsync(newResourceValues, WeakETag.FromVersionId(saveResult.Resource.VersionId));

            Assert.NotNull(updateResult);
            Assert.Equal(SaveOutcomeType.Updated, updateResult.Outcome);

            Assert.NotNull(updateResult.Resource);
            Assert.Equal(saveResult.Resource.Id, updateResult.Resource.Id);
        }

        [Fact]
        public async Task GivenAResource_WhenUpsertingDifferentTypeWithTheSameId_ThenTheExistingResourceIsNotOverridden()
        {
            Resource weightSample = Samples.GetJsonSample("Weight");
            Resource patientSample = Samples.GetJsonSample("Patient");

            var exampleId = Guid.NewGuid().ToString();

            weightSample.Id = exampleId;
            patientSample.Id = exampleId;

            await FhirRepository.UpsertAsync(weightSample);
            await FhirRepository.UpsertAsync(patientSample);

            var fetchedResult1 = await FhirRepository.GetAsync(new ResourceKey<Observation>(exampleId));
            var fetchedResult2 = await FhirRepository.GetAsync(new ResourceKey<Patient>(exampleId));

            Assert.Equal(weightSample.Id, fetchedResult1.Id);
            Assert.Equal(patientSample.Id, fetchedResult2.Id);

            Assert.Equal(weightSample.TypeName, fetchedResult1.TypeName);
            Assert.Equal(patientSample.TypeName, fetchedResult2.TypeName);
        }

        [Fact]
        public async Task GivenANewResource_WhenUpsertingWithCreateDisabled_ThenAMethodNotAllowedExceptionIsThrown()
        {
            var observation = _conformance.Rest[0].Resource.Find(r => r.Type == ResourceType.Observation);
            observation.UpdateCreate = false;
            observation.Versioning = CapabilityStatement.ResourceVersionPolicy.Versioned;
            var ex = await Assert.ThrowsAsync<MethodNotAllowedException>(() => FhirRepository.UpsertAsync(Samples.GetJsonSample("Weight")));

            Assert.Equal(Resources.ResourceCreationNotAllowed, ex.Message);
        }

        [Fact]
        public async Task GivenANewResource_WhenUpsertingWithCreateEnabledAndJunkEtag_ThenAMethodNotAllowedExceptionIsThrown()
        {
            var observation = _conformance.Rest[0].Resource.Find(r => r.Type == ResourceType.Observation);
            observation.UpdateCreate = true;
            observation.Versioning = CapabilityStatement.ResourceVersionPolicy.Versioned;
            await Assert.ThrowsAsync<ResourceConflictException>(() => FhirRepository.UpsertAsync(Samples.GetJsonSample("Weight"), WeakETag.FromVersionId("junk")));
        }

        [Fact]
        public async Task GivenANewResource_WhenUpsertingWithCreateDisabledAndJunkEtag_ThenAMethodNotAllowedExceptionIsThrown()
        {
            var observation = _conformance.Rest[0].Resource.Find(r => r.Type == ResourceType.Observation);
            observation.UpdateCreate = false;
            observation.Versioning = CapabilityStatement.ResourceVersionPolicy.Versioned;
            await Assert.ThrowsAsync<ResourceConflictException>(() => FhirRepository.UpsertAsync(Samples.GetJsonSample("Weight"), WeakETag.FromVersionId("junk")));
        }

        [Fact]
        public async Task GivenASavedResource_WhenUpsertingWithNoVersionId_ThenTheExistingResourceIsUpdated()
        {
            var saveResult = await FhirRepository.UpsertAsync(Samples.GetJsonSample("Weight"));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams");
            newResourceValues.Id = saveResult.Resource.Id;

            var updateResult = await FhirRepository.UpsertAsync(newResourceValues);

            Assert.NotNull(updateResult);
            Assert.Equal(SaveOutcomeType.Updated, updateResult.Outcome);

            Assert.NotNull(updateResult.Resource);
            Assert.Equal(saveResult.Resource.Id, updateResult.Resource.Id);
        }

        [Fact]
        public async Task GivenASavedResource_WhenConcurrentlyUpsertingWithNoVersionId_ThenTheExistingResourceIsUpdated()
        {
            var saveResult = await FhirRepository.UpsertAsync(Samples.GetJsonSample("Weight"));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams");
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
                list.Add(FhirRepository.UpsertAsync(CloneResource(i)));
            }

            await Task.WhenAll(list);

            foreach (var item in list)
            {
                Assert.Equal(SaveOutcomeType.Updated, item.Result.Outcome);
            }

            var allObservations = list.Select(x => ((Quantity)((Observation)x.Result.Resource).Value).Value.GetValueOrDefault()).Distinct();
            Assert.Equal(itemsToCreate, allObservations.Count());
        }

        [Fact]
        public async Task GivenASavedResource_WhenUpsertingWithIncorrectVersionId_ThenAResourceConflictIsThrown()
        {
            var saveResult = await FhirRepository.UpsertAsync(Samples.GetJsonSample("Weight"));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams");
            newResourceValues.Id = saveResult.Resource.Id;

            await Assert.ThrowsAsync<ResourceConflictException>(async () => await FhirRepository.UpsertAsync(newResourceValues, WeakETag.FromVersionId("incorrectVersion")));
        }

        [Fact]
        public async Task GivenAResourceWithNoHistory_WhenFetchingByVersionId_ThenReadWorksCorrectly()
        {
            var saveResult = await FhirRepository.UpsertAsync(Samples.GetJsonSample("Weight"));

            var result = await FhirRepository.GetAsync(new ResourceKey(saveResult.Resource.TypeName, saveResult.Resource.Id, saveResult.Resource.VersionId));

            Assert.NotNull(result);
            Assert.Equal(saveResult.Resource.Id, result.Id);
        }

        [Fact]
        public async Task UpdatingAResource_ThenWeCanAccessHistoricValues()
        {
            var saveResult = await FhirRepository.UpsertAsync(Samples.GetJsonSample("Weight"));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams");
            newResourceValues.Id = saveResult.Resource.Id;

            var updateResult = await FhirRepository.UpsertAsync(newResourceValues, WeakETag.FromVersionId(saveResult.Resource.VersionId));

            var getV1Result = await FhirRepository.GetAsync(new ResourceKey<Observation>(saveResult.Resource.Id, saveResult.Resource.VersionId));

            Assert.NotNull(getV1Result);
            Assert.Equal(saveResult.Resource.Id, getV1Result.Id);
            Assert.Equal(updateResult.Resource.Id, getV1Result.Id);

            var oldObservation = getV1Result as Observation;
            Assert.NotNull(oldObservation);
            Assert.NotNull(oldObservation.Value);

            SimpleQuantity sq = Assert.IsType<SimpleQuantity>(oldObservation.Value);

            Assert.Equal(67, sq.Value);
        }

        [Fact]
        public async Task UpdatingAResourceWithNoHistory_ThenWeCannotAccessHistoricValues()
        {
            var saveResult = await FhirRepository.UpsertAsync(Samples.GetDefaultOrganization());

            var newResourceValues = Samples.GetDefaultOrganization();
            newResourceValues.Id = saveResult.Resource.Id;

            var updateResult = await FhirRepository.UpsertAsync(newResourceValues, WeakETag.FromVersionId(saveResult.Resource.VersionId));

            await Assert.ThrowsAsync<ResourceNotFoundException>(
                () => FhirRepository.GetAsync(new ResourceKey<Organization>(saveResult.Resource.Id, saveResult.Resource.VersionId)));
        }

        [Fact]
        public async Task WhenDeletingAResource_ThenWeGetResourceGone()
        {
            var saveResult = await FhirRepository.UpsertAsync(Samples.GetJsonSample("Weight"));

            var deletedResourceKey = await FhirRepository.DeleteAsync(new ResourceKey("Observation", saveResult.Resource.Id), false);

            Assert.NotEqual(saveResult.Resource.Meta.VersionId, deletedResourceKey.VersionId);

            await Assert.ThrowsAsync<ResourceGoneException>(
                () => FhirRepository.GetAsync(new ResourceKey<Observation>(saveResult.Resource.Id)));
        }

        [Fact]
        public async Task WhenHardDeletingAResource_ThenWeGetResourceNotFound()
        {
            var saveResult = await FhirRepository.UpsertAsync(Samples.GetJsonSample("Weight"));

            var deletedResourceKey = await FhirRepository.DeleteAsync(new ResourceKey("Observation", saveResult.Resource.Id), true);

            Assert.Null(deletedResourceKey.VersionId);

            // Subsequent get should result in NotFound.
            await Assert.ThrowsAsync<ResourceNotFoundException>(
                () => FhirRepository.GetAsync(new ResourceKey<Observation>(saveResult.Resource.Id)));

            // Subsequent version get should result in NotFound.
            await Assert.ThrowsAsync<ResourceNotFoundException>(
                () => FhirRepository.GetAsync(new ResourceKey<Observation>(saveResult.Resource.Id, saveResult.Resource.Meta.VersionId)));
        }

        [Fact]
        public async Task WhenHardDeletingAResource_ThenHistoryShouldBeDeleted()
        {
            var createResult = await FhirRepository.UpsertAsync(Samples.GetJsonSample("Weight"));

            string resourceId = createResult.Resource.Id;

            var deleteResult = await FhirRepository.DeleteAsync(new ResourceKey("Observation", resourceId), false);
            var updateResult = await FhirRepository.UpsertAsync(createResult.Resource);

            // Hard-delete the resource.
            var deletedResourceKey = await FhirRepository.DeleteAsync(new ResourceKey("Observation", resourceId), true);

            Assert.Null(deletedResourceKey.VersionId);

            // Subsequent get should result in NotFound.
            await Assert.ThrowsAsync<ResourceNotFoundException>(
                () => FhirRepository.GetAsync(new ResourceKey<Observation>(resourceId)));

            // Subsequent version get should result in NotFound.
            foreach (string versionId in new[] { createResult.Resource.VersionId, deleteResult.VersionId, updateResult.Resource.VersionId })
            {
                await Assert.ThrowsAsync<ResourceNotFoundException>(
                    () => FhirRepository.GetAsync(new ResourceKey<Observation>(resourceId, versionId)));
            }
        }

        [Fact]
        public async Task GivenAResourceSavedInRepository_AccessingANonValidVersion_ThenGetsNotFound()
        {
            var saveResult = await FhirRepository.UpsertAsync(Samples.GetJsonSample("Weight"));

            await Assert.ThrowsAsync<ResourceNotFoundException>(
                async () => { await FhirRepository.GetAsync(new ResourceKey<Observation>(saveResult.Resource.Id, Guid.NewGuid().ToString())); });
        }

        [Fact]
        public async Task WhenGettingNonExistentResource_ThenNotFoundIsThrown()
        {
            await Assert.ThrowsAsync<ResourceNotFoundException>(
                async () => { await FhirRepository.GetAsync(new ResourceKey<Observation>(Guid.NewGuid().ToString())); });
        }

        [Fact]
        public async Task WhenDeletingSpecificVersion_ThenMethodNotAllowedIsThrown()
        {
            await Assert.ThrowsAsync<MethodNotAllowedException>(
                async () => { await FhirRepository.DeleteAsync(new ResourceKey<Observation>(Guid.NewGuid().ToString(), Guid.NewGuid().ToString()), false); });
        }

        private async Task ExecuteAndVerifyException<TException>(Func<Task> action)
            where TException : Exception
        {
            await Assert.ThrowsAsync<TException>(action);
        }
    }
}
