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
#if R5
using Microsoft.Health.Fhir.Core.Features.Subscriptions;
#endif
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
#if R5
        private ISubscriptionListener _subscriptionListener;
#endif

        public UpsertResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ResourceIdProvider resourceIdProvider,
            IAuthorizationService<DataActions> authorizationService,
            IModelInfoProvider modelInfoProvider
#if R5
#pragma warning disable SA1001 // Commas should be spaced correctly
#pragma warning disable SA1113 // Comma should be on the same line as previous parameter
#pragma warning disable SA1115 // Parameter should follow comma
            , ISubscriptionListener subscriptionListener
#pragma warning restore SA1115 // Parameter should follow comma
#pragma warning restore SA1113 // Comma should be on the same line as previous parameter
#pragma warning restore SA1001 // Commas should be spaced correctly
#endif
#pragma warning disable SA1009 // Closing parenthesis should be spaced correctly
#pragma warning disable SA1111 // Closing parenthesis should be on line of last parameter
            )
#pragma warning restore SA1111 // Closing parenthesis should be on line of last parameter
#pragma warning restore SA1009 // Closing parenthesis should be spaced correctly
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _modelInfoProvider = modelInfoProvider;
#if R5
            EnsureArg.IsNotNull(subscriptionListener, nameof(subscriptionListener));
            _subscriptionListener = subscriptionListener;
#endif
        }

        public async Task<UpsertResourceResponse> Handle(UpsertResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await AuthorizationService.CheckAccess(DataActions.Write, cancellationToken) != DataActions.Write)
            {
                throw new UnauthorizedFhirActionException();
            }

            Resource resource = request.Resource.ToPoco<Resource>();

            if (await ConformanceProvider.Value.RequireETag(resource.TypeName, cancellationToken) && request.WeakETag == null)
            {
                throw new PreconditionFailedException(string.Format(Core.Resources.IfMatchHeaderRequiredForResource, resource.TypeName));
            }

            bool allowCreate = await ConformanceProvider.Value.CanUpdateCreate(resource.TypeName, cancellationToken);
            bool keepHistory = await ConformanceProvider.Value.CanKeepHistory(resource.TypeName, cancellationToken);

            ResourceWrapper resourceWrapper = CreateResourceWrapper(resource, deleted: false, keepMeta: allowCreate);

            UpsertOutcome result = await UpsertAsync(request, resourceWrapper, allowCreate, keepHistory, cancellationToken);

            resource.VersionId = result.Wrapper.Version;

#if R5
            await _subscriptionListener.Evaluate(resource, result.OutcomeType == SaveOutcomeType.Created ? SubscriptionTopic.InteractionTrigger.Create : SubscriptionTopic.InteractionTrigger.Update);
#endif
            return new UpsertResourceResponse(new SaveOutcome(new RawResourceElement(result.Wrapper), result.OutcomeType));
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
