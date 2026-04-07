// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.ValueSets;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class FhirRequestContextRouteDataPopulatingFilterAttribute : ActionFilterAttribute
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IAuditEventTypeMapping _auditEventTypeMapping;
        private readonly ILogger<FhirRequestContextRouteDataPopulatingFilterAttribute> _logger;

        public FhirRequestContextRouteDataPopulatingFilterAttribute(
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IAuditEventTypeMapping auditEventTypeMapping,
            ILogger<FhirRequestContextRouteDataPopulatingFilterAttribute> logger)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(auditEventTypeMapping, nameof(auditEventTypeMapping));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _auditEventTypeMapping = auditEventTypeMapping;
            _logger = logger;
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

                        BundleType? bundleType = GetBundleType(bundle);

                        switch (bundleType)
                        {
                            case BundleType.Batch:
                                fhirRequestContext.AuditEventType = AuditEventSubType.Batch;
                                break;
                            case BundleType.Transaction:
                                fhirRequestContext.AuditEventType = AuditEventSubType.Transaction;
                                break;
                            case null:
                                // If bundleType is null, it means there was an error while trying to read the Bundle or the Bundle didn't have a type.
                                // In either case, we should log this as an InvalidBundleType for better visibility in our audit.
                                // Using ContentResult to bypass FHIR formatter pipeline which requires fully initialized request context.
                                fhirRequestContext.AuditEventType = "InvalidBundleType";
                                context.Result = new ContentResult
                                {
                                    StatusCode = 400,
                                    Content = "{\"resourceType\":\"OperationOutcome\",\"issue\":[{\"severity\":\"error\",\"code\":\"invalid\",\"diagnostics\":\"Bundle type is missing or invalid.\"}]}",
                                    ContentType = "application/fhir+json",
                                };
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

        private BundleType? GetBundleType(Hl7.Fhir.Model.Bundle bundle)
        {
            BundleType? bundleType;
            try
            {
                bundleType = bundle.Type;
            }
            catch (InvalidCastException ice)
            {
                // This scenario is better handled in BundleHandler, as it will be caught and returned as a proper 400 Bad Request in the context of bundle processing.
                bundleType = null;
                _logger.LogWarning(ice, "Error while auditing bundle.");
            }

            return bundleType;
        }
    }
}
