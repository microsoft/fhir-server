// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public class DeleteSearchParameterBehavior<TDeleteResourceRequest, TDeleteResourceResponse> : IPipelineBehavior<TDeleteResourceRequest, TDeleteResourceResponse>
        where TDeleteResourceRequest : DeleteResourceRequest, IRequest<TDeleteResourceResponse>
    {
        private ISearchParameterOperations _searchParameterOperations;
        private IFhirDataStore _fhirDataStore;
        private ISearchParameterDefinitionManager _searchParameterDefinitionManager;

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

        public async Task<TDeleteResourceResponse> HandleAsync(TDeleteResourceRequest request, RequestHandlerDelegate<TDeleteResourceResponse> next, CancellationToken cancellationToken)
        {
            var deleteRequest = request as DeleteResourceRequest;
            ResourceWrapper searchParamResource = null;

            if (deleteRequest.ResourceKey.ResourceType.Equals(KnownResourceTypes.SearchParameter, StringComparison.Ordinal))
            {
                // First check if this is a system-defined parameter by checking all parameters in the definition manager
                var allSearchParameters = _searchParameterDefinitionManager.AllSearchParameters;
                var systemDefinedParam = allSearchParameters.FirstOrDefault(sp =>
                    sp.IsSystemDefined &&
                    sp.Url?.Segments.LastOrDefault()?.TrimEnd('/') == deleteRequest.ResourceKey.Id);

                if (systemDefinedParam != null)
                {
                    throw new MethodNotAllowedException(string.Format(Core.Resources.SearchParameterDefinitionSystemDefined, systemDefinedParam.Url));
                }

                // Now try to get the custom search parameter from the data store
                searchParamResource = await _fhirDataStore.GetAsync(deleteRequest.ResourceKey, cancellationToken);

                if (searchParamResource == null)
                {
                    throw new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundById, deleteRequest.ResourceKey.ResourceType, deleteRequest.ResourceKey.Id));
                }

                // If the search parameter exists and is not already deleted, delete it
                if (!searchParamResource.IsDeleted)
                {
                    // First update the in-memory SearchParameterDefinitionManager, and remove the status metadata from the data store
                    // then remove the SearchParameter resource from the data store
                    await _searchParameterOperations.DeleteSearchParameterAsync(searchParamResource.RawResource, cancellationToken);
                }
            }

            return await next();
        }
    }
}
