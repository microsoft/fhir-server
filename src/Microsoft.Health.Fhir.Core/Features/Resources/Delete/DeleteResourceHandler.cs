// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Delete;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Delete
{
    public class DeleteResourceHandler : BaseResourceHandler, IRequestHandler<DeleteResourceRequest, DeleteResourceResponse>
    {
        public DeleteResourceHandler(
            IDataStore dataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory)
            : base(dataStore, conformanceProvider, resourceWrapperFactory)
        {
        }

        public async Task<DeleteResourceResponse> Handle(DeleteResourceRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            var key = message.ResourceKey;

            if (!string.IsNullOrEmpty(key.VersionId))
            {
                throw new MethodNotAllowedException(Core.Resources.DeleteVersionNotAllowed);
            }

            string version = null;

            if (message.HardDelete)
            {
                await DataStore.HardDeleteAsync(key, cancellationToken);
            }
            else
            {
                ResourceWrapper existing = await DataStore.GetAsync(key, cancellationToken);

                version = existing?.Version;

                if (existing?.IsDeleted == false)
                {
                    var emptyInstance = (Resource)Activator.CreateInstance(ModelInfo.GetTypeForFhirType(existing.ResourceTypeName));
                    emptyInstance.Id = existing.ResourceId;

                    ResourceWrapper deletedWrapper = CreateResourceWrapper(emptyInstance, deleted: true);

                    bool keepHistory = await ConformanceProvider.Value.CanKeepHistory(key.ResourceType, cancellationToken);

                    UpsertOutcome result = await DataStore.UpsertAsync(
                        deletedWrapper,
                        WeakETag.FromVersionId(existing.Version),
                        allowCreate: true,
                        keepHistory: keepHistory,
                        cancellationToken: cancellationToken);

                    version = result.Wrapper.Version;
                }
            }

            if (string.IsNullOrWhiteSpace(version))
            {
                return new DeleteResourceResponse(new ResourceKey(key.ResourceType, key.Id));
            }

            return new DeleteResourceResponse(new ResourceKey(key.ResourceType, key.Id, version), WeakETag.FromVersionId(version));
        }

        protected override void AddResourceCapability(ListedCapabilityStatement statement, ResourceType resourceType)
        {
            statement.TryAddRestInteraction(resourceType, CapabilityStatement.TypeRestfulInteraction.Delete);
        }
    }
}
