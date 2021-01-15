// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public class CreateSearchParameterBehavior<TCreateResourceRequest, TUpsertResourceResponse> : IPipelineBehavior<TCreateResourceRequest, TUpsertResourceResponse>
        where TCreateResourceRequest : CreateResourceRequest
    {
        private ISearchParameterUtilities _searchParameterUtitliies;

        public CreateSearchParameterBehavior(ISearchParameterUtilities searchParameterUtilities)
        {
            EnsureArg.IsNotNull(searchParameterUtilities, nameof(searchParameterUtilities));

            _searchParameterUtitliies = searchParameterUtilities;
        }

        public async Task<TUpsertResourceResponse> Handle(TCreateResourceRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<TUpsertResourceResponse> next)
        {
            var response = await next();

            var createRequest = request as CreateResourceRequest;

            if (createRequest.Resource.InstanceType.Equals(KnownResourceTypes.SearchParameter, StringComparison.Ordinal))
            {
                // Once the SearchParameter resource is committed to the data store, we can update the in
                // memory SearchParameterDefinitionManager, and persist the status to the data store
                await _searchParameterUtitliies.AddSearchParameterAsync(createRequest.Resource.Instance);
            }

            return response;
        }
    }
}
