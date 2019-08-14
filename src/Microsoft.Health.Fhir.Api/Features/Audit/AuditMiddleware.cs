// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Security;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    /// <summary>
    /// A middleware that logs audit events that cannot be logged by <see cref="AuditLoggingFilterAttribute"/>.
    /// </summary>
    public class AuditMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IClaimsExtractor _claimsExtractor;
        private readonly IAuditHelper _auditHelper;

        public AuditMiddleware(
            RequestDelegate next,
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IClaimsExtractor claimsExtractor,
            IAuditHelper auditHelper)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(auditHelper, nameof(auditHelper));

            _next = next;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _claimsExtractor = claimsExtractor;
            _auditHelper = auditHelper;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            finally
            {
                var statusCode = (HttpStatusCode)context.Response.StatusCode;

                // Since authorization filters runs first before any other filters, if the authorization fails,
                // the AuditLoggingFilterAttribute, which is where the audit logging would normally happen, will not be executed.
                // This middleware will log any Unauthorized or Forbidden request if it hasn't been logged yet.
                if (_fhirRequestContextAccessor.FhirRequestContext.RouteName == null &&
                    (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden))
                {
                    RouteData routeData = context.GetRouteData();

                    routeData.Values.TryGetValue("controller", out object controllerName);
                    routeData.Values.TryGetValue("action", out object actionName);
                    routeData.Values.TryGetValue(KnownActionParameterNames.ResourceType, out object resourceType);

                    _auditHelper.LogExecuted(
                        controllerName?.ToString(),
                        actionName?.ToString(),
                        resourceType?.ToString(),
                        context,
                        _claimsExtractor);
                }
            }
        }
    }
}
