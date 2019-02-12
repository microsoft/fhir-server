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
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;

        public FhirRequestContextRouteNameFilterAttribute(IFhirRequestContextAccessor fhirRequestContextAccessor)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.FhirRequestContext;

            fhirRequestContext.RouteName = context.ActionDescriptor?.AttributeRouteInfo?.Name;
        }
    }
}
