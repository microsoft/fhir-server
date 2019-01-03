// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    public class FhirRequestContextRouteNameFilterAttribute : ActionFilterAttribute
    {
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IAuditHelper _auditHelper;

        public FhirRequestContextRouteNameFilterAttribute(
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IAuditHelper auditHelper)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(auditHelper, nameof(auditHelper));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _auditHelper = auditHelper;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.FhirRequestContext;

            if (context.ActionDescriptor is ControllerActionDescriptor actionDescriptor)
            {
                string auditEventType = _auditHelper.GetAuditEventType(actionDescriptor.ControllerName, actionDescriptor.ActionName);

                if (auditEventType != null)
                {
                    fhirRequestContext.RequestSubType = new Coding(ValueSets.AuditEventSubType.System, auditEventType);
                }
            }

            fhirRequestContext.RouteName = context.ActionDescriptor?.AttributeRouteInfo?.Name;
        }
    }
}
