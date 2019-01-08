// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    public interface IAuditLogger
    {
        void LogAudit(
            AuditAction auditAction,
            string action,
            string resourceType,
            Uri requestUri,
            HttpStatusCode? statusCode,
            string correlationId,
            IReadOnlyCollection<KeyValuePair<string, string>> claims);
    }
}
