// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class FhirRepository : IFhirRepository, IProvideCapability
    {
        private readonly IDataStore _dataStore;
        private readonly Lazy<IConformanceProvider> _conformanceProvider;
        private readonly IResourceWrapperFactory _resourceWrapperFactory;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;

        public FhirRepository(
            IDataStore dataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            IFhirRequestContextAccessor fhirRequestContextAccessor)
        {
            EnsureArg.IsNotNull(dataStore, nameof(dataStore));
            EnsureArg.IsNotNull(resourceWrapperFactory, nameof(resourceWrapperFactory));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));

            _dataStore = dataStore;
            _conformanceProvider = conformanceProvider;
            _resourceWrapperFactory = resourceWrapperFactory;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
        }

        public async Task<Resource> CreateAsync(Resource resource, CancellationToken cancellationToken = default(CancellationToken))
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            // If an Id is supplied on create it should be removed/ignored
            resource.Id = null;

            ResourceWrapper resourceWrapper = CreateResourceWrapper(resource, deleted: false);

            bool keepHistory = await _conformanceProvider.Value.CanKeepHistory(resource.TypeName, cancellationToken);

            UpsertOutcome result = await _dataStore.UpsertAsync(
                resourceWrapper,
                weakETag: null,
                allowCreate: true,
                keepHistory: keepHistory,
                cancellationToken: cancellationToken);

            resource.VersionId = result.Wrapper.Version;

            return resource;
        }

        public async Task<SaveOutcome> UpsertAsync(Resource resource, WeakETag weakETag = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            if (await _conformanceProvider.Value.RequireETag(resource.TypeName, cancellationToken) && weakETag == null)
            {
                throw new PreconditionFailedException(string.Format(Core.Resources.IfMatchHeaderRequiredForResource, resource.TypeName));
            }

            bool allowCreate = await _conformanceProvider.Value.CanUpdateCreate(resource.TypeName, cancellationToken);
            bool keepHistory = await _conformanceProvider.Value.CanKeepHistory(resource.TypeName, cancellationToken);

            ResourceWrapper resourceWrapper = CreateResourceWrapper(resource, deleted: false);
            UpsertOutcome result = await _dataStore.UpsertAsync(resourceWrapper, weakETag, allowCreate, keepHistory, cancellationToken);
            resource.VersionId = result.Wrapper.Version;

            return new SaveOutcome(resource, result.OutcomeType);
        }

        public async Task<Resource> GetAsync(ResourceKey key, CancellationToken cancellationToken = default(CancellationToken))
        {
            EnsureArg.IsNotNull(key, nameof(key));

            var currentDoc = await _dataStore.GetAsync(key, cancellationToken);

            if (currentDoc == null)
            {
                if (string.IsNullOrEmpty(key.VersionId))
                {
                    throw new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundById, key.ResourceType, key.Id));
                }
                else
                {
                    throw new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundByIdAndVersion, key.ResourceType, key.Id, key.VersionId));
                }
            }

            if (currentDoc.IsHistory &&
                _conformanceProvider != null &&
                await _conformanceProvider.Value.CanReadHistory(key.ResourceType, cancellationToken) == false)
            {
                throw new MethodNotAllowedException(string.Format(Core.Resources.ReadHistoryDisabled, key.ResourceType));
            }

            if (currentDoc.IsDeleted)
            {
                // As per FHIR Spec if the resource was marked as deleted on that version or the latest is marked as deleted then
                // we need to return a resource gone message.
                throw new ResourceGoneException(new ResourceKey(currentDoc.ResourceTypeName, currentDoc.ResourceId, currentDoc.Version));
            }

            return ResourceDeserializer.Deserialize(currentDoc);
        }

        public async Task<ResourceKey> DeleteAsync(ResourceKey key, bool hardDelete, CancellationToken cancellationToken = default(CancellationToken))
        {
            EnsureArg.IsNotNull(key, nameof(key));

            if (!string.IsNullOrEmpty(key.VersionId))
            {
                throw new MethodNotAllowedException(Core.Resources.DeleteVersionNotAllowed);
            }

            string version = null;

            if (hardDelete)
            {
                await _dataStore.HardDeleteAsync(key, cancellationToken);
            }
            else
            {
                ResourceWrapper existing = await _dataStore.GetAsync(key, cancellationToken);

                version = existing?.Version;

                if (existing?.IsDeleted == false)
                {
                    var emptyInstance = (Resource)Activator.CreateInstance(ModelInfo.GetTypeForFhirType(existing.ResourceTypeName));
                    emptyInstance.Id = existing.ResourceId;

                    ResourceWrapper deletedWrapper = CreateResourceWrapper(emptyInstance, deleted: true);

                    bool keepHistory = await _conformanceProvider.Value.CanKeepHistory(key.ResourceType, cancellationToken);

                    UpsertOutcome result = await _dataStore.UpsertAsync(
                        deletedWrapper,
                        WeakETag.FromVersionId(existing.Version),
                        allowCreate: true,
                        keepHistory: keepHistory,
                        cancellationToken: cancellationToken);

                    version = result.Wrapper.Version;
                }
            }

            return new ResourceKey(key.ResourceType, key.Id, version);
        }

        private ResourceWrapper CreateResourceWrapper(Resource obj, bool deleted)
        {
            if (string.IsNullOrEmpty(obj.Id))
            {
                obj.Id = Guid.NewGuid().ToString();
            }

            if (obj.Meta == null)
            {
                obj.Meta = new Meta();
            }

            obj.Meta.LastUpdated = Clock.UtcNow;

            var resourceWrapper = _resourceWrapperFactory.Create(obj, deleted);
            return resourceWrapper;
        }

        public void Build(ListedCapabilityStatement statement)
        {
            foreach (var resource in ModelInfo.SupportedResources)
            {
                var resourceType = (ResourceType)Enum.Parse(typeof(ResourceType), resource);
                statement.TryAddRestInteraction(resourceType, CapabilityStatement.TypeRestfulInteraction.Create);
                statement.TryAddRestInteraction(resourceType, CapabilityStatement.TypeRestfulInteraction.Delete);
                statement.TryAddRestInteraction(resourceType, CapabilityStatement.TypeRestfulInteraction.Read);
                statement.TryAddRestInteraction(resourceType, CapabilityStatement.TypeRestfulInteraction.Update);
                statement.TryAddRestInteraction(resourceType, CapabilityStatement.TypeRestfulInteraction.Vread);
            }
        }
    }
}
