// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Core.Logging.Metrics;

namespace Microsoft.Health.Fhir.Api.Features.Filters.Metrics
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SearchLatencyMetricEmitterAttribute : BaseLatencyMetricEmitterAttribute
    {
        private readonly SearchMetricHandler _metricHandler;

        public SearchLatencyMetricEmitterAttribute(SearchMetricHandler metricHandler)
        {
            EnsureArg.IsNotNull(metricHandler, nameof(metricHandler));

            _metricHandler = metricHandler;
        }

        // Under the scope of bundles, this methid is not called and no metrics are emitted.
        public override void OnActionExecuted(ActionExecutedContext context, long elapsedMilliseconds)
        {
            _metricHandler.EmitSearchLatency(new SearchMetricNotification { ElapsedMilliseconds = elapsedMilliseconds });
        }
    }
}
