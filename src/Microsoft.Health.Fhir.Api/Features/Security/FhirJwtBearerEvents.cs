// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Health.Fhir.Api.Features.Logging;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Api.Features.Security
{
    public class FhirJwtBearerEvents : JwtBearerEvents
    {
        private readonly IAuditLogger _auditLogger;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IClaimsIndexer _claimsIndexer;

        public FhirJwtBearerEvents(IAuditLogger auditLogger, IFhirRequestContextAccessor fhirRequestContextAccessor, IClaimsIndexer claimsIndexer)
        {
            EnsureArg.IsNotNull(auditLogger, nameof(auditLogger));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(claimsIndexer, nameof(claimsIndexer));

            _auditLogger = auditLogger;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _claimsIndexer = claimsIndexer;
        }

        public override Task AuthenticationFailed(AuthenticationFailedContext context)
        {
            IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.FhirRequestContext;

            _auditLogger.LogAudit(
                AuditAction.Executed,
                action: fhirRequestContext.Method,
                resourceType: null,
                requestUri: fhirRequestContext.Uri,
                statusCode: System.Net.HttpStatusCode.Unauthorized,
                correlationId: fhirRequestContext.CorrelationId,
                claims: _claimsIndexer.Extract());

            return base.AuthenticationFailed(context);
        }
    }
}
