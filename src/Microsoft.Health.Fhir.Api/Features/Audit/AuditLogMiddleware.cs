// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    public class AuditLogMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IAuditLogger _auditLogger;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IClaimsIndexer _claimsIndexer;

        public AuditLogMiddleware(
            RequestDelegate next,
            IAuditLogger auditLogger,
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IClaimsIndexer claimsIndexer)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(auditLogger, nameof(auditLogger));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(claimsIndexer, nameof(claimsIndexer));

            _next = next;
            _auditLogger = auditLogger;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _claimsIndexer = claimsIndexer;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            finally
            {
                IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.FhirRequestContext;

                _auditLogger.LogAudit(
                    AuditAction.Executed,
                    action: fhirRequestContext.Method,
                    resourceType: null,
                    requestUri: fhirRequestContext.Uri,
                    statusCode: (HttpStatusCode)context.Response.StatusCode,
                    correlationId: fhirRequestContext.CorrelationId,
                    claims: _claimsIndexer.Extract());
            }
        }
    }
}
