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
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Delete
{
    public class DeleteResourceHandler : BaseResourceHandler, IRequestHandler<DeleteResourceRequest, DeleteResourceResponse>
    {
        public DeleteResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ResourceIdProvider resourceIdProvider,
            IFhirAuthorizationService authorizationService)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
        }

        public async Task<DeleteResourceResponse> Handle(DeleteResourceRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            DataActions requiredDataAction = message.HardDelete ? DataActions.Delete | DataActions.HardDelete : DataActions.Delete;
            if (await AuthorizationService.CheckAccess(requiredDataAction) != requiredDataAction)
            {
                throw new UnauthorizedFhirActionException();
            }

            var key = message.ResourceKey;

            if (!string.IsNullOrEmpty(key.VersionId))
            {
                throw new MethodNotAllowedException(Core.Resources.DeleteVersionNotAllowed);
            }

            string version = null;

            if (message.HardDelete)
            {
                await FhirDataStore.HardDeleteAsync(key, cancellationToken);
            }
            else
            {
                var emptyInstance = (Resource)Activator.CreateInstance(ModelInfo.GetTypeForFhirType(message.ResourceKey.ResourceType));
                emptyInstance.Id = message.ResourceKey.Id;

                ResourceWrapper deletedWrapper = CreateResourceWrapper(emptyInstance, deleted: true, keepMeta: false);

                bool keepHistory = await ConformanceProvider.Value.CanKeepHistory(key.ResourceType, cancellationToken);

                UpsertOutcome result = await FhirDataStore.UpsertAsync(
                    deletedWrapper,
                    weakETag: null,
                    allowCreate: true,
                    keepHistory: keepHistory,
                    cancellationToken: cancellationToken);

                version = result?.Wrapper.Version;
            }

            if (string.IsNullOrWhiteSpace(version))
            {
                return new DeleteResourceResponse(new ResourceKey(key.ResourceType, key.Id));
            }

            return new DeleteResourceResponse(new ResourceKey(key.ResourceType, key.Id, version), WeakETag.FromVersionId(version));
        }
    }
}
