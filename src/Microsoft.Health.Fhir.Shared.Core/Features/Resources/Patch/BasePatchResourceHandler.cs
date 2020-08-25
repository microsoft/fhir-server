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
using Hl7.Fhir.Patch;
using Hl7.Fhir.Patch.Exceptions;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch
{
    /// <summary>
    /// Handles patching a resource
    /// </summary>
    public abstract partial class BasePatchResourceHandler : BaseResourceHandler
    {
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ResourceDeserializer _resourceDeserializer;

        public BasePatchResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ResourceIdProvider resourceIdProvider,
            IFhirAuthorizationService authorizationService,
            IModelInfoProvider modelInfoProvider,
            ResourceDeserializer resourceDeserializer)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(resourceDeserializer);

            _modelInfoProvider = modelInfoProvider;
            _resourceDeserializer = resourceDeserializer;
        }

        protected async Task<PatchResourceResponse> ApplyPatch(ResourceWrapper currentDoc, IPatchDocument patchDocument, WeakETag weakETag, CancellationToken cancellationToken)
        {
            var resource = _resourceDeserializer.Deserialize(currentDoc);

            Resource patchedResource;
            try
            {
                patchedResource = resource.Instance.Apply(patchDocument).ToPoco<Resource>();
            }
            catch (PatchException ex)
            {
                throw new RequestNotValidException(ex.Message);
            }

            ResourceWrapper resourceWrapper = CreateResourceWrapper(patchedResource, deleted: false);

            // Validate immutable properties were not modified
            if (resourceWrapper.ResourceId != currentDoc.ResourceId ||
                resourceWrapper.ResourceTypeName != currentDoc.ResourceTypeName ||
                resourceWrapper.Version != currentDoc.Version)
            {
                throw new RequestNotValidException(Core.Resources.PatchImmutablePropertiesIsNotValid);
            }

            // Update the resource
            bool keepHistory = await ConformanceProvider.Value.CanKeepHistory(currentDoc.ResourceTypeName, cancellationToken);
            var result = await FhirDataStore.UpsertAsync(resourceWrapper, weakETag, false, keepHistory, cancellationToken);

            patchedResource.VersionId = result.Wrapper.Version;

            return new PatchResourceResponse(new SaveOutcome(patchedResource.ToResourceElement(), result.OutcomeType));
        }

        private async Task<UpsertOutcome> UpsertAsync(ResourceWrapper resourceWrapper, WeakETag weakETag, bool allowCreate, bool keepHistory, CancellationToken cancellationToken)
        {
            UpsertOutcome result;

            try
            {
                result = await FhirDataStore.UpsertAsync(resourceWrapper, weakETag, allowCreate, keepHistory, cancellationToken);
            }
            catch (PreconditionFailedException) when (_modelInfoProvider.Version == FhirSpecification.Stu3)
            {
                // The backwards compatibility behavior of Stu3 is to return a Conflict instead of Precondition fail
                throw new ResourceConflictException(weakETag);
            }

            return result;
        }
    }
}
