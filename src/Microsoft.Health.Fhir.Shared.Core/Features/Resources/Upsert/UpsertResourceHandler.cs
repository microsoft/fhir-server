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
using Hl7.Fhir.Rest;
using Medino;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Upsert
{
    /// <summary>
    /// Handles upserting a resource
    /// </summary>
    public partial class UpsertResourceHandler : BaseResourceHandler, IRequestHandler<UpsertResourceRequest, UpsertResourceResponse>
    {
        private readonly ResourceReferenceResolver _referenceResolver;
        private readonly Dictionary<string, (string resourceId, string resourceType)> _referenceIdDictionary;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;

        public UpsertResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ResourceIdProvider resourceIdProvider,
            ResourceReferenceResolver referenceResolver,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            IAuthorizationService<DataActions> authorizationService,
            IModelInfoProvider modelInfoProvider)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(referenceResolver, nameof(referenceResolver));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));

            _referenceResolver = referenceResolver;
            _modelInfoProvider = modelInfoProvider;
            _contextAccessor = contextAccessor;
            _referenceIdDictionary = new Dictionary<string, (string resourceId, string resourceType)>();
        }

        public async Task<UpsertResourceResponse> HandleAsync(UpsertResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            // Determine HTTP method, preferring Bundle context over request context
            Hl7.Fhir.Model.Bundle.HTTPVerb? method = request.BundleResourceContext?.HttpVerb;

            if (method == null && _contextAccessor?.RequestContext?.Method != null)
            {
                if (System.Enum.TryParse<Hl7.Fhir.Model.Bundle.HTTPVerb>(_contextAccessor.RequestContext.Method, true, out var parsedMethod))
                {
                    method = parsedMethod;
                }
            }

            if (method == Hl7.Fhir.Model.Bundle.HTTPVerb.POST)
            {
                // Explicit create via POST
                var granted = await AuthorizationService.CheckAccess(DataActions.Create | DataActions.Write, cancellationToken);
                if ((granted & (DataActions.Create | DataActions.Write)) == DataActions.None)
                {
                    throw new UnauthorizedFhirActionException();
                }
            }
            else if (method == Hl7.Fhir.Model.Bundle.HTTPVerb.PUT)
            {
                // Explicit update via PUT
                var granted = await AuthorizationService.CheckAccess(DataActions.Update | DataActions.Write, cancellationToken);
                if ((granted & (DataActions.Update | DataActions.Write)) == DataActions.None)
                {
                    throw new UnauthorizedFhirActionException();
                }
            }
            else
            {
                // Fallback when method is unavailable: infer from ETag/Id
                var tmp = request.Resource?.ToPoco<Resource>();
                if (string.IsNullOrEmpty(tmp?.Id))
                {
                    var granted = await AuthorizationService.CheckAccess(DataActions.Create | DataActions.Write, cancellationToken);
                    if ((granted & (DataActions.Create | DataActions.Write)) == DataActions.None)
                    {
                        throw new UnauthorizedFhirActionException();
                    }
                }
                else if (request.WeakETag != null)
                {
                    var granted = await AuthorizationService.CheckAccess(DataActions.Update | DataActions.Write, cancellationToken);
                    if ((granted & (DataActions.Update | DataActions.Write)) == DataActions.None)
                    {
                        throw new UnauthorizedFhirActionException();
                    }
                }
                else
                {
                    var granted = await AuthorizationService.CheckAccess(DataActions.Create | DataActions.Update | DataActions.Write, cancellationToken);
                    if ((granted & (DataActions.Create | DataActions.Update | DataActions.Write)) == DataActions.None)
                    {
                        throw new UnauthorizedFhirActionException();
                    }
                }
            }

            Resource resource = request.Resource.ToPoco<Resource>();

            bool allowCreate = await ConformanceProvider.Value.CanUpdateCreate(resource.TypeName, cancellationToken);
            bool keepHistory = await ConformanceProvider.Value.CanKeepHistory(resource.TypeName, cancellationToken);
            bool requireETagOnUpdate = await ConformanceProvider.Value.RequireETag(resource.TypeName, cancellationToken);

            await _referenceResolver.ResolveReferencesAsync(resource, _referenceIdDictionary, resource.TypeName, cancellationToken);

            ResourceWrapper resourceWrapper = ResourceWrapperFactory.CreateResourceWrapper(resource, ResourceIdProvider, deleted: false, keepMeta: allowCreate);

            UpsertOutcome result = await FhirDataStore.UpsertAsync(new ResourceWrapperOperation(resourceWrapper, allowCreate, keepHistory, request.WeakETag, requireETagOnUpdate, false, request.BundleResourceContext, request.MetaHistory), cancellationToken);

            resource.VersionId = result.Wrapper.Version;

            return new UpsertResourceResponse(new SaveOutcome(new RawResourceElement(result.Wrapper), result.OutcomeType));
        }
    }
}
