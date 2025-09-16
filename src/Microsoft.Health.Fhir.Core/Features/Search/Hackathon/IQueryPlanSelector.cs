// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search.Hackathon
{
    public interface IQueryPlanSelector<T>
    {
        T GetQueryPlanCachingSetting(string hash);

        void ReportExecutionTime(string hash, T metricName, double executionTimeMs);
    }
}
