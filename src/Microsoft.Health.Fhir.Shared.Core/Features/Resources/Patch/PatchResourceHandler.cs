// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.JsonPatch.Exceptions;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch
{
    public class PatchResourceHandler : BaseResourceHandler, IRequestHandler<PatchResourceRequest, PatchResourceResponse>
    {
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ResourceDeserializer _resourceDeserializer;

        public PatchResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ResourceIdProvider resourceIdProvider,
            IAuthorizationService<DataActions> authorizationService,
            IModelInfoProvider modelInfoProvider,
            ResourceDeserializer resourceDeserializer)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _modelInfoProvider = modelInfoProvider;
            _resourceDeserializer = resourceDeserializer;
        }

        public async Task<PatchResourceResponse> Handle(PatchResourceRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            if (await AuthorizationService.CheckAccess(DataActions.Write, cancellationToken) != DataActions.Write)
            {
                throw new UnauthorizedFhirActionException();
            }

            ResourceKey key = message.ResourceKey;

            if (!string.IsNullOrEmpty(key.VersionId))
            {
                throw new MethodNotAllowedException(Core.Resources.DeleteVersionNotAllowed);
            }

            ResourceWrapper currentDoc = await FhirDataStore.GetAsync(key, cancellationToken);

            if (currentDoc == null)
            {
                throw new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundById, key.ResourceType, key.Id));
            }

            if (currentDoc.IsHistory)
            {
                throw new MethodNotAllowedException(Core.Resources.PatchVersionNotAllowed);
            }

            ResourceElement resource = _resourceDeserializer.Deserialize(currentDoc);
            Resource resourceInstance = resource.Instance.ToPoco<Resource>();
            string resourceId = resourceInstance.Id;
            string resourceVersion = resourceInstance.VersionId;

            try
            {
                message.PatchDocument.ApplyTo(resourceInstance);

                if (resourceId != resourceInstance.Id ||
                    resourceVersion != resourceInstance.VersionId)
                {
                    throw new RequestNotValidException(Core.Resources.PatchImmutablePropertiesIsNotValid);
                }

                ResourceWrapper resourceWrapper = CreateResourceWrapper(resourceInstance, deleted: false, keepMeta: true);
                bool keepHistory = await ConformanceProvider.Value.CanKeepHistory(currentDoc.ResourceTypeName, cancellationToken);
                UpsertOutcome result = await FhirDataStore.UpsertAsync(resourceWrapper, message.WeakETag, false, keepHistory, cancellationToken);
                resourceInstance.VersionId = result.Wrapper.Version;

                return new PatchResourceResponse(new SaveOutcome(new RawResourceElement(result.Wrapper), result.OutcomeType));
            }
            catch (JsonPatchException e)
            {
                throw new RequestNotValidException(string.Format(Core.Resources.PatchResourceError, e.Message));
            }
        }
    }
}
