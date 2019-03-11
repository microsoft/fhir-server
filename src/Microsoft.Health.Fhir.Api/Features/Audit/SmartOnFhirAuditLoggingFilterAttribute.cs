// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    [AttributeUsage(AttributeTargets.Method)]
    internal class SmartOnFhirAuditLoggingFilterAttribute : ActionFilterAttribute
    {
        private readonly IAuditLogger _auditLogger;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly string _action;

        public SmartOnFhirAuditLoggingFilterAttribute(string action, IAuditLogger auditLogger, IFhirRequestContextAccessor fhirRequestContextAccessor)
        {
            EnsureArg.IsNotNullOrWhiteSpace(action, nameof(action));
            EnsureArg.IsNotNull(auditLogger, nameof(auditLogger));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));

            _action = action;
            _auditLogger = auditLogger;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            _auditLogger.LogAudit(
                AuditAction.Executing,
                _action,
                null,
                _fhirRequestContextAccessor.FhirRequestContext.Uri,
                null,
                _fhirRequestContextAccessor.FhirRequestContext.CorrelationId,
                GetClientIdFromQueryStringOrForm(context));

            base.OnActionExecuting(context);
        }

        public override void OnResultExecuted(ResultExecutedContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            _auditLogger.LogAudit(
                AuditAction.Executed,
                _action,
                null,
                _fhirRequestContextAccessor.FhirRequestContext.Uri,
                (HttpStatusCode)context.HttpContext.Response.StatusCode,
                _fhirRequestContextAccessor.FhirRequestContext.CorrelationId,
                GetClientIdFromQueryStringOrForm(context));

            base.OnResultExecuted(context);
        }

        private static ReadOnlyCollection<KeyValuePair<string, string>> GetClientIdFromQueryStringOrForm(FilterContext context)
        {
            StringValues clientId = context.HttpContext.Request.HasFormContentType ? context.HttpContext.Request.Form["client_id"] : context.HttpContext.Request.Query["client_id"];

            ReadOnlyCollection<KeyValuePair<string, string>> claims = clientId.Select(x => new KeyValuePair<string, string>("client_id", x)).ToList().AsReadOnly();
            return claims;
        }
    }
}
