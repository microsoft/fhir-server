// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Health.Fhir.Api.Features.Audit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Audit
{
    public class TraceAuditLogger : IAuditLogger
    {
        private BlockingCollection<AuditEntry> _auditEntries = new BlockingCollection<AuditEntry>();

        public void LogAudit(
            AuditAction auditAction,
            string action,
            string resourceType,
            Uri requestUri,
            HttpStatusCode? statusCode,
            string correlationId,
            string callerIpAddress,
            IReadOnlyCollection<KeyValuePair<string, string>> callerClaims,
            IReadOnlyCollection<KeyValuePair<string,string>> customHeaders)
        {
            _auditEntries.Add(new AuditEntry(auditAction, action, resourceType, requestUri, statusCode, correlationId, callerIpAddress, callerClaims, customHeaders));
        }

        public IReadOnlyList<AuditEntry> GetAuditEntriesByCorrelationId(string correlationId)
        {
            return _auditEntries.Where(ae => ae.CorrelationId == correlationId).ToList();
        }
    }
}
