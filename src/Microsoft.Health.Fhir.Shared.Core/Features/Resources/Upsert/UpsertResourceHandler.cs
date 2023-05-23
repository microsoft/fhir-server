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
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
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
        private readonly IModelInfoProvider _modelInfoProvider;

        public UpsertResourceHandler(
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

        public async Task<UpsertResourceResponse> Handle(UpsertResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await AuthorizationService.CheckAccess(DataActions.Write, cancellationToken) != DataActions.Write)
            {
                throw new UnauthorizedFhirActionException();
            }

            Resource resource = request.Resource.ToPoco<Resource>();

            bool allowCreate = await ConformanceProvider.Value.CanUpdateCreate(resource.TypeName, cancellationToken);
            bool keepHistory = await ConformanceProvider.Value.CanKeepHistory(resource.TypeName, cancellationToken);
            bool requireETagOnUpdate = await ConformanceProvider.Value.RequireETag(resource.TypeName, cancellationToken);

            ResourceWrapper resourceWrapper = ResourceWrapperFactory.CreateResourceWrapper(resource, ResourceIdProvider, deleted: false, keepMeta: allowCreate);

            UpsertOutcome result = await FhirDataStore.UpsertAsync(new ResourceWrapperOperation(resourceWrapper, allowCreate, keepHistory, request.WeakETag, requireETagOnUpdate), cancellationToken);

            resource.VersionId = result.Wrapper.Version;

            return new UpsertResourceResponse(new SaveOutcome(new RawResourceElement(result.Wrapper), result.OutcomeType));
        }
    }
}
