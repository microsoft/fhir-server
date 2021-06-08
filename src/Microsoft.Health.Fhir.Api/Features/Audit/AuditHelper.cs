// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    /// <summary>
    /// Provides helper methods for auditing.
    /// </summary>
    public class AuditHelper : IAuditHelper
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IAuditLogger _auditLogger;
        private readonly IAuditHeaderReader _auditHeaderReader;

        public AuditHelper(
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IAuditLogger auditLogger,
            IAuditHeaderReader auditHeaderReader)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(auditLogger, nameof(auditLogger));
            EnsureArg.IsNotNull(auditHeaderReader, nameof(auditHeaderReader));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _auditLogger = auditLogger;
            _auditHeaderReader = auditHeaderReader;
        }

        /// <inheritdoc />
        public void LogExecuting(HttpContext httpContext, IClaimsExtractor claimsExtractor)
        {
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(httpContext, nameof(httpContext));

            Log(AuditAction.Executing, statusCode: null, httpContext, claimsExtractor);
        }

        /// <summary>
        /// Logs an executed audit entry for the current operation.
        /// </summary>
        /// <param name="httpContext">The HTTP context.</param>
        /// <param name="claimsExtractor">The extractor used to extract claims.</param>
        /// <param name="shouldCheckForAuthXFailure">Only emit LogExecuted messages if this is an authentication error (401), since others would already have been logged.</param>
        public void LogExecuted(HttpContext httpContext, IClaimsExtractor claimsExtractor, bool shouldCheckForAuthXFailure = false)
        {
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(httpContext, nameof(httpContext));

            var responseStatusCode = (HttpStatusCode)httpContext.Response.StatusCode;
            if (!shouldCheckForAuthXFailure || responseStatusCode == HttpStatusCode.Unauthorized)
            {
                Log(AuditAction.Executed, responseStatusCode, httpContext, claimsExtractor);
            }
        }

        private void Log(AuditAction auditAction, HttpStatusCode? statusCode, HttpContext httpContext, IClaimsExtractor claimsExtractor)
        {
            IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.RequestContext;

            string auditEventType = fhirRequestContext.AuditEventType;

            // Audit the call if an audit event type is associated with the action.
            if (!string.IsNullOrEmpty(auditEventType))
            {
                _auditLogger.LogAudit(
                    auditAction,
                    operation: auditEventType,
                    resourceType: fhirRequestContext.ResourceType,
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
