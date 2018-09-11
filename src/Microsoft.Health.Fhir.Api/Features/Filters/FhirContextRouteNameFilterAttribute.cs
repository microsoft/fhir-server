// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    public class FhirContextRouteNameFilterAttribute : ActionFilterAttribute
    {
        private readonly IFhirContextAccessor _accessor;

        public FhirContextRouteNameFilterAttribute(IFhirContextAccessor accessor)
        {
            _accessor = accessor;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            _accessor.FhirContext.RouteName = context.ActionDescriptor?.AttributeRouteInfo?.Name;
        }
    }
}
