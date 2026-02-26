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
        private readonly ISearchParameterStatusManager _searchParameterStatusManager;
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly IModelInfoProvider _modelInfoProvider;

        public CreateOrUpdateSearchParameterBehavior(
            ISearchParameterOperations searchParameterOperations,
            IFhirDataStore fhirDataStore,
            ISearchParameterStatusManager searchParameterStatusManager,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _searchParameterOperations = searchParameterOperations;
            _fhirDataStore = fhirDataStore;
            _searchParameterStatusManager = searchParameterStatusManager;
            _requestContextAccessor = requestContextAccessor;
            _modelInfoProvider = modelInfoProvider;
        }

        public async Task<UpsertResourceResponse> Handle(CreateResourceRequest request, RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
        {
            if (request.Resource.InstanceType.Equals(KnownResourceTypes.SearchParameter, StringComparison.Ordinal))
            {
                // Before committing the SearchParameter resource to the data store, add it to the SearchParameterDefinitionManager
                // and parse the fhirPath, as well as validate the parameter type
                await _searchParameterOperations.AddSearchParameterAsync(request.Resource.Instance, cancellationToken);

                var url = request.Resource.Instance.GetStringScalar("url");
                await QueueStatusAsync(url, SearchParameterStatus.Supported, cancellationToken);
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

                if (prevSearchParamResource != null && prevSearchParamResource.IsDeleted == false)
                {
                    // Update the SearchParameterDefinitionManager with the new SearchParameter in order to validate any changes
                    // to the fhirpath or the datatype
                    await _searchParameterOperations.UpdateSearchParameterAsync(request.Resource.Instance, prevSearchParamResource.RawResource, cancellationToken);

                    var previousUrl = _modelInfoProvider.ToTypedElement(prevSearchParamResource.RawResource).GetStringScalar("url");
                    var newUrl = request.Resource.Instance.GetStringScalar("url");

                    if (!string.IsNullOrWhiteSpace(previousUrl) && !previousUrl.Equals(newUrl, StringComparison.Ordinal))
                    {
                        await QueueStatusAsync(previousUrl, SearchParameterStatus.Deleted, cancellationToken);
                    }

                    await QueueStatusAsync(newUrl, SearchParameterStatus.Supported, cancellationToken);
                }
                else
                {
                    // No previous version exists or it was deleted, so add it as a new SearchParameter
                    await _searchParameterOperations.AddSearchParameterAsync(request.Resource.Instance, cancellationToken);

                    var url = request.Resource.Instance.GetStringScalar("url");
                    await QueueStatusAsync(url, SearchParameterStatus.Supported, cancellationToken);
                }
            }

            // Now allow the resource to updated per the normal behavior
            return await next(cancellationToken);
        }

        private async Task QueueStatusAsync(string url, SearchParameterStatus status, CancellationToken cancellationToken)
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

            var currentStatuses = await _searchParameterStatusManager.GetAllSearchParameterStatus(cancellationToken);
            var existing = currentStatuses.FirstOrDefault(s => string.Equals(s.Uri?.OriginalString, url, StringComparison.Ordinal));

            var update = new ResourceSearchParameterStatus
            {
                Uri = new Uri(url),
                Status = status,
                LastUpdated = existing?.LastUpdated ?? DateTimeOffset.UtcNow,
                IsPartiallySupported = existing?.IsPartiallySupported ?? false,
                SortStatus = existing?.SortStatus ?? SortParameterStatus.Disabled,
            };

            lock (pendingStatuses)
            {
                pendingStatuses.RemoveAll(s => string.Equals(s.Uri?.OriginalString, url, StringComparison.Ordinal));
                pendingStatuses.Add(update);
            }
        }
    }
}
