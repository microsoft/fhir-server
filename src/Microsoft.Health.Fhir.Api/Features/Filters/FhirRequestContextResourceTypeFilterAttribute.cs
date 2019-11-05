// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    public class FhirRequestContextResourceTypeFilterAttribute : ActionFilterAttribute
    {
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;

        public FhirRequestContextResourceTypeFilterAttribute(IFhirRequestContextAccessor fhirRequestContextAccessor)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.FhirRequestContext;

            RouteData routeData = context.RouteData;

            object resourceType = null;

            if (routeData != null && routeData.Values != null)
            {
                routeData.Values.TryGetValue(KnownActionParameterNames.ResourceType, out resourceType);
            }

            _fhirRequestContextAccessor.FhirRequestContext.ResourceType = resourceType?.ToString();
        }
    }
}
