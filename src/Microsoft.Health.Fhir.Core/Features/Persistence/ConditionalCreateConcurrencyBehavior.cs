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
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    /// <summary>
    /// Intercepts ConditionalCreateResourceRequests and checks to ensure no duplicate items were created
    /// </summary>
    public class ConditionalCreateConcurrencyBehavior
        : IPipelineBehavior<ConditionalCreateResourceRequest, UpsertResourceResponse>, IPipelineBehavior<ConditionalUpsertResourceRequest, UpsertResourceResponse>
    {
        private readonly ISearchService _searchService;
        private readonly IFhirDataStore _dataStore;
        private readonly ILogger<ConditionalCreateConcurrencyBehavior> _logger;

        public ConditionalCreateConcurrencyBehavior(ISearchService searchService, IFhirDataStore dataStore, ILogger<ConditionalCreateConcurrencyBehavior> logger)
        {
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(dataStore, nameof(dataStore));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _searchService = searchService;
            _dataStore = dataStore;
            _logger = logger;
        }

        public async Task<UpsertResourceResponse> Handle(ConditionalCreateResourceRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<UpsertResourceResponse> next)
        {
            EnsureArg.IsNotNull(request, nameof(request));
            EnsureArg.IsNotNull(next, nameof(next));

            return await Execute(request.Resource.InstanceType, request.ConditionalParameters, next);
        }

        public async Task<UpsertResourceResponse> Handle(ConditionalUpsertResourceRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<UpsertResourceResponse> next)
        {
            EnsureArg.IsNotNull(request, nameof(request));
            EnsureArg.IsNotNull(next, nameof(next));

            return await Execute(request.Resource.InstanceType, request.ConditionalParameters, next);
        }

        private async Task<UpsertResourceResponse> Execute(string resourceType, IReadOnlyList<Tuple<string, string>> conditions, RequestHandlerDelegate<UpsertResourceResponse> next)
        {
            EnsureArg.IsNotNullOrEmpty(resourceType, nameof(resourceType));
            EnsureArg.IsNotNull(conditions, nameof(conditions));
            EnsureArg.IsNotNull(next, nameof(next));

            UpsertResourceResponse response = await next();

            // Check for duplicates
            if (response?.Outcome.Outcome == SaveOutcomeType.Created)
            {
                IReadOnlyCollection<SearchResultEntry> matchedResults = await _searchService.ConditionalSearchAsync(resourceType, conditions, default);

                SearchResultEntry[] orderedResults = matchedResults
                    .OrderBy(x => x.Resource.ResourceId)
                    .ToArray();

                if (orderedResults.Length > 1 && !string.Equals(response.Outcome.RawResourceElement.Id, orderedResults.First().Resource.ResourceId, StringComparison.Ordinal))
                {
                    _logger.LogWarning("{ItemsFound} items found after creating with conditional Create/Update, reverting", orderedResults.Length);

                    // remove current item and return conflict.
                    await _dataStore.HardDeleteAsync(new ResourceKey(resourceType, response.Outcome.RawResourceElement.Id), default);

                    throw new ResourceConflictException(Core.Resources.ResourceConflict);
                }
            }

            return response;
        }
    }
}
