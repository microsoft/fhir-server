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
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;

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
            ResourceIdProvider resourceIdProvider)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider)
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
    }
}
