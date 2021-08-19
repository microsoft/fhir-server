// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Configs;
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
        private readonly IOptions<AuditConfiguration> _auditConfiguration;

        public AuditHelper(
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IAuditLogger auditLogger,
            IAuditHeaderReader auditHeaderReader,
            IOptions<AuditConfiguration> auditConfiguration)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(auditLogger, nameof(auditLogger));
            EnsureArg.IsNotNull(auditHeaderReader, nameof(auditHeaderReader));
            EnsureArg.IsNotNull(auditConfiguration, nameof(auditConfiguration));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _auditLogger = auditLogger;
            _auditHeaderReader = auditHeaderReader;
            _auditConfiguration = auditConfiguration;
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
                var customHeaders = (Dictionary<string, string>)_auditHeaderReader.Read(httpContext);

                // check for custom audit headers in context-response
                if (auditAction == AuditAction.Executed)
                {
                    CheckForCustomAuditHeadersInResponse(httpContext, customHeaders);
                }

                _auditLogger.LogAudit(
                    auditAction,
                    operation: auditEventType,
                    resourceType: fhirRequestContext.ResourceType,
                    requestUri: fhirRequestContext.Uri,
                    statusCode: statusCode,
                    correlationId: fhirRequestContext.CorrelationId,
                    callerIpAddress: httpContext.Connection?.RemoteIpAddress?.ToString(),
                    callerClaims: claimsExtractor.Extract(),
                    customHeaders: customHeaders);
            }
        }

        private void CheckForCustomAuditHeadersInResponse(HttpContext httpContext, Dictionary<string, string> customHeaders)
        {
            var responseCustomHeaders = httpContext.Response.Headers.Where(x => x.Key.StartsWith(_auditConfiguration.Value.CustomAuditHeaderPrefix, StringComparison.OrdinalIgnoreCase)).ToDictionary(a => a.Key, a => a.Value.ToString());
            if (responseCustomHeaders.Any())
            {
                var largeHeaders = responseCustomHeaders.Where(x => x.Value.Length > AuditConstants.MaximumLengthOfCustomHeader).ToDictionary(a => a.Key, a => a.Value.ToString());
                if (largeHeaders.Any())
                {
                    throw new AuditHeaderTooLargeException(largeHeaders.First().Key, largeHeaders.First().Value.Length);
                }

                foreach (var header in responseCustomHeaders)
                {
                    var headerValue = header.Value.ToString();
                    if (headerValue.Length > AuditConstants.MaximumLengthOfCustomHeader)
                    {
                        throw new AuditHeaderTooLargeException(header.Key, headerValue.Length);
                    }

                    if (!customHeaders.ContainsKey(header.Key))
                    {
                        customHeaders.Add(header.Key, headerValue);
                    }
                }

                if (customHeaders.Count > AuditConstants.MaximumNumberOfCustomHeaders)
                {
                    throw new AuditHeaderCountExceededException(customHeaders.Count);
                }
            }
        }
    }
}
