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
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public class DeleteSearchParameterBehavior<TDeleteResourceRequest, TDeleteResourceResponse> : IPipelineBehavior<TDeleteResourceRequest, TDeleteResourceResponse>
        where TDeleteResourceRequest : DeleteResourceRequest, IRequest<TDeleteResourceResponse>
    {
        private ISearchParameterOperations _searchParameterOperations;
        private IFhirDataStore _fhirDataStore;

        public DeleteSearchParameterBehavior(ISearchParameterOperations searchParameterOperations, IFhirDataStore fhirDataStore)
        {
            EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));

            _searchParameterOperations = searchParameterOperations;
            _fhirDataStore = fhirDataStore;
        }

        public async Task<TDeleteResourceResponse> Handle(TDeleteResourceRequest request, RequestHandlerDelegate<TDeleteResourceResponse> next, CancellationToken cancellationToken)
        {
            var deleteRequest = request as DeleteResourceRequest;
            ResourceWrapper searchParamResource = null;

            if (deleteRequest.ResourceKey.ResourceType.Equals(KnownResourceTypes.SearchParameter, StringComparison.Ordinal))
            {
                searchParamResource = await _fhirDataStore.GetAsync(deleteRequest.ResourceKey, cancellationToken);
            }

            var response = await next();

            if (searchParamResource != null && searchParamResource.IsDeleted == false)
            {
                // Once the SearchParameter resource is removed from the data store, we can update the in
                // memory SearchParameterDefinitionManager, and remove the status metadata from the data store
                await _searchParameterOperations.DeleteSearchParameterAsync(searchParamResource.RawResource, cancellationToken);
            }

            return response;
        }
    }
}
