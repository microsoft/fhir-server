// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.Metrics;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics.Handlers
{
    public sealed class DefaultSearchParameterCacheRefresherMetricHandler : BaseSuccessRateMetricHandler, ISearchParameterCacheRefresherMetricHandler
    {
        public DefaultSearchParameterCacheRefresherMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory, successMetricName: "SearchParameter.CacheRefresher.Success", failureMetricName: "SearchParameter.CacheRefresher.Failure")
        {
        }
    }
}
