// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Medino;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Create
{
    public class CreateResourceHandler : BaseResourceHandler, IRequestHandler<CreateResourceRequest, UpsertResourceResponse>
    {
        private readonly ResourceReferenceResolver _referenceResolver;
        private readonly Dictionary<string, (string resourceId, string resourceType)> _referenceIdDictionary;

        public CreateResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ResourceIdProvider resourceIdProvider,
            ResourceReferenceResolver referenceResolver,
            IAuthorizationService<DataActions> authorizationService)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            EnsureArg.IsNotNull(referenceResolver, nameof(referenceResolver));

            _referenceResolver = referenceResolver;
            _referenceIdDictionary = new Dictionary<string, (string resourceId, string resourceType)>();
        }

        public async Task<UpsertResourceResponse> HandleAsync(CreateResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            // Check for granular Create permission (SMART v2) or legacy Write permission (SMART v1/backward compatibility)
            DataActions requiredActions = DataActions.Create | DataActions.Write;
            DataActions allowedActions = await AuthorizationService.CheckAccess(requiredActions, cancellationToken);

            if ((allowedActions & requiredActions) == DataActions.None)
            {
                throw new UnauthorizedFhirActionException();
            }

            var resource = request.Resource.ToPoco<Resource>();

            // In a POST request, a Transactional Bundle processing defines the ID of resources being created when a full url is provided (as part of the bundle entry).
            // After defined, the IDs are used to update references between records in the same bundle.
            // If the record (in the current request) is part of a transaction and had an ID assigned to it, then the ID should be preserved.
            if (IsBundleParallelTransaction(request) && !string.IsNullOrWhiteSpace(request.BundleResourceContext?.PersistedId))
            {
                // The following check ensures that the resource ID provided in the request matches the ID in the bundle context.
                // This is important to maintain consistency and integrity of the resource being created.
                // If the IDs do not match, an exception is thrown to prevent any inconsistencies.
                if (!string.Equals(resource.Id, request.BundleResourceContext.PersistedId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Bundle failure. Resource ID mismatch: The ID generated when handling the bundle does not match with the ID in this context. Expected '{request.BundleResourceContext.PersistedId}', but got '{resource.Id}'.");
                }
            }
            else
            {
                resource.Id = null;
            }

            await _referenceResolver.ResolveReferencesAsync(resource, _referenceIdDictionary, resource.TypeName, cancellationToken);

            ResourceWrapper resourceWrapper = ResourceWrapperFactory.CreateResourceWrapper(resource, ResourceIdProvider, deleted: false, keepMeta: true);
            bool keepHistory = await ConformanceProvider.Value.CanKeepHistory(resource.TypeName, cancellationToken);

            UpsertOutcome result = await FhirDataStore.UpsertAsync(new ResourceWrapperOperation(resourceWrapper, true, keepHistory, null, false, false, request.BundleResourceContext), cancellationToken);

            resource.VersionId = result.Wrapper.Version;

            return new UpsertResourceResponse(new SaveOutcome(new RawResourceElement(result.Wrapper), SaveOutcomeType.Created));
        }

        private static bool IsBundleParallelTransaction(CreateResourceRequest request)
        {
            return request.IsBundleInnerRequest &&
                request.BundleResourceContext.BundleType.HasValue &&
                request.BundleResourceContext.BundleType == Hl7.Fhir.Model.Bundle.BundleType.Transaction &&
                request.BundleResourceContext.ProcessingLogic == BundleProcessingLogic.Parallel;
        }
    }
}
