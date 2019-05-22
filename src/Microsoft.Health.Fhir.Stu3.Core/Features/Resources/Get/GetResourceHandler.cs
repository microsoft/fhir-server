// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Get
{
    public class GetResourceHandler : BaseResourceHandler, IRequestHandler<GetResourceRequest, GetResourceResponse>
    {
        private readonly ResourceDeserializer _deserializer;

        public GetResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ResourceDeserializer deserializer)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory)
        {
            EnsureArg.IsNotNull(deserializer, nameof(deserializer));

            _deserializer = deserializer;
        }

        public async Task<GetResourceResponse> Handle(GetResourceRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            var key = message.ResourceKey;

            var currentDoc = await FhirDataStore.GetAsync(key, cancellationToken);

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
                ConformanceProvider != null &&
                await ConformanceProvider.Value.CanReadHistory(key.ResourceType, cancellationToken) == false)
            {
                throw new MethodNotAllowedException(string.Format(Core.Resources.ReadHistoryDisabled, key.ResourceType));
            }

            if (currentDoc.IsDeleted)
            {
                // As per FHIR Spec if the resource was marked as deleted on that version or the latest is marked as deleted then
                // we need to return a resource gone message.
                throw new ResourceGoneException(new ResourceKey(currentDoc.ResourceTypeName, currentDoc.ResourceId, currentDoc.Version));
            }

            return new GetResourceResponse(_deserializer.Deserialize(currentDoc));
        }

        protected override void AddResourceCapability(IListedCapabilityStatement statement, string resourceType)
        {
            statement.TryAddRestInteraction(resourceType, TypeRestfulInteraction.Read);
            statement.TryAddRestInteraction(resourceType, TypeRestfulInteraction.Vread);
        }
    }
}
