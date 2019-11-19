// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Net;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Security;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    /// <summary>
    /// Provides helper methods for auditing.
    /// </summary>
    public class AuditHelper : IAuditHelper
    {
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IAuditEventTypeMapping _auditEventTypeMapping;
        private readonly IAuditLogger _auditLogger;
        private readonly ILogger<AuditHelper> _logger;
        private readonly IAuditHeaderReader _auditHeaderReader;

        public AuditHelper(
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IAuditEventTypeMapping auditEventTypeMapping,
            IAuditLogger auditLogger,
            ILogger<AuditHelper> logger,
            IAuditHeaderReader auditHeaderReader)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(auditEventTypeMapping, nameof(auditEventTypeMapping));
            EnsureArg.IsNotNull(auditLogger, nameof(auditLogger));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _auditEventTypeMapping = auditEventTypeMapping;
            _auditLogger = auditLogger;
            _logger = logger;
            _auditHeaderReader = auditHeaderReader;
        }

        /// <inheritdoc />
        public void LogExecuting(string controllerName, string actionName, HttpContext httpContext, IClaimsExtractor claimsExtractor)
        {
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(httpContext, nameof(httpContext));

            Log(AuditAction.Executing, controllerName, actionName, statusCode: null, resourceType: null, httpContext, claimsExtractor);
        }

        /// <inheritdoc />
        public void LogExecuted(string controllerName, string actionName, string responseResultType, HttpContext httpContext, IClaimsExtractor claimsExtractor)
        {
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(httpContext, nameof(httpContext));

            Log(AuditAction.Executed, controllerName, actionName, (HttpStatusCode)httpContext.Response.StatusCode, responseResultType, httpContext, claimsExtractor);
        }

        private void Log(AuditAction auditAction, string controllerName, string actionName, HttpStatusCode? statusCode, string resourceType, HttpContext httpContext, IClaimsExtractor claimsExtractor)
        {
            IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.FhirRequestContext;

            // fhirRequestContext.AuditEventType will not be set in the case of an unauthorized call because the filter that sets it will not be executed
            string auditEventType = string.IsNullOrWhiteSpace(fhirRequestContext.AuditEventType) ? _auditEventTypeMapping.GetAuditEventType(controllerName, actionName) : fhirRequestContext.AuditEventType;

            // Audit the call if an audit event type is associated with the action.
            if (auditEventType != null)
            {
                _auditLogger.LogAudit(
                    auditAction,
                    operation: auditEventType,
                    resourceType: resourceType,
                    requestUri: fhirRequestContext.Uri,
                    statusCode: statusCode,
                    correlationId: fhirRequestContext.CorrelationId,
                    callerIpAddress: httpContext.Connection?.RemoteIpAddress?.ToString(),
                    callerClaims: claimsExtractor.Extract(),
                    customHeaders: _auditHeaderReader.Read(httpContext));
            }
        }
    }
}
