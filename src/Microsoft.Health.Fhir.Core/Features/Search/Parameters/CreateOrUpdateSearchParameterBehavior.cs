// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public class CreateOrUpdateSearchParameterBehavior<TCreateResourceRequest, TUpsertResourceResponse> : IPipelineBehavior<CreateResourceRequest, UpsertResourceResponse>,
        IPipelineBehavior<UpsertResourceRequest, UpsertResourceResponse>
    {
        private ISearchParameterOperations _searchParameterOperations;
        private IFhirDataStore _fhirDataStore;

        public CreateOrUpdateSearchParameterBehavior(ISearchParameterOperations searchParameterOperations, IFhirDataStore fhirDataStore)
        {
            EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));

            _searchParameterOperations = searchParameterOperations;
            _fhirDataStore = fhirDataStore;
        }

        public async Task<UpsertResourceResponse> Handle(CreateResourceRequest request, RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
        {
            if (request.Resource.InstanceType.Equals(KnownResourceTypes.SearchParameter, StringComparison.Ordinal))
            {
                // Before committing the SearchParameter resource to the data store, add it to the SearchParameterDefinitionManager
                // and parse the fhirPath, as well as validate the parameter type
                await _searchParameterOperations.AddSearchParameterAsync(request.Resource.Instance, cancellationToken);
            }

            // Allow the resource to be updated with the normal handler
            var response = await next(cancellationToken);

            if (request.Resource.InstanceType.Equals(KnownResourceTypes.SearchParameter, StringComparison.Ordinal) && response.Outcome.Outcome == SaveOutcomeType.Created)
            {
                // Persist status only after the resource is successfully written.
                await _searchParameterOperations.AddSearchParameterStatusAsync(request.Resource.Instance, cancellationToken);
            }

            return response;
        }

        public async Task<UpsertResourceResponse> Handle(UpsertResourceRequest request, RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
        {
            // if the resource type being updated is a SearchParameter, then we want to query the previous version before it is changed
            // because we will need to the Url property to update the definition in the SearchParameterDefinitionManager
            // and the user could be changing the Url as part of this update
            var isSearchParameter = request.Resource.InstanceType.Equals(KnownResourceTypes.SearchParameter, StringComparison.Ordinal);
            ResourceWrapper prevSearchParamResource = null;

            if (isSearchParameter)
            {
                var resourceKey = new ResourceKey(request.Resource.InstanceType, request.Resource.Id, request.Resource.VersionId);

                try
                {
                    prevSearchParamResource = await _fhirDataStore.GetAsync(resourceKey, cancellationToken);
                }
                catch (ResourceNotFoundException)
                {
                    // Resource doesn't exist yet, which is valid for PUT operations (upsert behavior)
                    // We'll treat this as a create operation
                    prevSearchParamResource = null;
                }

                if (prevSearchParamResource != null && prevSearchParamResource.IsDeleted == false)
                {
                    // Update the SearchParameterDefinitionManager with the new SearchParameter in order to validate any changes
                    // to the fhirpath or the datatype
                    await _searchParameterOperations.UpdateSearchParameterAsync(request.Resource.Instance, prevSearchParamResource.RawResource, cancellationToken);
                }
                else
                {
                    // No previous version exists or it was deleted, so add it as a new SearchParameter
                    await _searchParameterOperations.AddSearchParameterAsync(request.Resource.Instance, cancellationToken);
                }
            }

            // Now allow the resource to updated per the normal behavior
            var response = await next(cancellationToken);

            if (isSearchParameter && (response.Outcome.Outcome == SaveOutcomeType.Created || response.Outcome.Outcome == SaveOutcomeType.Updated))
            {
                // Persist status only after the resource is successfully written.
                if (prevSearchParamResource != null && prevSearchParamResource.IsDeleted == false)
                {
                    await _searchParameterOperations.UpdateSearchParameterStatusAsync(request.Resource.Instance, prevSearchParamResource.RawResource, cancellationToken);
                }
                else
                {
                    await _searchParameterOperations.AddSearchParameterStatusAsync(request.Resource.Instance, cancellationToken);
                }
            }

            return response;
        }
    }
}
