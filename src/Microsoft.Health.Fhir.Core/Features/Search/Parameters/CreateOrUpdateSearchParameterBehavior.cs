// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
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

        public async Task<UpsertResourceResponse> Handle(CreateResourceRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<UpsertResourceResponse> next)
        {
            // Allow the resource to be updated with the normal handler
            var response = await next();

            if (request.Resource.InstanceType.Equals(KnownResourceTypes.SearchParameter, StringComparison.Ordinal))
            {
                // Once the SearchParameter resource is committed to the data store, we can update the in
                // memory SearchParameterDefinitionManager, and persist the status to the data store
                await _searchParameterOperations.AddSearchParameterAsync(request.Resource.Instance);
            }

            return response;
        }

        public async Task<UpsertResourceResponse> Handle(UpsertResourceRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<UpsertResourceResponse> next)
        {
            ResourceWrapper prevSearchParamResource = null;
            bool isSearchParameter = false;

            // if the resource type being updated is a SearchParaemter, then we want to query the previous version before it is changed
            // because we will need to the Url property to update the definition in the SearchParameterDefinitionManager
            // and the user could be changing the Url as part of this update
            if (request.Resource.InstanceType.Equals(KnownResourceTypes.SearchParameter, StringComparison.Ordinal))
            {
                isSearchParameter = true;
                var resourceKey = new ResourceKey(request.Resource.InstanceType, request.Resource.Id, request.Resource.VersionId);
                prevSearchParamResource = await _fhirDataStore.GetAsync(resourceKey, cancellationToken);
            }

            // Now allow the resource to updated per the normal behavior
            var response = await next();

            if (isSearchParameter)
            {
                // Once the SearchParameter resource is update in the data store, we will update
                // the metadata in the SearchParameterDefinitionManager
                await _searchParameterOperations.UpdateSearchParameterAsync(request.Resource.Instance, prevSearchParamResource.RawResource);
            }

            return response;
        }
    }
}
