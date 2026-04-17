// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Build.Framework;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal class QueryPlanReuseChecker
    {
        private readonly List<string> _skewedParameters = new List<string>();

        private ISqlRetryService _sqlRetryService;
        private ILogger<QueryPlanReuseChecker> _logger;

        public QueryPlanReuseChecker(ISqlRetryService sqlRetryService, ILogger<QueryPlanReuseChecker> logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public static bool CanReuseQueryPlan(SearchOptions searchOptions)
        {
            // Check the skew of the search parameters. If the search parameters are skewed, the query plan should not be reused.
            var parameters = searchOptions.SearchParameters;

            return true;
        }

        private async Task RefreshCache()
        {
            using var cmd = new SqlCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "dbo.GetAllStatistics";
            
            var results = await cmd.ExecuteReaderAsync(
                _sqlRetryService,
                (reader) =>
                {

                },
                _logger,
                CancellationToken.None);
        }
    }
}
