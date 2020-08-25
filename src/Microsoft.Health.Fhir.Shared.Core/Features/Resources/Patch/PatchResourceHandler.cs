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
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch
{
    /// <summary>
    /// Handles pathing a resource
    /// </summary>
    public partial class PatchResourceHandler : BasePatchResourceHandler, IRequestHandler<PatchResourceRequest, PatchResourceResponse>
    {
        public PatchResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ResourceIdProvider resourceIdProvider,
            IFhirAuthorizationService authorizationService,
            IModelInfoProvider modelInfoProvider,
            ResourceDeserializer resourceDeserializer)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService, modelInfoProvider, resourceDeserializer)
        {
        }

        public async Task<PatchResourceResponse> Handle(PatchResourceRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            if (await AuthorizationService.CheckAccess(DataActions.Write) != DataActions.Write)
            {
                throw new UnauthorizedFhirActionException();
            }

            ResourceKey key = message.ResourceKey;

            if (await ConformanceProvider.Value.RequireETag(key.ResourceType, cancellationToken) && message.WeakETag == null)
            {
                throw new PreconditionFailedException(string.Format(Core.Resources.IfMatchHeaderRequiredForResource, key.ResourceType));
            }

            var currentDoc = await FhirDataStore.GetAsync(key, cancellationToken);

            if (currentDoc == null)
            {
                throw new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundById, key.ResourceType, key.Id));
            }

            if (currentDoc.IsHistory)
            {
                throw new MethodNotAllowedException(Core.Resources.PatchVersionNotAllowed);
            }

            if (currentDoc.IsDeleted)
            {
                throw new ResourceGoneException(new ResourceKey(currentDoc.ResourceTypeName, currentDoc.ResourceId, currentDoc.Version));
            }

            return await ApplyPatch(currentDoc, message.PatchDocument, message.WeakETag, cancellationToken);
        }
    }
}
