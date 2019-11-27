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
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Search;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    public class TransactionValidator
    {
        private readonly ISearchService _searchService;

        public TransactionValidator(ISearchService searchService)
        {
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            _searchService = searchService;
        }

        // Validates if transaction bundle contains multiple entries that are modifying the same resource.
        public async Task ValidateBundle(Hl7.Fhir.Model.Bundle bundle)
        {
            if (bundle.Type != BundleType.Transaction)
            {
                return;
            }

            var resourceIdList = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in bundle.Entry)
            {
                if (ShouldValidateBundleEntry(entry))
                {
                    string resourceId = await GetResourceId(entry);

                    if (!string.IsNullOrEmpty(resourceId))
                    {
                        // Throw exception if resourceId is already present in the hashset.
                        if (resourceIdList.Contains(resourceId))
                        {
                            throw new RequestNotValidException(string.Format(Api.Resources.ResourcesMustBeUnique, resourceId));
                        }

                        resourceIdList.Add(resourceId);
                    }
                }
            }
        }

        private async Task<string> GetResourceId(EntryComponent entry)
        {
            if (entry.Request.IfNoneExist == null && !entry.Request.Url.Contains("?", StringComparison.InvariantCulture))
            {
                if (entry.Request.Method == HTTPVerb.POST)
                {
                    return entry.FullUrl;
                }

                return entry.Request.Url;
            }
            else
            {
                string resourceType = null;
                StringValues conditionalQueries;

                if (entry.Request.Method == HTTPVerb.PUT || entry.Request.Method == HTTPVerb.DELETE)
                {
                    string[] conditinalUpdateParameters = entry.Request.Url.Split("?");
                    resourceType = conditinalUpdateParameters[0];
                    conditionalQueries = conditinalUpdateParameters[1];
                }
                else if (entry.Request.Method == HTTPVerb.POST)
                {
                    resourceType = entry.Request.Url;
                    conditionalQueries = entry.Request.IfNoneExist;
                }

                SearchResult results = await GetExistingResourceId(entry.Request.Url, resourceType, conditionalQueries);

                if (results?.Results.Count() > 0)
                {
                    return entry.Resource.TypeName + "/" + results.Results.First().Resource.ResourceId;
                }
            }

            return string.Empty;
        }

        public async Task<SearchResult> GetExistingResourceId(string requestUrl, string resourceType, StringValues conditionalQueries)
        {
            if (string.IsNullOrEmpty(resourceType) || string.IsNullOrEmpty(conditionalQueries))
            {
                throw new RequestNotValidException(string.Format(Api.Resources.InvalidConditionalReferenceParameters, requestUrl));
            }

            Tuple<string, string>[] conditionalParameters = QueryHelpers.ParseQuery(conditionalQueries)
                              .SelectMany(query => query.Value, (query, value) => Tuple.Create(query.Key, value)).ToArray();

            var searchResourceRequest = new SearchResourceRequest(resourceType, conditionalParameters);

            return await _searchService.SearchAsync(searchResourceRequest.ResourceType, searchResourceRequest.Queries, CancellationToken.None);
        }

        private static bool ShouldValidateBundleEntry(EntryComponent entry)
        {
            string requestUrl = entry.Request.Url;
            HTTPVerb? requestMethod = entry.Request.Method;

            // Search operations using _search and POST endpoint is not supported for bundle.
            if (requestMethod == HTTPVerb.POST && requestUrl.Contains("_search", StringComparison.InvariantCulture))
            {
                throw new RequestNotValidException(string.Format(Api.Resources.InvalidBundleEntry, entry.Request.Url));
            }

            // Check for duplicate resources within a bundle entry is skipped if the entry is bundle or if the request within a entry is not modifying the resource.
            return !(entry.Resource?.ResourceType == Hl7.Fhir.Model.ResourceType.Bundle
                 || requestMethod == HTTPVerb.GET
                 || requestUrl.Contains("$", StringComparison.InvariantCulture));
        }
    }
}
