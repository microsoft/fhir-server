// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search.Filters;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public sealed class SearchResultFilter : ISearchResultFilter
    {
        private readonly bool _isUSCoreEnabled;
        private readonly bool _isSmartUserRequest;

        public SearchResultFilter(IOptions<ImplementationGuidesConfiguration> implementationGuides, RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor)
        {
            _isUSCoreEnabled = implementationGuides?.Value?.USCore?.MissingData ?? false;
            _isSmartUserRequest = fhirRequestContextAccessor?.RequestContext?.AccessControlContext?.ApplyFineGrainedAccessControl ?? false;
        }

        private SearchResultFilter(bool isUSCoreEnabled, bool isSmartUserRequest)
        {
            _isUSCoreEnabled = isUSCoreEnabled;
            _isSmartUserRequest = isSmartUserRequest;
        }

        public static SearchResultFilter Default => new SearchResultFilter(isUSCoreEnabled: false, isSmartUserRequest: false);

        public SearchResult Filter(SearchResult searchResult)
        {
            EnsureArg.IsNotNull(searchResult);

            // Set of filter criteria to be applied on top of a SearchResult.
            IFilterCriteria[] filterCriterias =
            {
                new MissingDataFilterCriteria(isCriteriaEnabled: _isUSCoreEnabled, isSmartRequest: _isSmartUserRequest),
            };

            foreach (IFilterCriteria filterCriteria in filterCriterias)
            {
                searchResult = filterCriteria.Apply(searchResult);
            }

            return searchResult;
        }
    }
}
