// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Core.Features.Security;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    [AttributeUsage(AttributeTargets.Class)]
    internal class AuditLoggingFilterAttribute : ActionFilterAttribute
    {
        private readonly IClaimsExtractor _claimsExtractor;
        private readonly IAuditHelper _auditHelper;

        public AuditLoggingFilterAttribute(
            IClaimsExtractor claimsExtractor,
            IAuditHelper auditHelper)
        {
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(auditHelper, nameof(auditHelper));

            _claimsExtractor = claimsExtractor;
            _auditHelper = auditHelper;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            _auditHelper.LogExecuting(context.HttpContext, _claimsExtractor);

            base.OnActionExecuting(context);
        }

        public override void OnResultExecuted(ResultExecutedContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            // The status code of 403 is only ever encountered here in the case of a failed bundle sub operation.
            // All other 403s will be thrown before the attributes are executed and audited in the middleware.
            if ((HttpStatusCode)context.HttpContext.Response.StatusCode != HttpStatusCode.Forbidden)
            {
                _auditHelper.LogExecuted(context.HttpContext, _claimsExtractor);
            }

            base.OnResultExecuted(context);
        }
    }
}
