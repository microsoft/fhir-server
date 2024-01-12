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
using Microsoft.Health.Fhir.Core.Registration;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    /// <summary>
    /// Latency over efficiency filter.
    /// Adds to FHIR Request Context a flag to optimize query latency over efficiency.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class QueryLatencyOverEfficiencyFilterAttribute : ActionFilterAttribute
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IFhirRuntimeConfiguration _runtimeConfiguration;

        public QueryLatencyOverEfficiencyFilterAttribute(RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor, IFhirRuntimeConfiguration runtimeConfiguration)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(runtimeConfiguration, nameof(runtimeConfiguration));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _runtimeConfiguration = runtimeConfiguration;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (_runtimeConfiguration.IsLatencyOverEfficiencySupported)
            {
                SetupConditionalRequestWithQueryOptimizeConcurrency(context.HttpContext, _fhirRequestContextAccessor.RequestContext);
            }

            base.OnActionExecuting(context);
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
