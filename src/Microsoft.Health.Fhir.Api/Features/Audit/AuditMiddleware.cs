// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Core.Features.Security;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    /// <summary>
    /// A middleware that logs executed audit events.
    /// </summary>
    public class AuditMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IClaimsExtractor _claimsExtractor;
        private readonly IAuditHelper _auditHelper;

        public AuditMiddleware(
            RequestDelegate next,
            IClaimsExtractor claimsExtractor,
            IAuditHelper auditHelper)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(auditHelper, nameof(auditHelper));

            _next = next;
            _claimsExtractor = claimsExtractor;
            _auditHelper = auditHelper;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            finally
            {
                var statusCode = (HttpStatusCode)context.Response.StatusCode;

                // Since authorization filters runs first before any other filters, if the authorization fails,
                // the AuditLoggingFilterAttribute, which is where the audit logging would normally happen, will not be executed.
                // This middleware will log any Unauthorized request if it hasn't been logged yet.
                if (statusCode == HttpStatusCode.Unauthorized)
                {
                    _auditHelper.LogExecuted(context, _claimsExtractor);
                }
            }
        }
    }
}
