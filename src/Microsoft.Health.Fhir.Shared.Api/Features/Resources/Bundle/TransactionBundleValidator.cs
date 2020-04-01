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
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    public class TransactionBundleValidator
    {
        private readonly ResourceReferenceResolver _referenceResolver;

        public TransactionBundleValidator(ResourceReferenceResolver referenceResolver)
        {
            EnsureArg.IsNotNull(referenceResolver, nameof(referenceResolver));

            _referenceResolver = referenceResolver;
        }

        /// <summary>
        /// This method validates if transaction bundle contains multiple entries that are modifying the same resource.
        /// It also validates if the request operations within a entry is a valid operation.
        /// </summary>
        /// <param name="bundle"> The input bundle</param>
        /// <param name="cancellationToken"> The cancellation token</param>
        public async Task ValidateBundle(Hl7.Fhir.Model.Bundle bundle, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(bundle, nameof(bundle));

            var resourceIdList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in bundle.Entry)
            {
                if (ShouldValidateBundleEntry(entry))
                {
                    string resourceId = await GetResourceId(entry, cancellationToken);
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

        private async Task<string> GetResourceId(EntryComponent entry, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(entry.Request.IfNoneExist) && !entry.Request.Url.Contains("?", StringComparison.Ordinal))
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
                HTTPVerb requestMethod = (HTTPVerb)entry.Request.Method;
                bool conditionalCreate = requestMethod == HTTPVerb.POST;
                bool condtionalUpdate = requestMethod == HTTPVerb.PUT;

                if (condtionalUpdate)
                {
                    string[] conditinalUpdateParameters = entry.Request.Url.Split("?");
                    resourceType = conditinalUpdateParameters[0];
                    conditionalQueries = conditinalUpdateParameters[1];
                }
                else if (conditionalCreate)
                {
                    resourceType = entry.Request.Url;
                    conditionalQueries = entry.Request.IfNoneExist;
                }

                IReadOnlyCollection<SearchResultEntry> matchedResults = await _referenceResolver.GetExistingResourceId(entry.Request.Url, resourceType, conditionalQueries, cancellationToken);
                int? count = matchedResults?.Count;

                if (count > 1)
                {
                    // Multiple matches: The server returns a 412 Precondition Failed error indicating the client's criteria were not selective enough
                    throw new PreconditionFailedException(string.Format(Api.Resources.ConditionalOperationInBundleNotSelectiveEnough, conditionalQueries));
                }

                if (count == 1)
                {
                    return entry.Resource.TypeName + "/" + matchedResults.First().Resource.ResourceId;
                }
            }

            return string.Empty;
        }

        private static bool ShouldValidateBundleEntry(EntryComponent entry)
        {
            string requestUrl = entry.Request.Url;
            HTTPVerb? requestMethod = entry.Request.Method;

            // Search operations using _search and POST endpoint is not supported for bundle.
            // Conditional Delete operation is also not currently not supported.
            if ((requestMethod == HTTPVerb.POST && requestUrl.Contains("_search", StringComparison.OrdinalIgnoreCase))
                || (requestMethod == HTTPVerb.DELETE && requestUrl.Contains("?", StringComparison.Ordinal)))
            {
                throw new RequestNotValidException(string.Format(Api.Resources.InvalidBundleEntry, entry.Request.Url, requestMethod));
            }

            // Resource type bundle is not supported.within a bundle.
            if (entry.Resource?.ResourceType == Hl7.Fhir.Model.ResourceType.Bundle)
            {
                throw new RequestNotValidException(string.Format(Api.Resources.UnsupportedResourceType, KnownResourceTypes.Bundle));
            }

            // Check for duplicate resources within a bundle entry is skipped if the request within a entry is not modifying the resource.
            return !(requestMethod == HTTPVerb.GET
                    || requestUrl.Contains("$", StringComparison.InvariantCulture));
        }
    }
}
