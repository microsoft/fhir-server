// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    /// <summary>
    /// Latency over efficiency filter.
    /// Decorates controller classes witch requests can contain requests with HTTP Readers decorated by latency over efficiency flag.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class QueryLatencyOverEfficiencyFilterAttribute : ActionFilterAttribute
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;

        public QueryLatencyOverEfficiencyFilterAttribute(RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
        }

        public override void OnActionExecuted(ActionExecutedContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            SetupConditionalRequestWithQueryOptimizeConcurrency(context.HttpContext, _fhirRequestContextAccessor.RequestContext);

            base.OnActionExecuted(context);
        }

        private static void SetupConditionalRequestWithQueryOptimizeConcurrency(HttpContext context, IFhirRequestContext fhirRequestContext)
        {
            if (context?.Request?.Headers != null && fhirRequestContext != null)
            {
                bool latencyOverEfficiencyEnabled = context.IsLatencyOverEfficiencyEnabled();

                if (latencyOverEfficiencyEnabled)
                {
                    fhirRequestContext.DecorateRequestContextWithOptimizedConcurrency();
                }
            }
        }
    }
}
