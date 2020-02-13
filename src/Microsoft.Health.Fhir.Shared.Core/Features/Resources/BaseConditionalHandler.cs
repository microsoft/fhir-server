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
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.Resources
{
    public abstract class BaseConditionalHandler : BaseResourceHandler
    {
        private readonly ISearchService _searchService;

        protected BaseConditionalHandler(
            IFhirDataStore fhirDataStore,
            ISearchService searchService,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ResourceIdProvider resourceIdProvider,
            IFhirAuthorizationService authorizationService)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            EnsureArg.IsNotNull(searchService, nameof(searchService));

            _searchService = searchService;
        }

        protected async Task<SearchResultEntry[]> Search(string instanceType, IReadOnlyList<Tuple<string, string>> conditionalParameters, CancellationToken cancellationToken)
        {
            // Filters search parameters that can limit the number of results (e.g. _count=1)
            IReadOnlyList<Tuple<string, string>> filteredParameters = conditionalParameters
                .Where(x => !string.Equals(x.Item1, KnownQueryParameterNames.Count, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(x.Item1, KnownQueryParameterNames.Summary, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            SearchResult results = await _searchService.SearchAsync(instanceType, filteredParameters, cancellationToken);

            // Check if all parameters passed in were unused, this would result in no search parameters being applied to search results
            int? totalUnusedParameters = results?.UnsupportedSearchParameters.Count + results?.UnsupportedSortingParameters.Count;
            if (totalUnusedParameters == filteredParameters.Count)
            {
                throw new PreconditionFailedException(Core.Resources.ConditionalOperationNotSelectiveEnough);
            }

            SearchResultEntry[] matchedResults = results?.Results.Where(x => x.SearchEntryMode == ValueSets.SearchEntryMode.Match).ToArray();

            return matchedResults;
        }

        public async Task ResolveReferencesAsync(Resource resource, Dictionary<string, (string resourceId, string resourceType)> referenceIdDictionary, string requestUrl, CancellationToken cancellationToken)
        {
            IEnumerable<ResourceReference> references = resource.GetAllChildren<ResourceReference>();

            foreach (ResourceReference reference in references)
            {
                if (string.IsNullOrWhiteSpace(reference.Reference))
                {
                    continue;
                }

                // Checks to see if this reference has already been assigned an Id
                if (referenceIdDictionary.TryGetValue(reference.Reference, out var referenceInformation))
                {
                    reference.Reference = $"{referenceInformation.resourceType}/{referenceInformation.resourceId}";
                }
                else
                {
                    if (reference.Reference.Contains("?", StringComparison.Ordinal))
                    {
                        string[] queries = reference.Reference.Split("?");
                        string resourceType = queries[0];
                        string conditionalQueries = queries[1];

                        if (!ModelInfoProvider.IsKnownResource(resourceType))
                        {
                            throw new RequestNotValidException(string.Format(Core.Resources.ReferenceResourceTypeNotSupported, resourceType, reference.Reference));
                        }

                        SearchResultEntry[] results = await GetExistingResourceId(requestUrl, resourceType, conditionalQueries, cancellationToken);

                        if (results == null || results.Length != 1)
                        {
                            throw new RequestNotValidException(string.Format(Core.Resources.InvalidConditionalReference, reference.Reference));
                        }

                        string resourceId = results[0].Resource.ResourceId;

                        referenceIdDictionary.Add(reference.Reference, (resourceId, resourceType));

                        reference.Reference = $"{resourceType}/{resourceId}";
                    }
                }
            }
        }

        protected async Task<SearchResultEntry[]> GetExistingResourceId(string requestUrl, string resourceType, StringValues conditionalQueries, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(resourceType) || string.IsNullOrEmpty(conditionalQueries))
            {
                throw new RequestNotValidException(string.Format(Core.Resources.InvalidConditionalReferenceParameters, requestUrl));
            }

            Tuple<string, string>[] conditionalParameters = QueryHelpers.ParseQuery(conditionalQueries)
                              .SelectMany(query => query.Value, (query, value) => Tuple.Create(query.Key, value)).ToArray();

            var searchResourceRequest = new SearchResourceRequest(resourceType, conditionalParameters);

            return await Search(searchResourceRequest.ResourceType, searchResourceRequest.Queries, cancellationToken);
        }
    }
}
