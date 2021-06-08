// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.FhirPath;
using MediatR;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch
{
    public class PatchResourceHandler : BaseResourceHandler, IRequestHandler<PatchResourceRequest, UpsertResourceResponse>
    {
        private readonly IModelInfoProvider _modelInfoProvider;

        private readonly ISet<string> _immutableProperties = new HashSet<string>
        {
            "Resource.id",
            "Resource.meta.lastUpdated",
            "Resource.meta.version",
            "Resource.text.div",
            "Resource.text.status",
        };

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

        public async Task<UpsertResourceResponse> Handle(PatchResourceRequest request, CancellationToken cancellationToken)
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

            if (request.WeakETag != null && request.WeakETag.VersionId != currentDoc.Version)
            {
                throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, request.WeakETag.VersionId));
            }

            if (currentDoc.RawResource.Format != FhirResourceFormat.Json)
            {
                throw new RequestNotValidException(Core.Resources.PatchResourceMustBeJson);
            }

            try
            {
                // Use low-level JSON parser
                var node = (FhirJsonNode)FhirJsonNode.Parse(currentDoc.RawResource.Data);

                // Capture the state of properties that are immutable
                ITypedElement resource = node.ToTypedElement(_modelInfoProvider.StructureDefinitionSummaryProvider);
                (string path, object result)[] preState = _immutableProperties.Select(x => (path: x, result: resource.Scalar(x))).ToArray();

                request.PatchDocument.ApplyTo(node.JsonObject);

                // Check immutable properties are not modified
                resource = node.ToTypedElement(_modelInfoProvider.StructureDefinitionSummaryProvider);
                (string path, object result)[] postState = _immutableProperties.Select(x => (path: x, result: resource.Scalar(x))).ToArray();

                if (!preState.Zip(postState).All(x => x.First.path == x.Second.path && string.Equals(x.First.result?.ToString(), x.Second.result?.ToString(), StringComparison.Ordinal)))
                {
                    throw new RequestNotValidException(Core.Resources.PatchImmutablePropertiesIsNotValid);
                }

                // Persist
                Resource resourcePoco = resource.ToPoco<Resource>();

                ResourceWrapper resourceWrapper = CreateResourceWrapper(resourcePoco, deleted: false, keepMeta: true);
                bool keepHistory = await ConformanceProvider.Value.CanKeepHistory(currentDoc.ResourceTypeName, cancellationToken);
                UpsertOutcome result = await FhirDataStore.UpsertAsync(resourceWrapper, WeakETag.FromVersionId(currentDoc.Version), false, keepHistory, cancellationToken);

                return new(new SaveOutcome(new RawResourceElement(result.Wrapper), result.OutcomeType));
            }
            catch (Exception e)
            {
                throw new RequestNotValidException(string.Format(Core.Resources.PatchResourceError, e.Message));
            }
        }
    }
}
