// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch
{
    public class PatchResourceHandler : BaseResourceHandler, IRequestHandler<PatchResourceRequest, PatchResourceResponse>
    {
        private readonly IModelInfoProvider _modelInfoProvider;

        public PatchResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ResourceIdProvider resourceIdProvider,
            IAuthorizationService<DataActions> authorizationService,
            IModelInfoProvider modelInfoProvider)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _modelInfoProvider = modelInfoProvider;
        }

        public async Task<PatchResourceResponse> Handle(PatchResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await AuthorizationService.CheckAccess(DataActions.Write, cancellationToken) != DataActions.Write)
            {
                throw new UnauthorizedFhirActionException();
            }

            ResourceKey key = request.ResourceKey;

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

            try
            {
                ISourceNode node = FhirJsonNode.Parse(currentDoc.RawResource.Data);
                Resource resource = node.ToTypedElement(_modelInfoProvider.StructureDefinitionSummaryProvider).ToPoco<Resource>();
                JObject nodeJson = node.ToJObject();
                string narrative = resource.Scalar(KnownFhirPaths.ResourceNarrative) as string;
                string narrativeStatus = (string)nodeJson["text"]["status"];

                request.PatchDocument.ApplyTo(nodeJson);

                FhirJsonNode nodePatch = (FhirJsonNode)FhirJsonNode.Create(nodeJson);
                Resource resourcePatch = nodePatch.ToTypedElement(_modelInfoProvider.StructureDefinitionSummaryProvider).ToPoco<Resource>();
                string narrativePatch = resourcePatch.Scalar(KnownFhirPaths.ResourceNarrative) as string;
                string narrativeStatusPatch = (string)nodeJson["text"]["status"];

                if (resource.Id != resourcePatch.Id ||
                    resource.VersionId != resourcePatch.VersionId ||
                    resource.Meta.LastUpdated.Value != resourcePatch.Meta.LastUpdated.Value ||
                    narrative != narrativePatch ||
                    narrativeStatus != narrativeStatusPatch)
                {
                    throw new RequestNotValidException(Core.Resources.PatchImmutablePropertiesIsNotValid);
                }

                ResourceWrapper resourceWrapper = CreateResourceWrapper(resourcePatch, deleted: false, keepMeta: true);
                bool keepHistory = await ConformanceProvider.Value.CanKeepHistory(currentDoc.ResourceTypeName, cancellationToken);
                UpsertOutcome result = await FhirDataStore.UpsertAsync(resourceWrapper, request.WeakETag, false, keepHistory, cancellationToken);
                resourcePatch.VersionId = result.Wrapper.Version;

                return new PatchResourceResponse(new SaveOutcome(new RawResourceElement(result.Wrapper), result.OutcomeType));
            }
            catch (Exception e)
            {
                throw new RequestNotValidException(string.Format(Core.Resources.PatchResourceError, e.Message));
            }
        }
    }
}
