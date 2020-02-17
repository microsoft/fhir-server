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
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Upsert
{
    /// <summary>
    /// Handles upserting a resource
    /// </summary>
    public partial class UpsertResourceHandler : BaseResourceHandler, IRequestHandler<UpsertResourceRequest, UpsertResourceResponse>
    {
        public UpsertResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ResourceIdProvider resourceIdProvider,
            IFhirAuthorizationService authorizationService)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
        }

        public async Task<UpsertResourceResponse> Handle(UpsertResourceRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            DataActions permittedDataActions = AuthorizationService.CheckAccess(DataActions.Write);

            if (!permittedDataActions.HasFlag(DataActions.Update))
            {
                throw new UnauthorizedFhirActionException();
            }

            Resource resource = message.Resource.Instance.ToPoco<Resource>();

            if (await ConformanceProvider.Value.RequireETag(resource.TypeName, cancellationToken) && message.WeakETag == null)
            {
                throw new PreconditionFailedException(string.Format(Core.Resources.IfMatchHeaderRequiredForResource, resource.TypeName));
            }

            bool principalAllowedToCreate = permittedDataActions.HasFlag(DataActions.Create);
            bool allowCreate = principalAllowedToCreate && await ConformanceProvider.Value.CanUpdateCreate(resource.TypeName, cancellationToken);
            bool keepHistory = await ConformanceProvider.Value.CanKeepHistory(resource.TypeName, cancellationToken);

            ResourceWrapper resourceWrapper = CreateResourceWrapper(resource, deleted: false);
            try
            {
                UpsertOutcome result = await UpsertAsync(message, resourceWrapper, allowCreate, keepHistory, cancellationToken);

                resource.VersionId = result.Wrapper.Version;

                return new UpsertResourceResponse(new SaveOutcome(resource.ToResourceElement(), result.OutcomeType));
            }
            catch (MethodNotAllowedException) when (!principalAllowedToCreate)
            {
                throw new UnauthorizedFhirActionException();
            }
        }
    }
}
