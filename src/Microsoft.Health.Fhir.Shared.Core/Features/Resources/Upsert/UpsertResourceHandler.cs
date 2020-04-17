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
            IFhirAuthorizationService authorizationService,
            IModelInfoProvider modelInfoProvider)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _modelInfoProvider = modelInfoProvider;
        }

        public async Task<UpsertResourceResponse> Handle(UpsertResourceRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            if (await AuthorizationService.CheckAccess(DataActions.Write) != DataActions.Write)
            {
                throw new UnauthorizedFhirActionException();
            }

            Resource resource = message.Resource.ToPoco<Resource>();

            if (await ConformanceProvider.Value.RequireETag(resource.TypeName, cancellationToken) && message.WeakETag == null)
            {
                throw new PreconditionFailedException(string.Format(Core.Resources.IfMatchHeaderRequiredForResource, resource.TypeName));
            }

            bool allowCreate = await ConformanceProvider.Value.CanUpdateCreate(resource.TypeName, cancellationToken);
            bool keepHistory = await ConformanceProvider.Value.CanKeepHistory(resource.TypeName, cancellationToken);

            ResourceWrapper resourceWrapper = CreateResourceWrapper(resource, deleted: false);

            UpsertOutcome result = await UpsertAsync(message, resourceWrapper, allowCreate, keepHistory, cancellationToken);

            resource.VersionId = result.Wrapper.Version;

            return new UpsertResourceResponse(new SaveOutcome(resource.ToResourceElement(), result.OutcomeType));
        }

        private async Task<UpsertOutcome> UpsertAsync(UpsertResourceRequest message, ResourceWrapper resourceWrapper, bool allowCreate, bool keepHistory, CancellationToken cancellationToken)
        {
            UpsertOutcome result;

            try
            {
                result = await FhirDataStore.UpsertAsync(resourceWrapper, message.WeakETag, allowCreate, keepHistory, cancellationToken);
            }
            catch (PreconditionFailedException) when (_modelInfoProvider.Version == FhirSpecification.Stu3)
            {
                // The backwards compatibility behavior of Stu3 is to return a Conflict instead of Precondition fail
                throw new ResourceConflictException(message.WeakETag);
            }

            return result;
        }
    }
}
