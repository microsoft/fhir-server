// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.SqlServer
{
    public class SqlServerSearchService : SearchService
    {
        private readonly SqlServerDataStore _sqlServerDataStore;

        public SqlServerSearchService(ISearchOptionsFactory searchOptionsFactory, IBundleFactory bundleFactory, SqlServerDataStore sqlServerDataStore)
            : base(searchOptionsFactory, bundleFactory, sqlServerDataStore)
        {
            _sqlServerDataStore = sqlServerDataStore;
        }

        protected override Task<SearchResult> SearchInternalAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            return _sqlServerDataStore.Search(searchOptions, cancellationToken);
        }

        protected override Task<SearchResult> SearchHistoryInternalAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
