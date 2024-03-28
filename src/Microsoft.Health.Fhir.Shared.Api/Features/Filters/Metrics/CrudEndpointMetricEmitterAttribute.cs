// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Core.Logging.Metrics;

namespace Microsoft.Health.Fhir.Api.Features.Filters.Metrics
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class CrudEndpointMetricEmitterAttribute : BaseEndpointMetricEmitterAttribute
    {
        private readonly CrudMetricHandler _metricHandler;

        public CrudEndpointMetricEmitterAttribute(CrudMetricHandler metricHandler)
        {
            EnsureArg.IsNotNull(metricHandler, nameof(metricHandler));

            _metricHandler = metricHandler;
        }

        // Under the scope of bundle requests, this methid is not called and no metrics are emitted.
        public override void OnActionExecuted(ActionExecutedContext context, ActionExecutedStatistics statistics)
        {
            _metricHandler.EmitCrudLatency(new CrudMetricNotification { ElapsedMilliseconds = statistics.ElapsedMilliseconds });
        }
    }
}
