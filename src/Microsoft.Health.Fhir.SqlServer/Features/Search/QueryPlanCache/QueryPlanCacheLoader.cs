// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.QueryPlanCache
{
    public class QueryPlanCacheLoader : IQueryPlanCacheLoader
    {
        internal const string ReuseQueryPlansParameterId = "Search.ReuseQueryPlans.IsEnabled";

        private readonly ISqlRetryService _sqlRetryService;
        private readonly ProcessingFlag<QueryPlanCacheLoader> _processingFlag;

        public QueryPlanCacheLoader(ISqlRetryService sqlRetryService, ILogger<QueryPlanCacheLoader> logger)
        {
            EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlRetryService = sqlRetryService;
            _processingFlag = new ProcessingFlag<QueryPlanCacheLoader>(ReuseQueryPlansParameterId, false, logger);
        }

        public bool IsEnabled()
        {
            return _processingFlag.IsEnabled(_sqlRetryService);
        }

        public void Reset()
        {
            _processingFlag.Reset();
        }
    }
}
