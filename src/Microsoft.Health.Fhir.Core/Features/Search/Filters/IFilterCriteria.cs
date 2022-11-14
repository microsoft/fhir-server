// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Search.Filters
{
    /// <summary>
    /// Common interface for data filter criteria.
    /// </summary>
    internal interface IFilterCriteria
    {
        SearchResult Apply(SearchResult searchResult);

        FilterCriteriaOutcome Match(ResourceWrapper resourceWrapper);
    }
}
