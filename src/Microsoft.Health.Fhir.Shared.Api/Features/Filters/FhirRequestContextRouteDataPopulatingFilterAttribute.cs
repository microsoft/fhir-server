// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Net;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class FhirRequestContextRouteDataPopulatingFilterAttribute : ActionFilterAttribute
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IAuditEventTypeMapping _auditEventTypeMapping;

        public FhirRequestContextRouteDataPopulatingFilterAttribute(
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IAuditEventTypeMapping auditEventTypeMapping)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(auditEventTypeMapping, nameof(auditEventTypeMapping));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _auditEventTypeMapping = auditEventTypeMapping;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.RequestContext;

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
                // if controllerActionDescriptor.ActionName is CustomError then retain the AuditEventType from previous context
                // e.g. In case of 500 error - we want to make sure we log the AuditEventType of the original request for which the error occurred in RequestMetric.
                fhirRequestContext.AuditEventType = KnownRoutes.CustomError.Contains(controllerActionDescriptor.ActionName, StringComparison.OrdinalIgnoreCase) ? fhirRequestContext.AuditEventType : _auditEventTypeMapping.GetAuditEventType(
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

                        try
                        {
                            // bundle.Type can raise InvalidCastException if the incoming value is not a valid BundleType.
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
                        catch (InvalidCastException)
                        {
                            // I had to add the 'timer' to the Http context, as the filter Microsoft.Health.Api.Features.Audit.AuditLoggingFilterAttribute expected it to be there.
                            // This change avoid a null reference exception to happen.
                            // The correct implementation would be handling the absence of the 'timer' in AuditLoggingFilterAttribute.
                            context.HttpContext.Items["timer"] = Stopwatch.StartNew();

                            context.Result = new OperationOutcomeResult(
                                new OperationOutcome
                                {
                                    Id = fhirRequestContext.CorrelationId,
                                    Issue =
                                    {
                                        new OperationOutcome.IssueComponent
                                        {
                                            Severity = OperationOutcome.IssueSeverity.Error,
                                            Code = OperationOutcome.IssueType.Invalid,
                                            Diagnostics = Core.Resources.UnsupportedBundleType,
                                        },
                                    },
                                },
                                HttpStatusCode.BadRequest);
                            return;
                        }
                    }
                }
            }

            if (context.HttpContext.Request.Headers.TryGetValue(KnownHeaders.PartiallyIndexedParamsHeaderName, out var headerValues))
            {
                fhirRequestContext.IncludePartiallyIndexedSearchParams = true;
            }

            base.OnActionExecuting(context);
        }
    }
}
