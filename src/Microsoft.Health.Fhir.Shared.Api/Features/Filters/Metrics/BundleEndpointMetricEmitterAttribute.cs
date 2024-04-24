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
    public sealed class BundleEndpointMetricEmitterAttribute : BaseEndpointMetricEmitterAttribute
    {
        private readonly IBundleMetricHandler _metricHandler;

        public BundleEndpointMetricEmitterAttribute(IBundleMetricHandler metricHandler)
        {
            EnsureArg.IsNotNull(metricHandler, nameof(metricHandler));

            _metricHandler = metricHandler;
        }

        public override void EmitMetricOnActionExecuted(ActionExecutedContext context, ActionExecutedStatistics statistics)
        {
            _metricHandler.EmitLatency(new BundleMetricNotification { ElapsedMilliseconds = statistics.ElapsedMilliseconds });
        }
    }
}
