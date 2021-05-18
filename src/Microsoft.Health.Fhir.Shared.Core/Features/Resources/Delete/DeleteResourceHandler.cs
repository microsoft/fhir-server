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
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Delete;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Delete
{
    public class DeleteResourceHandler : BaseResourceHandler, IRequestHandler<DeleteResourceRequest, DeleteResourceResponse>
    {
        public DeleteResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ResourceIdProvider resourceIdProvider,
            IAuthorizationService<DataActions> authorizationService)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
        }

        public async Task<DeleteResourceResponse> Handle(DeleteResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            DataActions requiredDataAction = request.HardDelete ? DataActions.Delete | DataActions.HardDelete : DataActions.Delete;
            if (await AuthorizationService.CheckAccess(requiredDataAction, cancellationToken) != requiredDataAction)
            {
                throw new UnauthorizedFhirActionException();
            }

            var key = request.ResourceKey;

            if (!string.IsNullOrEmpty(key.VersionId))
            {
                throw new MethodNotAllowedException(Core.Resources.DeleteVersionNotAllowed);
            }

            string version = null;

            if (request.HardDelete)
            {
                await FhirDataStore.HardDeleteAsync(key, cancellationToken);
            }
            else
            {
                var emptyInstance = (Resource)Activator.CreateInstance(ModelInfo.GetTypeForFhirType(request.ResourceKey.ResourceType));
                emptyInstance.Id = request.ResourceKey.Id;

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

            return new DeleteResourceResponse(new ResourceKey(key.ResourceType, key.Id, version), weakETag: WeakETag.FromVersionId(version));
        }
    }
}
