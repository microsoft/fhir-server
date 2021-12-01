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
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.Resources
{
    public class ResourceReferenceResolver
    {
        private readonly ISearchService _searchService;
        private readonly IQueryStringParser _queryStringParser;

        public ResourceReferenceResolver(ISearchService searchService, IQueryStringParser queryStringParser)
        {
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(queryStringParser, nameof(queryStringParser));

            _searchService = searchService;
            _queryStringParser = queryStringParser;
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

                        if (results == null || results.Count != 1)
                        {
                            throw new RequestNotValidException(string.Format(Core.Resources.InvalidConditionalReference, reference.Reference));
                        }

                        string resourceId = results.First().Resource.ResourceId;

                        referenceIdDictionary.Add(reference.Reference, (resourceId, resourceType));

                        reference.Reference = $"{resourceType}/{resourceId}";
                    }
                }
            }
        }

        public async Task<IReadOnlyCollection<SearchResultEntry>> GetExistingResourceId(string requestUrl, string resourceType, StringValues conditionalQueries, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(resourceType) || string.IsNullOrEmpty(conditionalQueries))
            {
                throw new RequestNotValidException(string.Format(Core.Resources.InvalidConditionalReferenceParameters, requestUrl));
            }

            IReadOnlyList<Tuple<string, string>> conditionalParameters = _queryStringParser.Parse(conditionalQueries).AsTuples();

            var searchResourceRequest = new SearchResourceRequest(resourceType, conditionalParameters);

            return await _searchService.ConditionalSearchAsync(searchResourceRequest.ResourceType, searchResourceRequest.Queries, cancellationToken);
        }
    }
}
