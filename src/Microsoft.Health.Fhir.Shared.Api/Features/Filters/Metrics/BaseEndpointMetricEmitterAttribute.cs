// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Microsoft.Health.Fhir.Api.Features.Filters.Metrics
{
    public abstract class BaseEndpointMetricEmitterAttribute : ActionFilterAttribute
    {
        private Stopwatch _stopwatch;

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            _stopwatch = Stopwatch.StartNew();

            base.OnActionExecuting(context);
        }

        public override void OnActionExecuted(ActionExecutedContext context)
        {
            base.OnActionExecuted(context);

            EmitMetricOnActionExecuted(context, new ActionExecutedStatistics() { ElapsedMilliseconds = _stopwatch.ElapsedMilliseconds });
        }

        public abstract void EmitMetricOnActionExecuted(ActionExecutedContext context, ActionExecutedStatistics statistics);
    }
}
