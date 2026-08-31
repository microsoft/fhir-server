// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Medino;
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
        private readonly IFhirDataStore _fhirDataStore;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;

        public DeleteSearchParameterBehavior(
            IFhirDataStore fhirDataStore,
            ISearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));

            _fhirDataStore = fhirDataStore;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
        }

        public async Task<TDeleteResourceResponse> HandleAsync(TDeleteResourceRequest request, RequestHandlerDelegate<TDeleteResourceResponse> next, CancellationToken cancellationToken)
        {
            var deleteRequest = request as DeleteResourceRequest;

            if (deleteRequest.ResourceKey.ResourceType == KnownResourceTypes.SearchParameter)
            {
                // First check if this is a system-defined parameter by checking all parameters in the definition manager
                var current = _searchParameterDefinitionManager.AllSearchParameters.FirstOrDefault(_ => _.Url?.Segments.LastOrDefault()?.TrimEnd('/') == deleteRequest.ResourceKey.Id);
                if (current != null && current.IsSystemDefined)
                {
                    throw new MethodNotAllowedException(string.Format(Core.Resources.SearchParameterDefinitionSystemDefined, current.Url));
                }

                // Now try to get the custom search parameter from the data store
                var searchParamResource = await _fhirDataStore.GetAsync(deleteRequest.ResourceKey, cancellationToken);
                if (searchParamResource == null)
                {
                    throw new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundById, deleteRequest.ResourceKey.ResourceType, deleteRequest.ResourceKey.Id));
                }
            }

            return await next();
        }
    }
}
