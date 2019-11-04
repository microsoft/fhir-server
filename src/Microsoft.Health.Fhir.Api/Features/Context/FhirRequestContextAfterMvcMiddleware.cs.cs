// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Api.Features.Context
{
    public class FhirRequestContextAfterMvcMiddleware
    {
        private readonly RequestDelegate _next;

        public FhirRequestContextAfterMvcMiddleware(RequestDelegate next)
        {
            EnsureArg.IsNotNull(next, nameof(next));

            _next = next;
        }

        public async Task Invoke(HttpContext context, IFhirRequestContextAccessor fhirRequestContextAccessor)
        {
            await _next(context);

            RouteData routeData = context.GetRouteData();

            object resourceType = null;

            if (routeData != null && routeData.Values != null)
            {
                routeData.Values.TryGetValue(KnownActionParameterNames.ResourceType, out resourceType);
            }

            fhirRequestContextAccessor.FhirRequestContext.ResourceType = resourceType?.ToString();
        }
    }
}
