// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    public class FhirRequestContextRouteNameFilterAttribute : ActionFilterAttribute
    {
        private readonly IFhirRequestContextAccessor _fhirRequestAccessor;

        public FhirRequestContextRouteNameFilterAttribute(IFhirRequestContextAccessor fhirRequestAccessor)
        {
            EnsureArg.IsNotNull(fhirRequestAccessor, nameof(fhirRequestAccessor));

            _fhirRequestAccessor = fhirRequestAccessor;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            _fhirRequestAccessor.FhirRequestContext.RouteName = context.ActionDescriptor?.AttributeRouteInfo?.Name;
        }
    }
}
