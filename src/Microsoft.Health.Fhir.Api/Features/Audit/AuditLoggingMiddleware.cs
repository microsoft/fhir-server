// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Routing;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    /// <summary>
    /// Middleware to ensure audit logging for HTTP 405 responses that bypass controller filters.
    /// </summary>
    public class AuditLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IAuditHelper _auditHelper;
        private readonly IClaimsExtractor _claimsExtractor;
        private readonly IAuditEventTypeMapping _auditEventTypeMapping;

        public AuditLoggingMiddleware(
            RequestDelegate next,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IAuditHelper auditHelper,
            IClaimsExtractor claimsExtractor,
            IAuditEventTypeMapping auditEventTypeMapping)
        {
            _next = EnsureArg.IsNotNull(next, nameof(next));
            _fhirRequestContextAccessor = EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            _auditHelper = EnsureArg.IsNotNull(auditHelper, nameof(auditHelper));
            _claimsExtractor = EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            _auditEventTypeMapping = EnsureArg.IsNotNull(auditEventTypeMapping, nameof(auditEventTypeMapping));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            var stopwatch = Stopwatch.StartNew();
            var wasAuditLogged = false;

            try
            {
                await _next(context);
            }
            finally
            {
                stopwatch.Stop();

                // Check if this is an HTTP 405 response that wasn't handled by controller filters
                if (context.Response.StatusCode == (int)HttpStatusCode.MethodNotAllowed)
                {
                    // Check if audit logging was already handled by checking if request context has audit event type
                    var fhirRequestContext = _fhirRequestContextAccessor.RequestContext;
                    if (string.IsNullOrEmpty(fhirRequestContext?.AuditEventType))
                    {
                        // Populate minimal FHIR request context for audit logging
                        PopulateFhirRequestContextFor405(context);
                        
                        // Log the audit entry for the 405 response
                        _auditHelper.LogExecuted(context, _claimsExtractor, shouldCheckForAuthXFailure: false, durationMs: stopwatch.ElapsedMilliseconds);
                        wasAuditLogged = true;
                    }
                }
            }
        }

        private void PopulateFhirRequestContextFor405(HttpContext context)
        {
            var fhirRequestContext = _fhirRequestContextAccessor.RequestContext;
            
            // Try to extract resource type from the path
            string resourceType = ExtractResourceTypeFromPath(context.Request.Path);
            if (!string.IsNullOrEmpty(resourceType))
            {
                fhirRequestContext.ResourceType = resourceType;
            }

            // Set a generic audit event type for 405 responses
            fhirRequestContext.AuditEventType = "MethodNotAllowed";
        }

        private string ExtractResourceTypeFromPath(PathString path)
        {
            if (path.HasValue)
            {
                var segments = path.Value.Split('/', StringSplitOptions.RemoveEmptyEntries);
                
                // Look for the first segment that could be a FHIR resource type
                // FHIR paths typically follow patterns like: /ResourceType, /ResourceType/id, etc.
                foreach (var segment in segments)
                {
                    // Check if segment starts with uppercase letter (FHIR resource types are PascalCase)
                    if (!string.IsNullOrEmpty(segment) && char.IsUpper(segment[0]))
                    {
                        return segment;
                    }
                }
            }

            return null;
        }
    }
}