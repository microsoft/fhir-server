// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Api.Features.Context
{
    /// <summary>
    /// Middlware that runs before authentication middleware.
    /// </summary>
    public class FhirRequestContextBeforeAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IAuditEventTypeMapping _auditEventTypeMapping;

        public FhirRequestContextBeforeAuthenticationMiddleware(
            RequestDelegate next,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IAuditEventTypeMapping auditEventTypeMapping)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(auditEventTypeMapping, nameof(auditEventTypeMapping));

            _next = next;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _auditEventTypeMapping = auditEventTypeMapping;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                // Call the next delegate/middleware in the pipeline
                await _next(context);
            }
            finally
            {
                var statusCode = (HttpStatusCode)context.Response.StatusCode;

                // The authorization middleware runs before MVC middleware and therefore,
                // information related to route and audit will not be populated if authentication fails.
                // Handle such condition and populate them here if possible.
                if (_fhirRequestContextAccessor.RequestContext.RouteName == null &&
                    (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden))
                {
                    RouteData routeData = context.GetRouteData();

                    if (routeData?.Values != null)
                    {
                        routeData.Values.TryGetValue("controller", out object controllerName);
                        routeData.Values.TryGetValue("action", out object actionName);
                        routeData.Values.TryGetValue(KnownActionParameterNames.ResourceType, out object resourceType);

                        IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.RequestContext;

                        fhirRequestContext.AuditEventType = _auditEventTypeMapping.GetAuditEventType(
                            controllerName?.ToString(),
                            actionName?.ToString());
                        fhirRequestContext.ResourceType = resourceType?.ToString();
                    }
                }
            }
        }
    }
}
