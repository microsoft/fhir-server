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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources
{
    public class ResourceReferenceResolver
    {
        private readonly ISearchService _searchService;
        private readonly IQueryStringParser _queryStringParser;
        private readonly ILogger<ResourceReferenceResolver> _logger;

        public ResourceReferenceResolver(
            ISearchService searchService,
            IQueryStringParser queryStringParser,
            ILogger<ResourceReferenceResolver> logger)
        {
            _searchService = EnsureArg.IsNotNull(searchService, nameof(searchService));
            _queryStringParser = EnsureArg.IsNotNull(queryStringParser, nameof(queryStringParser));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task<int> ResolveReferencesAsync(Resource resource, IDictionary<string, (string resourceId, string resourceType)> referenceIdDictionary, string requestUrl, CancellationToken cancellationToken)
        {
            IEnumerable<ResourceReference> references = resource.GetAllChildren<ResourceReference>();

            int totalResolvedReferences = 0;
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
                    totalResolvedReferences++;
                }
                else
                {
                    if (reference.Reference.Contains('?', StringComparison.Ordinal))
                    {
                        string[] queries = reference.Reference.Split("?");
                        string resourceType = queries[0];
                        string conditionalQueries = queries[1];

                        if (!ModelInfoProvider.IsKnownResource(resourceType))
                        {
                            throw new RequestNotValidException(string.Format(Core.Resources.ReferenceResourceTypeNotSupported, resourceType, reference.Reference));
                        }

                        var results = await GetExistingResourceId(requestUrl, resourceType, conditionalQueries, cancellationToken);

                        if (results == null || !results.Where(result => result.SearchEntryMode == ValueSets.SearchEntryMode.Match).Any())
                        {
                            throw new RequestNotValidException(string.Format(Core.Resources.InvalidConditionalReference, reference.Reference));
                        }
                        else if (results.Where(result => result.SearchEntryMode == ValueSets.SearchEntryMode.Match).Count() > 1)
                        {
                            throw new RequestNotValidException(string.Format(Core.Resources.InvalidConditionalReferenceToMultipleResources, reference.Reference));
                        }

                        string resourceId = results.First(result => result.SearchEntryMode == ValueSets.SearchEntryMode.Match).Resource.ResourceId;

                        referenceIdDictionary.Add(reference.Reference, (resourceId, resourceType));

                        reference.Reference = $"{resourceType}/{resourceId}";
                        totalResolvedReferences++;
                    }
                }
            }

            return totalResolvedReferences;
        }

        public async Task<IReadOnlyCollection<SearchResultEntry>> GetExistingResourceId(string requestUrl, string resourceType, StringValues conditionalQueries, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(resourceType) || string.IsNullOrEmpty(conditionalQueries))
            {
                throw new RequestNotValidException(string.Format(Core.Resources.InvalidConditionalReferenceParameters, requestUrl));
            }

            IReadOnlyList<Tuple<string, string>> conditionalParameters = _queryStringParser.Parse(conditionalQueries).AsTuples();

            var searchResourceRequest = new SearchResourceRequest(resourceType, conditionalParameters);

            var matches = (await _searchService.ConditionalSearchAsync(searchResourceRequest.ResourceType, searchResourceRequest.Queries, cancellationToken, logger: _logger))
                .Results
                .Where(result => result.SearchEntryMode == ValueSets.SearchEntryMode.Match)
                .ToList();

            return matches;
        }
    }
}
