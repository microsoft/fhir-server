// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    public class FhirRequestContextRouteDataPopulatingFilterAttribute : ActionFilterAttribute
    {
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IAuditEventTypeMapping _auditEventTypeMapping;

        public FhirRequestContextRouteDataPopulatingFilterAttribute(
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IAuditEventTypeMapping auditEventTypeMapping)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(auditEventTypeMapping, nameof(auditEventTypeMapping));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _auditEventTypeMapping = auditEventTypeMapping;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.FhirRequestContext;

            fhirRequestContext.RouteName = context.ActionDescriptor?.AttributeRouteInfo?.Name;

            // Set the resource type based on the route data
            RouteData routeData = context.RouteData;

            if (routeData?.Values != null)
            {
                if (routeData.Values.TryGetValue(KnownActionParameterNames.ResourceType, out object resourceType))
                {
                    fhirRequestContext.ResourceType = resourceType?.ToString();
                }
            }

            if (context.ActionDescriptor is ControllerActionDescriptor controllerActionDescriptor)
            {
                fhirRequestContext.AuditEventType = _auditEventTypeMapping.GetAuditEventType(
                    controllerActionDescriptor.ControllerName,
                    controllerActionDescriptor.ActionName);

                // If this is a request from the batch and transaction route, we need to examine the payload to set the AuditEventType
                if (fhirRequestContext.AuditEventType == AuditEventSubType.BundlePost)
                {
                    if (context.ActionArguments.TryGetValue(KnownActionParameterNames.Bundle, out object value))
                    {
                        if (!(value is Hl7.Fhir.Model.Bundle bundle))
                        {
                            return;
                        }

                        switch (bundle.Type)
                        {
                            case Hl7.Fhir.Model.Bundle.BundleType.Batch:
                                fhirRequestContext.AuditEventType = AuditEventSubType.Batch;
                                break;
                            case Hl7.Fhir.Model.Bundle.BundleType.Transaction:
                                fhirRequestContext.AuditEventType = AuditEventSubType.Transaction;
                                break;
                        }
                    }
                }
            }

            base.OnActionExecuting(context);
        }
    }
}
