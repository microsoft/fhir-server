// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Web;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Api.Features.AnonymousOperations;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    /// <summary>
    /// Provides helper methods for auditing.
    /// </summary>
    public class AuditHelper : IAuditHelper
    {
        internal const string DefaultCallerAgent = "Microsoft.Health.Fhir.Server";
        internal const string UnknownOperationType = "Unknown";

        private static readonly HashSet<string> ValidOperationTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            HttpMethods.Connect,
            HttpMethods.Delete,
            HttpMethods.Get,
            HttpMethods.Head,
            HttpMethods.Options,
            HttpMethods.Patch,
            HttpMethods.Post,
            HttpMethods.Put,
            HttpMethods.Trace,
        };

        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IAuditLogger _auditLogger;
        private readonly IAuditHeaderReader _auditHeaderReader;
        private static Lazy<IList<string>> _fhirAnonymousOperationTypeList;

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
            _fhirAnonymousOperationTypeList = new Lazy<IList<string>>(() => GetAnonymousOperations());
        }

        public static IList<string> FhirAnonymousOperationTypeList => _fhirAnonymousOperationTypeList.Value;

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

            // We are retaining AuditEventType when CustomError occurs. Below check ensures that the audit log is not entered for the custom error request
            httpContext.Request.RouteValues.TryGetValue("action", out object actionName);
            if (!string.IsNullOrEmpty(actionName?.ToString()) && KnownRoutes.CustomError.Contains(actionName?.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Audit the call if an audit event type is associated with the action.
            // Since AuditEventType holds value for both AuditEventType and FhirAnonymousOperationType ensure that we only log the AuditEventType
            if (!string.IsNullOrEmpty(auditEventType) && !FhirAnonymousOperationTypeList.Contains(auditEventType, StringComparer.OrdinalIgnoreCase))
            {
                // Note: HttpUtility.HtmlEncode() is to suffice a code scanning alert for log injection.
                var sanitizedOperationType = HttpUtility.HtmlEncode(httpContext.Request?.Method?.Trim());
                if (string.IsNullOrWhiteSpace(sanitizedOperationType) || !ValidOperationTypes.Contains(sanitizedOperationType))
                {
                    sanitizedOperationType = UnknownOperationType;
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
                    customHeaders: _auditHeaderReader.Read(httpContext),
                    operationType: sanitizedOperationType,
                    callerAgent: DefaultCallerAgent);
            }
        }

        /// <summary>
        /// Return all the values of constants of the specified type
        /// </summary>
        /// <returns>List of constant values</returns>
        private static IList<string> GetAnonymousOperations()
        {
            IList<string> anonymousOperations = new List<string>();
            FieldInfo[] fieldInfos = typeof(FhirAnonymousOperationType).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

            // Go through the list and only pick out the constants
            foreach (FieldInfo fi in fieldInfos)
            {
                if (fi.IsLiteral && !fi.IsInitOnly)
                {
                    anonymousOperations.Add(fi.Name);
                }
            }

            // Return an array of FieldInfos
            return anonymousOperations;
        }
    }
}
