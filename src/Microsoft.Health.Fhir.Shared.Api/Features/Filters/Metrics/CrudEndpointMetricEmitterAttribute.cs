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
        private readonly ICrudMetricHandler _metricHandler;

        public CrudEndpointMetricEmitterAttribute(ICrudMetricHandler metricHandler)
        {
            EnsureArg.IsNotNull(metricHandler, nameof(metricHandler));

            _metricHandler = metricHandler;
        }

        // Under the scope of bundle requests, this methid is not called and no nested metrics are emitted.
        public override void EmitMetricOnActionExecuted(ActionExecutedContext context, ActionExecutedStatistics statistics)
        {
            _metricHandler.EmitLatency(new CrudMetricNotification { ElapsedMilliseconds = statistics.ElapsedMilliseconds });
        }
    }
}
