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
using MediatR;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public class CreateOrUpdateSearchParameterBehavior<TCreateResourceRequest, TUpsertResourceResponse> : IPipelineBehavior<CreateResourceRequest, UpsertResourceResponse>,
        IPipelineBehavior<UpsertResourceRequest, UpsertResourceResponse>
    {
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly IFhirDataStore _fhirDataStore;
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;

        public CreateOrUpdateSearchParameterBehavior(
            ISearchParameterOperations searchParameterOperations,
            IFhirDataStore fhirDataStore,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _searchParameterOperations = searchParameterOperations;
            _fhirDataStore = fhirDataStore;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _requestContextAccessor = requestContextAccessor;
            _modelInfoProvider = modelInfoProvider;
        }

        public async Task<UpsertResourceResponse> Handle(CreateResourceRequest request, RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
        {
            if (request.Resource.InstanceType.Equals(KnownResourceTypes.SearchParameter, StringComparison.Ordinal))
            {
                var refreshCache = request.BundleResourceContext == null || !request.BundleResourceContext.IsParallelBundle;

                // Before committing the SearchParameter resource to the data store, validate the parameter type
                await _searchParameterOperations.ValidateSearchParameterAsync(request.Resource.Instance, cancellationToken, refreshCache);

                QueueStatusAsync(request.Resource.Instance.GetStringScalar("url"), SearchParameterStatus.Supported, cancellationToken);
            }

            // Allow the resource to be updated with the normal handler
            return await next(cancellationToken);
        }

        public async Task<UpsertResourceResponse> Handle(UpsertResourceRequest request, RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
        {
            // if the resource type being updated is a SearchParameter, then we want to query the previous version before it is changed
            // because we will need to the Url property to update the definition in the SearchParameterDefinitionManager
            // and the user could be changing the Url as part of this update
            if (request.Resource.InstanceType.Equals(KnownResourceTypes.SearchParameter, StringComparison.Ordinal))
            {
                var resourceKey = new ResourceKey(request.Resource.InstanceType, request.Resource.Id, request.Resource.VersionId);
                ResourceWrapper prevSearchParamResource = null;

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

                var refreshCache = request.BundleResourceContext == null || !request.BundleResourceContext.IsParallelBundle;

                if (prevSearchParamResource != null && prevSearchParamResource.IsDeleted == false)
                {
                    // Validate any changes to the fhirpath or the datatype
                    await _searchParameterOperations.ValidateSearchParameterAsync(request.Resource.Instance, cancellationToken, refreshCache);

                    var previousUrl = _modelInfoProvider.ToTypedElement(prevSearchParamResource.RawResource).GetStringScalar("url");
                    var newUrl = request.Resource.Instance.GetStringScalar("url");

                    if (!string.IsNullOrWhiteSpace(previousUrl) && !previousUrl.Equals(newUrl, StringComparison.Ordinal))
                    {
                        QueueStatusAsync(previousUrl, SearchParameterStatus.Deleted, cancellationToken);
                    }

                    QueueStatusAsync(newUrl, SearchParameterStatus.Supported, cancellationToken);
                }
                else
                {
                    // No previous version exists or it was deleted, so add it as a new SearchParameter
                    await _searchParameterOperations.ValidateSearchParameterAsync(request.Resource.Instance, cancellationToken, refreshCache);

                    QueueStatusAsync(request.Resource.Instance.GetStringScalar("url"), SearchParameterStatus.Supported, cancellationToken);
                }
            }

            // Now allow the resource to updated per the normal behavior
            return await next(cancellationToken);
        }

        private void QueueStatusAsync(string url, SearchParameterStatus status, CancellationToken cancellationToken)
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

            if (!context.Properties.TryGetValue(SearchParameterRequestContextPropertyNames.PendingStatusUpdates, out var value) ||
                value is not List<ResourceSearchParameterStatus> pendingStatuses)
            {
                pendingStatuses = new List<ResourceSearchParameterStatus>();
                context.Properties[SearchParameterRequestContextPropertyNames.PendingStatusUpdates] = pendingStatuses;
            }

            _searchParameterDefinitionManager.TryGetSearchParameter(url, out var current);

            var update = new ResourceSearchParameterStatus
            {
                Uri = new Uri(url),
                Status = status,
                LastUpdated = _searchParameterOperations.SearchParamLastUpdated.Value,
                IsPartiallySupported = current?.IsPartiallySupported ?? false,
                SortStatus = current?.SortStatus ?? SortParameterStatus.Disabled,
            };

            lock (pendingStatuses)
            {
                pendingStatuses.RemoveAll(s => string.Equals(s.Uri?.OriginalString, url, StringComparison.Ordinal));
                pendingStatuses.Add(update);
            }
        }
    }
}
