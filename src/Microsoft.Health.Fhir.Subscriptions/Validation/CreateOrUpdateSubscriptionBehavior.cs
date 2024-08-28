// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Subscriptions.Validation;

namespace Microsoft.Health.Fhir.Core.Features.Subscriptions
{
    public class CreateOrUpdateSubscriptionBehavior<TCreateResourceRequest, TUpsertResourceResponse> : IPipelineBehavior<CreateResourceRequest, UpsertResourceResponse>,
        IPipelineBehavior<UpsertResourceRequest, UpsertResourceResponse>
     {
        private ISubscriptionValidator _subscriptionValidator;
        private IFhirDataStore _fhirDataStore;

        public CreateOrUpdateSubscriptionBehavior(ISubscriptionValidator subscriptionValidator, IFhirDataStore fhirDataStore)
        {
            EnsureArg.IsNotNull(subscriptionValidator, nameof(subscriptionValidator));
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));

            _subscriptionValidator = subscriptionValidator;
            _fhirDataStore = fhirDataStore;
        }

        public async Task<UpsertResourceResponse> Handle(CreateResourceRequest request, RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
        {
            if (request.Resource.InstanceType.Equals(KnownResourceTypes.Subscription, StringComparison.Ordinal))
            {
               request.Resource = await _subscriptionValidator.ValidateSubscriptionInput(request.Resource, cancellationToken);
            }

            // Allow the resource to be updated with the normal handler
            return await next();
        }

        public async Task<UpsertResourceResponse> Handle(UpsertResourceRequest request, RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
        {
            // if the resource type being updated is a SearchParameter, then we want to query the previous version before it is changed
            // because we will need to the Url property to update the definition in the SearchParameterDefinitionManager
            // and the user could be changing the Url as part of this update
            if (request.Resource.InstanceType.Equals(KnownResourceTypes.Subscription, StringComparison.Ordinal))
            {
                request.Resource = await _subscriptionValidator.ValidateSubscriptionInput(request.Resource, cancellationToken);
            }

            // Now allow the resource to updated per the normal behavior
            return await next();
        }
    }
}
