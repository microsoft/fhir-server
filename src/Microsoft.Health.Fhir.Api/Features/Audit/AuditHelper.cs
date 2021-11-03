// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.ValueSets;

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
        private static IList<string> _fhirAnonymousOperationTypeList = new List<string>();

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
            _fhirAnonymousOperationTypeList = GetConstants(typeof(FhirAnonymousOperationType));
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
            // Since AuditEventType holds value for both AuditEventType and FhirAnonymousOperationType ensure that we only log the AuditEventType
            if (!string.IsNullOrEmpty(auditEventType) && !_fhirAnonymousOperationTypeList.Contains(auditEventType, StringComparer.OrdinalIgnoreCase))
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

        /// <summary>
        /// Return all the values of constants of the specified type
        /// </summary>
        /// <param name="type">Type to examine</param>
        /// <returns>List of constant values</returns>
        public static IList<string> GetConstants(System.Type type)
        {
            if (_fhirAnonymousOperationTypeList.Count == 0)
            {
                FieldInfo[] fieldInfos = type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

                // Go through the list and only pick out the constants
                foreach (FieldInfo fi in fieldInfos)
                {
                    if (fi.IsLiteral && !fi.IsInitOnly)
                    {
                        _fhirAnonymousOperationTypeList.Add(fi.Name);
                    }
                }
            }

            // Return an array of FieldInfos
            return _fhirAnonymousOperationTypeList;
        }
    }
}
