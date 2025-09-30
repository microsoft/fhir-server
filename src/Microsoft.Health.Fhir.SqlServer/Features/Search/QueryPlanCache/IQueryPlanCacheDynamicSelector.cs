// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.QueryPlanCache
{
    public interface IQueryPlanCacheDynamicSelector
    {
        bool GetDefaultQueryPlanCacheSetting();

        bool GetRecommendedQueryPlanCacheSetting(string hash);

        void ReportExecutionTime(string hash, bool metricName, double executionTimeMs);
    }
}
