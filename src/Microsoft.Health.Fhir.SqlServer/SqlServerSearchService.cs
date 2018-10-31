// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.SqlServer
{
    public class SqlServerSearchService : SearchService
    {
        public SqlServerSearchService(ISearchOptionsFactory searchOptionsFactory, IBundleFactory bundleFactory, IDataStore dataStore)
            : base(searchOptionsFactory, bundleFactory, dataStore)
        {
        }

        protected override Task<SearchResult> SearchInternalAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            return Task.FromResult(new SearchResult(Enumerable.Empty<ResourceWrapper>(), null));
        }

        protected override Task<SearchResult> SearchHistoryInternalAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
