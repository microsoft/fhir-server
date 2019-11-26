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
        public async Task ValidateTransactionBundle(Hl7.Fhir.Model.Bundle bundle)
        {
            var resourceIdList = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in bundle.Entry)
            {
                if (ShouldValidateBundleEntry(entry))
                {
                    string resourceId = await GetResourceId(entry);

                    CheckIfMultipleResourceExists(resourceIdList, resourceId);
                }
            }
        }

        public static void CheckIfMultipleResourceExists(HashSet<string> resourceIdList, string resourceId)
        {
            if (!string.IsNullOrEmpty(resourceId))
            {
                if (resourceIdList.Contains(resourceId))
                {
                    throw new RequestNotValidException(string.Format(Api.Resources.ResourcesMustBeUnique, resourceId));
                }

                resourceIdList.Add(resourceId);
            }
        }

        private async Task<string> GetResourceId(EntryComponent entry)
        {
            string resourceId = null;

            if (entry.Request.IfNoneExist == null && !entry.Request.Url.Contains("?", StringComparison.InvariantCulture))
            {
                if (entry.Request.Method != HTTPVerb.POST)
                {
                    resourceId = entry.Request.Url;
                }
            }
            else
            {
                SearchResourceRequest searchResource = null;

                if (entry.Request.Method == HTTPVerb.PUT || entry.Request.Method == HTTPVerb.DELETE)
                {
                    string[] queries = entry.Request.Url.Split("?");
                    searchResource = new SearchResourceRequest(entry.Resource.TypeName, GetQueriesForSearch(queries[1]));
                }
                else if (entry.Request.Method == HTTPVerb.POST)
                {
                    searchResource = new SearchResourceRequest(entry.Resource.TypeName, GetQueriesForSearch(entry.Request.IfNoneExist));
                }

                SearchResult results = await _searchService.SearchAsync(searchResource.ResourceType, searchResource.Queries, CancellationToken.None);

                if (results?.Results.Count() > 0)
                {
                    resourceId = entry.Resource.TypeName + "/" + results.Results.First().Resource.ResourceId;
                }
            }

            return resourceId;
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

        private static List<Tuple<string, string>> GetQueriesForSearch(string queryParameter)
        {
            var queries = new List<Tuple<string, string>>();

            string[] searchParameters = queryParameter.Split("&&");

            foreach (string searchParameter in searchParameters)
            {
                string[] parameters = searchParameter.Split("=");
                queries.Add(Tuple.Create(parameters[0], parameters[1]));
            }

            return queries;
        }
    }
}
