// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema
{
    public class SqlServerSearchService : SearchService
    {
        public SqlServerSearchService(ISearchOptionsFactory searchOptionsFactory, IBundleFactory bundleFactory, IFhirDataStore fhirDataStore)
            : base(searchOptionsFactory, bundleFactory, fhirDataStore)
        {
        }

        protected override Task<SearchResult> SearchInternalAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            throw new System.NotImplementedException();
        }

        protected override Task<SearchResult> SearchHistoryInternalAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            throw new System.NotImplementedException();
        }
    }
}
