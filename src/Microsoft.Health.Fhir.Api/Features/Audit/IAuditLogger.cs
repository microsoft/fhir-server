// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    /// <summary>
    /// Provides mechanism to log audit event.
    /// </summary>
    public interface IAuditLogger
    {
        /// <summary>
        /// Logs an audit event.
        /// </summary>
        /// <param name="auditAction">The action to audit.</param>
        /// <param name="operation">The FHIR operation to audit.</param>
        /// <param name="resourceType">The FHIR resource type to audit.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="statusCode">The response status code (if any).</param>
        /// <param name="correlationId">The correlation ID.</param>
        /// <param name="callerIpAddress">The caller IP address.</param>
        /// <param name="callerClaims">The claims of the caller.</param>
        /// <param name="customHeaders">Headers added by the caller with data to be added to the audit logs.</param>
        void LogAudit(
            AuditAction auditAction,
            string operation,
            string resourceType,
            Uri requestUri,
            HttpStatusCode? statusCode,
            string correlationId,
            string callerIpAddress,
            IReadOnlyCollection<KeyValuePair<string, string>> callerClaims,
            IReadOnlyDictionary<string, string> customHeaders = null);
    }
}
