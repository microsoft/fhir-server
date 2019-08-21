// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Features.Security;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    [AttributeUsage(AttributeTargets.Class)]
    internal class AuditLoggingFilterAttribute : ActionFilterAttribute
    {
        private readonly IClaimsExtractor _claimsExtractor;
        private readonly IAuditHelper _auditHelper;

        public AuditLoggingFilterAttribute(IClaimsExtractor claimsExtractor, IAuditHelper auditHelper)
        {
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(auditHelper, nameof(auditHelper));

            _claimsExtractor = claimsExtractor;
            _auditHelper = auditHelper;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            var actionDescriptor = context.ActionDescriptor as ControllerActionDescriptor;

            Debug.Assert(actionDescriptor != null, "The ActionDescriptor must be ControllerActionDescriptor.");

            if (actionDescriptor != null)
            {
                _auditHelper.LogExecuting(actionDescriptor.ControllerName, actionDescriptor.ActionName, context.HttpContext, _claimsExtractor);
            }

            base.OnActionExecuting(context);
        }

        public override void OnResultExecuted(ResultExecutedContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            var actionDescriptor = context.ActionDescriptor as ControllerActionDescriptor;

            Debug.Assert(actionDescriptor != null, "The ActionDescriptor must be ControllerActionDescriptor.");

            // The result can either be a FhirResult or an OperationOutcomeResult which both extend BaseActionResult.
            var result = context.Result as IBaseActionResult;

            _auditHelper.LogExecuted(
                actionDescriptor.ControllerName,
                actionDescriptor.ActionName,
                result?.GetResultTypeName(),
                context.HttpContext,
                _claimsExtractor);

            base.OnResultExecuted(context);
        }
    }
}
