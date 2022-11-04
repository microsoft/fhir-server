// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Filters;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Orchestrates filtering criteria and apply filters and checks on top of data resources.
    /// </summary>
    public sealed class DataResourceFilter : IDataResourceFilter
    {
        private readonly IReadOnlyList<IFilterCriteria> _filterCriterias;

        public DataResourceFilter(MissingDataFilterCriteria missingDataFilterCriteria)
        {
            EnsureArg.IsNotNull(missingDataFilterCriteria);

            // Set of filter criteria to be applied on top of a SearchResult.
            _filterCriterias = new IFilterCriteria[]
            {
                missingDataFilterCriteria,
            };
        }

        public SearchResult Filter(SearchResult searchResult)
        {
            EnsureArg.IsNotNull(searchResult);

            foreach (IFilterCriteria filterCriteria in _filterCriterias)
            {
                searchResult = filterCriteria.Apply(searchResult);
            }

            return searchResult;
        }

        public FilterCriteriaOutcome Match(ResourceWrapper resourceWrapper)
        {
            EnsureArg.IsNotNull(resourceWrapper);

            foreach (IFilterCriteria filterCriteria in _filterCriterias)
            {
                FilterCriteriaOutcome outcome = filterCriteria.Match(resourceWrapper);

                if (!outcome.Match)
                {
                    // Return the first outcome not maching with a filtering criteria.
                    return outcome;
                }
            }

            return FilterCriteriaOutcome.MatchingOutcome;
        }
    }
}
