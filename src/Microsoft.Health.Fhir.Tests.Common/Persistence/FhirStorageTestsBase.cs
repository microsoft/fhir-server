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
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Export;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Create;
using Microsoft.Health.Fhir.Core.Features.Resources.Delete;
using Microsoft.Health.Fhir.Core.Features.Resources.Get;
using Microsoft.Health.Fhir.Core.Features.Resources.Upsert;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
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

            var collection = new ServiceCollection();

            collection.AddSingleton(typeof(IRequestHandler<CreateResourceRequest, UpsertResourceResponse>), new CreateResourceHandler(dataStore, new Lazy<IConformanceProvider>(() => provider), _resourceWrapperFactory));
            collection.AddSingleton(typeof(IRequestHandler<UpsertResourceRequest, UpsertResourceResponse>), new UpsertResourceHandler(dataStore, new Lazy<IConformanceProvider>(() => provider), _resourceWrapperFactory));
            collection.AddSingleton(typeof(IRequestHandler<GetResourceRequest, GetResourceResponse>), new GetResourceHandler(dataStore, new Lazy<IConformanceProvider>(() => provider), _resourceWrapperFactory, Deserializers.ResourceDeserializer));
            collection.AddSingleton(typeof(IRequestHandler<DeleteResourceRequest, DeleteResourceResponse>), new DeleteResourceHandler(dataStore, new Lazy<IConformanceProvider>(() => provider), _resourceWrapperFactory));
            collection.AddSingleton(typeof(IRequestHandler<CreateExportRequest, CreateExportResponse>), new CreateExportRequestHandler(dataStore));
            collection.AddSingleton(typeof(IRequestHandler<GetExportRequest, GetExportResponse>), new GetExportRequestHandler(dataStore));

            ServiceProvider services = collection.BuildServiceProvider();

            Mediator = new Mediator(type => services.GetService(type));
        }

        protected Mediator Mediator { get; }

        [Fact]
        public async Task GivenAResource_WhenSaving_ThenTheMetaIsUpdated()
        {
            var instant = DateTimeOffset.Now;
            using (Mock.Property(() => Clock.UtcNowFunc, () => instant))
            {
                var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

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
            var saveResult = await Mediator.CreateResourceAsync(Samples.GetJsonSample("Weight"));
            var getResult = await Mediator.GetResourceAsync(new ResourceKey("Observation", saveResult.Id));

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
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams");
            newResourceValues.Id = saveResult.Resource.Id;

            var updateResult = await Mediator.UpsertResourceAsync(newResourceValues, WeakETag.FromVersionId(saveResult.Resource.VersionId));

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

            await Mediator.UpsertResourceAsync(weightSample);
            await Mediator.UpsertResourceAsync(patientSample);

            var fetchedResult1 = await Mediator.GetResourceAsync(new ResourceKey<Observation>(exampleId));
            var fetchedResult2 = await Mediator.GetResourceAsync(new ResourceKey<Patient>(exampleId));

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
            var ex = await Assert.ThrowsAsync<MethodNotAllowedException>(() => Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight")));

            Assert.Equal(Resources.ResourceCreationNotAllowed, ex.Message);
        }

        [Fact]
        public async Task GivenANewResource_WhenUpsertingWithCreateEnabledAndJunkEtag_ThenAMethodNotAllowedExceptionIsThrown()
        {
            var observation = _conformance.Rest[0].Resource.Find(r => r.Type == ResourceType.Observation);
            observation.UpdateCreate = true;
            observation.Versioning = CapabilityStatement.ResourceVersionPolicy.Versioned;
            await Assert.ThrowsAsync<ResourceConflictException>(() => Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"), WeakETag.FromVersionId("junk")));
        }

        [Fact]
        public async Task GivenANewResource_WhenUpsertingWithCreateDisabledAndJunkEtag_ThenAMethodNotAllowedExceptionIsThrown()
        {
            var observation = _conformance.Rest[0].Resource.Find(r => r.Type == ResourceType.Observation);
            observation.UpdateCreate = false;
            observation.Versioning = CapabilityStatement.ResourceVersionPolicy.Versioned;
            await Assert.ThrowsAsync<ResourceConflictException>(() => Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"), WeakETag.FromVersionId("junk")));
        }

        [Fact]
        public async Task GivenASavedResource_WhenUpsertingWithNoVersionId_ThenTheExistingResourceIsUpdated()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams");
            newResourceValues.Id = saveResult.Resource.Id;

            var updateResult = await Mediator.UpsertResourceAsync(newResourceValues);

            Assert.NotNull(updateResult);
            Assert.Equal(SaveOutcomeType.Updated, updateResult.Outcome);

            Assert.NotNull(updateResult.Resource);
            Assert.Equal(saveResult.Resource.Id, updateResult.Resource.Id);
        }

        [Fact]
        public async Task GivenASavedResource_WhenConcurrentlyUpsertingWithNoVersionId_ThenTheExistingResourceIsUpdated()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

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
                list.Add(Mediator.UpsertResourceAsync(CloneResource(i)));
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
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams");
            newResourceValues.Id = saveResult.Resource.Id;

            await Assert.ThrowsAsync<ResourceConflictException>(async () => await Mediator.UpsertResourceAsync(newResourceValues, WeakETag.FromVersionId("incorrectVersion")));
        }

        [Fact]
        public async Task GivenAResourceWithNoHistory_WhenFetchingByVersionId_ThenReadWorksCorrectly()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var result = await Mediator.GetResourceAsync(new ResourceKey(saveResult.Resource.TypeName, saveResult.Resource.Id, saveResult.Resource.VersionId));

            Assert.NotNull(result);
            Assert.Equal(saveResult.Resource.Id, result.Id);
        }

        [Fact]
        public async Task UpdatingAResource_ThenWeCanAccessHistoricValues()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams");
            newResourceValues.Id = saveResult.Resource.Id;

            var updateResult = await Mediator.UpsertResourceAsync(newResourceValues, WeakETag.FromVersionId(saveResult.Resource.VersionId));

            var getV1Result = await Mediator.GetResourceAsync(new ResourceKey<Observation>(saveResult.Resource.Id, saveResult.Resource.VersionId));

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
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetDefaultOrganization());

            var newResourceValues = Samples.GetDefaultOrganization();
            newResourceValues.Id = saveResult.Resource.Id;

            var updateResult = await Mediator.UpsertResourceAsync(newResourceValues, WeakETag.FromVersionId(saveResult.Resource.VersionId));

            await Assert.ThrowsAsync<ResourceNotFoundException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Organization>(saveResult.Resource.Id, saveResult.Resource.VersionId)));
        }

        [Fact]
        public async Task WhenDeletingAResource_ThenWeGetResourceGone()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var deletedResourceKey = await Mediator.DeleteResourceAsync(new ResourceKey("Observation", saveResult.Resource.Id), false);

            Assert.NotEqual(saveResult.Resource.Meta.VersionId, deletedResourceKey.ResourceKey.VersionId);

            await Assert.ThrowsAsync<ResourceGoneException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Observation>(saveResult.Resource.Id)));
        }

        [Fact]
        public async Task WhenHardDeletingAResource_ThenWeGetResourceNotFound()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var deletedResourceKey = await Mediator.DeleteResourceAsync(new ResourceKey("Observation", saveResult.Resource.Id), true);

            Assert.Null(deletedResourceKey.ResourceKey.VersionId);

            // Subsequent get should result in NotFound.
            await Assert.ThrowsAsync<ResourceNotFoundException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Observation>(saveResult.Resource.Id)));

            // Subsequent version get should result in NotFound.
            await Assert.ThrowsAsync<ResourceNotFoundException>(
                () => Mediator.GetResourceAsync(new ResourceKey<Observation>(saveResult.Resource.Id, saveResult.Resource.Meta.VersionId)));
        }

        [Fact]
        public async Task WhenHardDeletingAResource_ThenHistoryShouldBeDeleted()
        {
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
        public async Task WhenGivenExistingExportRequest_WhenGettingExportStatus_ThenGetsJobExists()
        {
            Uri requestUri = new Uri("https://localhost/$export");
            var result = await Mediator.ExportAsync(requestUri);

            Assert.True(result.JobCreated);

            requestUri = new Uri("https://localhost/_operation/export/" + result.Id);
            var exportStatus = await Mediator.GetExportStatusAsync(requestUri, result.Id);

            Assert.True(exportStatus.JobExists);
        }

        [Fact]
        public async Task WhenExportRequestDoesNotExist_WhenGettingExportStatus_ThenGetsJobDoesNotExist()
        {
            string id = Guid.NewGuid().ToString();
            Uri requestUri = new Uri("https://localhost/_operation/export/" + id);
            var exportStatus = await Mediator.GetExportStatusAsync(requestUri, id);

            Assert.False(exportStatus.JobExists);
        }

        private async Task ExecuteAndVerifyException<TException>(Func<Task> action)
            where TException : Exception
        {
            await Assert.ThrowsAsync<TException>(action);
        }
    }
}
