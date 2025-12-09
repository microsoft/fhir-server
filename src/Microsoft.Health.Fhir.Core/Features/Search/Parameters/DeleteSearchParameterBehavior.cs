// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public class DeleteSearchParameterBehavior<TDeleteResourceRequest, TDeleteResourceResponse> : IPipelineBehavior<TDeleteResourceRequest, TDeleteResourceResponse>
        where TDeleteResourceRequest : DeleteResourceRequest, IRequest<TDeleteResourceResponse>
    {
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly IFhirDataStore _fhirDataStore;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;

        public DeleteSearchParameterBehavior(
            ISearchParameterOperations searchParameterOperations,
            IFhirDataStore fhirDataStore,
            ISearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));

            _searchParameterOperations = searchParameterOperations;
            _fhirDataStore = fhirDataStore;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
        }

        public async Task<TDeleteResourceResponse> Handle(TDeleteResourceRequest request, RequestHandlerDelegate<TDeleteResourceResponse> next, CancellationToken cancellationToken)
        {
            var deleteRequest = request as DeleteResourceRequest;
            ResourceWrapper customSearchParamResource = null;

            if (deleteRequest.ResourceKey.ResourceType.Equals(KnownResourceTypes.SearchParameter, StringComparison.Ordinal))
            {
                customSearchParamResource = await _fhirDataStore.GetAsync(deleteRequest.ResourceKey, cancellationToken);

                // Check if this is a spec-defined SearchParameter (exists in definition manager but not in Resource table)
                if (customSearchParamResource == null)
                {
                    // Try to find by URL constructed from the resource ID
                    // Spec-defined parameters typically have URLs like: http://hl7.org/fhir/SearchParameter/{id}
                    var possibleUrl = $"http://hl7.org/fhir/SearchParameter/{deleteRequest.ResourceKey.Id}";
                    if (_searchParameterDefinitionManager.TryGetSearchParameter(possibleUrl, out var searchParameterInfo))
                    {
                        // Found in definition manager but not in Resource table - this is a spec-defined parameter
                        throw new MethodNotAllowedException(string.Format(Core.Resources.SearchParameterDefinitionCannotDeleteSpecDefined, possibleUrl));
                    }

                    // Also check all search parameters to see if any match this ID
                    var matchingParam = _searchParameterDefinitionManager.AllSearchParameters
                        .FirstOrDefault(sp => sp.Url.ToString().EndsWith($"/{deleteRequest.ResourceKey.Id}", StringComparison.OrdinalIgnoreCase));

                    if (matchingParam != null)
                    {
                        throw new MethodNotAllowedException(string.Format(Core.Resources.SearchParameterDefinitionCannotDeleteSpecDefined, matchingParam.Url));
                    }

                    // Not found in Resource table or definition manager - doesn't exist
                    throw new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundById, deleteRequest.ResourceKey.ResourceType, deleteRequest.ResourceKey.Id));
                }
            }

            if (customSearchParamResource != null && customSearchParamResource.IsDeleted == false)
            {
                // This is a custom search parameter
                await _searchParameterOperations.DeleteSearchParameterAsync(customSearchParamResource.RawResource, cancellationToken);
            }

            return await next(cancellationToken);
        }
    }
}
