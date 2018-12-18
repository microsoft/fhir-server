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
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Create
{
    public class CreateResourceHandler : BaseResourceHandler, IRequestHandler<CreateResourceRequest, UpsertResourceResponse>
    {
        public CreateResourceHandler(
            IDataStore dataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory)
            : base(dataStore, conformanceProvider, resourceWrapperFactory)
        {
        }

        public async Task<UpsertResourceResponse> Handle(CreateResourceRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            Resource resource = message.Resource;

            // If an Id is supplied on create it should be removed/ignored
            resource.Id = null;

            ResourceWrapper resourceWrapper = CreateResourceWrapper(resource, deleted: false);

            bool keepHistory = await ConformanceProvider.Value.CanKeepHistory(resource.TypeName, cancellationToken);

            UpsertOutcome result = await DataStore.UpsertAsync(
                resourceWrapper,
                weakETag: null,
                allowCreate: true,
                keepHistory: keepHistory,
                cancellationToken: cancellationToken);

            resource.VersionId = result.Wrapper.Version;

            return new UpsertResourceResponse(new SaveOutcome(resource, SaveOutcomeType.Created));
        }

        protected override void AddResourceCapability(ListedCapabilityStatement statement, ResourceType resourceType)
        {
            statement.TryAddRestInteraction(resourceType, CapabilityStatement.TypeRestfulInteraction.Create);
        }
    }
}
