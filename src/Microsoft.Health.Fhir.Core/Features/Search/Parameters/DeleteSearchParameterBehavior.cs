// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Medino;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Validation;
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
        private readonly ISearchParameterStatusManager _searchParameterStatusManager;
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly IModelInfoProvider _modelInfoProvider;

        public DeleteSearchParameterBehavior(
            ISearchParameterOperations searchParameterOperations,
            IFhirDataStore fhirDataStore,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ISearchParameterStatusManager searchParameterStatusManager,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _searchParameterOperations = searchParameterOperations;
            _fhirDataStore = fhirDataStore;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _searchParameterStatusManager = searchParameterStatusManager;
            _requestContextAccessor = requestContextAccessor;
            _modelInfoProvider = modelInfoProvider;
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

                // If the search parameter exists and is not already deleted, update status to pending delete
                if (!searchParamResource.IsDeleted)
                {
                    var typed = _modelInfoProvider.ToTypedElement(searchParamResource.RawResource);
                    await QueuePendingDeleteStatusAsync(typed.GetStringScalar("url"), deleteRequest.DeleteOperation, cancellationToken);
                }

                return await next();
            }

            return await next();
        }

        private async Task QueuePendingDeleteStatusAsync(string url, DeleteOperation deleteOperation, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            var context = _requestContextAccessor.RequestContext;
            if (context == null)
            {
                return;
            }

            var lastUpdated = _requestContextAccessor.RequestContext.GetSearchParameterLastUpdated();
            if (!lastUpdated.HasValue)
            {
                await _searchParameterOperations.GetAndApplySearchParameterUpdates(cancellationToken);
                lastUpdated = _searchParameterOperations.SearchParamLastUpdated;
            }

            _searchParameterDefinitionManager.TryGetSearchParameter(url, out var existing);

            var status = deleteOperation == DeleteOperation.HardDelete ? SearchParameterStatus.PendingHardDelete : SearchParameterStatus.PendingDelete;

            var update = new ResourceSearchParameterStatus
            {
                Uri = new Uri(url),
                Status = status,
                LastUpdated = lastUpdated.Value,
                IsPartiallySupported = existing?.IsPartiallySupported ?? false,
                SortStatus = existing?.SortStatus ?? SortParameterStatus.Disabled,
            };

            context.Properties[SearchParameterRequestContextPropertyNames.PendingStatus] = update;
        }
    }
}
