// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json.Linq;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    public class TransactionBundleValidator
    {
        private readonly ResourceReferenceResolver _referenceResolver;
        private readonly ILogger<TransactionBundleValidator> _logger;

        public TransactionBundleValidator(ResourceReferenceResolver referenceResolver, ILogger<TransactionBundleValidator> logger)
        {
            EnsureArg.IsNotNull(referenceResolver, nameof(referenceResolver));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _referenceResolver = referenceResolver;
            _logger = logger;
        }

        /// <summary>
        /// This method validates if transaction bundle contains multiple entries that are modifying the same resource.
        /// It also validates if the request operations within a entry is a valid operation.
        /// If a conditional create or update is executed and a resource exists, the value is populated in the idDictionary.
        /// </summary>
        /// <param name="bundle"> The input bundle</param>
        /// <param name="idDictionary">The id dictionary that stores fullUrl to actual ids.</param>
        /// <param name="cancellationToken"> The cancellation token</param>
        public async Task ValidateBundle(Hl7.Fhir.Model.Bundle bundle, IDictionary<string, (string resourceId, string resourceType)> idDictionary, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(bundle, nameof(bundle));

            var resourceIdList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in bundle.Entry)
            {
                if (ShouldValidateBundleEntry(entry))
                {
                    string resourceId = await GetResourceId(entry, idDictionary, cancellationToken);
                    string conditionalCreateQuery = entry.Request.IfNoneExist;

                    if (!string.IsNullOrEmpty(resourceId))
                    {
                        // Throw exception if resourceId is already present in the hashset.
                        if (resourceIdList.Contains(resourceId))
                        {
                            string requestUrl = BuildRequestUrlForConditionalQueries(entry, conditionalCreateQuery);
                            throw new RequestNotValidException(string.Format(Api.Resources.ResourcesMustBeUnique, requestUrl));
                        }

                        resourceIdList.Add(resourceId);
                    }
                }
            }
        }

        private static string BuildRequestUrlForConditionalQueries(EntryComponent entry, string conditionalCreateQuery)
        {
            return string.IsNullOrWhiteSpace(conditionalCreateQuery) ? entry.Request.Url : entry.Request.Url + "?" + conditionalCreateQuery;
        }

        private async Task<string> GetResourceId(EntryComponent entry, IDictionary<string, (string resourceId, string resourceType)> idDictionary, CancellationToken cancellationToken)
        {
            // If there is no search or conditional operations, then use the FullUrl for posts and the request url otherwise
            if (string.IsNullOrWhiteSpace(entry.Request.IfNoneExist) && !entry.Request.Url.Contains('?', StringComparison.Ordinal))
            {
                return entry.Request.Method == HTTPVerb.POST ? entry.FullUrl : entry.Request.Url;
            }

            string resourceType = null;
            StringValues conditionalQueries = string.Empty;

            if (entry.Request.Method == HTTPVerb.PUT)
            {
                string[] conditionalUpdate = entry.Request.Url.Split('?');
                resourceType = conditionalUpdate[0];
                conditionalQueries = conditionalUpdate[1];
            }
            else if (entry.Request.Method == HTTPVerb.POST)
            {
                resourceType = entry.Request.Url;
                conditionalQueries = entry.Request.IfNoneExist;
            }

            IReadOnlyCollection<SearchResultEntry> matchedResults = await _referenceResolver.GetExistingResourceId(entry.Request.Url, resourceType, conditionalQueries, cancellationToken);

            JObject serializableEntity = JObject.FromObject(new
            {
                requestUrl = entry.Request.Url,
                resourceType,
                conditionalQueries,
                matchedResults = matchedResults?.Count,
                idDictionary = idDictionary.Count,
            });

            _logger.LogInformation(serializableEntity.ToString());

            if (matchedResults?.Count > 1)
            {
                // Multiple matches: The server returns a 412 Precondition Failed error indicating the client's criteria were not selective enought
                throw new PreconditionFailedException(string.Format(Api.Resources.ConditionalOperationInBundleNotSelectiveEnough, conditionalQueries));
            }
            else if (matchedResults?.Count == 1)
            {
                // If this entry has a fullUrl, then save it to the idDictionary for matching later
                if (!string.IsNullOrWhiteSpace(entry.FullUrl))
                {
                    idDictionary.Add(entry.FullUrl, (matchedResults.First().Resource.ResourceId, entry.Resource.TypeName));
                }

                return entry.Resource.TypeName + "/" + matchedResults.First().Resource.ResourceId;
            }

            return string.Empty;
        }

        private static bool ShouldValidateBundleEntry(EntryComponent entry)
        {
            string requestUrl = entry.Request?.Url;
            HTTPVerb? requestMethod = entry.Request?.Method;

            if (string.IsNullOrWhiteSpace(requestUrl))
            {
                throw new RequestNotValidException(string.Format(Api.Resources.InvalidBundleEntryRequestUrl));
            }

            // Search operations using _search and POST endpoint is not supported for bundle.
            // Conditional Delete operation is also not currently not supported.
            if ((requestMethod == HTTPVerb.POST && requestUrl.Contains(KnownRoutes.Search, StringComparison.OrdinalIgnoreCase))
                || (requestMethod == HTTPVerb.DELETE && requestUrl.Contains('?', StringComparison.Ordinal)))
            {
                throw new RequestNotValidException(string.Format(Api.Resources.InvalidBundleEntry, entry.Request.Url, requestMethod));
            }

            // Resource type bundle is not supported.within a bundle.
            if (entry.Resource?.TypeName == KnownResourceTypes.Bundle)
            {
                throw new RequestNotValidException(string.Format(Api.Resources.UnsupportedResourceType, KnownResourceTypes.Bundle));
            }

            // Check for duplicate resources within a bundle entry is skipped if the request within a entry is not modifying the resource.
            return !(requestMethod == HTTPVerb.GET
                    || requestUrl.Contains('$', StringComparison.InvariantCulture));
        }
    }
}
