// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Logging;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    internal class AuditLoggingFilterAttribute : ActionFilterAttribute
    {
        private readonly IFhirContextAccessor _fhirContextAccessor;
        private readonly IClaimsIndexer _claimsIndexer;
        private readonly IAuditLogger _auditLogger;
        private static ConcurrentDictionary<ControllerActionDescriptor, Attribute> s_attributeDict = new ConcurrentDictionary<ControllerActionDescriptor, Attribute>();

        public AuditLoggingFilterAttribute(IAuditLogger auditLogger, IFhirContextAccessor fhirContextAccessor, IClaimsIndexer claimsIndexer)
        {
            EnsureArg.IsNotNull(auditLogger, nameof(auditLogger));
            EnsureArg.IsNotNull(fhirContextAccessor, nameof(fhirContextAccessor));
            EnsureArg.IsNotNull(claimsIndexer, nameof(claimsIndexer));

            _auditLogger = auditLogger;
            _fhirContextAccessor = fhirContextAccessor;
            _claimsIndexer = claimsIndexer;
        }

        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            EnsureArg.IsNotNull(filterContext, nameof(filterContext));

            if (filterContext.ActionDescriptor is ControllerActionDescriptor actionDescriptor)
            {
                var attribute = s_attributeDict.GetOrAdd(actionDescriptor, GetAttributeToAdd);

                if (attribute == null || !(attribute is AllowAnonymousAttribute || attribute is AuditEventSubTypeAttribute))
                {
                    throw new NotSupportedException(string.Format(Resources.AuditEventSubTypeNotSet, actionDescriptor.MethodInfo.Name));
                }

                // If anonymous allowed, don't audit.
                if (attribute is AllowAnonymousAttribute)
                {
                    base.OnActionExecuting(filterContext);
                    return;
                }

                var auditEventSubTypeAttribute = attribute as AuditEventSubTypeAttribute;

                _fhirContextAccessor.FhirContext.RequestSubType = new Coding(ValueSets.AuditEventSubType.System, auditEventSubTypeAttribute.AuditEventType);

                _auditLogger.LogAudit(
                    AuditAction.Executing,
                    action: _fhirContextAccessor.FhirContext.RequestSubType.Code,
                    resourceType: null,
                    requestUri: _fhirContextAccessor.FhirContext.RequestUri,
                    statusCode: null,
                    correlationId: _fhirContextAccessor.FhirContext.CorrelationId,
                    claims: _claimsIndexer.Extract());
            }

            base.OnActionExecuting(filterContext);
        }

        private static Attribute GetAttributeToAdd(ControllerActionDescriptor actionDescriptor)
        {
            Attribute attribute = actionDescriptor?.MethodInfo?.GetCustomAttributes<AllowAnonymousAttribute>()?.FirstOrDefault();
            if (attribute != null)
            {
                return attribute;
            }

            return actionDescriptor?.MethodInfo?.GetCustomAttributes<AuditEventSubTypeAttribute>()?.FirstOrDefault();
        }

        public override void OnResultExecuted(ResultExecutedContext filterContext)
        {
            EnsureArg.IsNotNull(filterContext, nameof(filterContext));

            var actionDescriptor = filterContext.ActionDescriptor as ControllerActionDescriptor;

            var attribute = s_attributeDict.GetOrAdd(actionDescriptor, GetAttributeToAdd);

            // If anonymous allowed, don't audit.
            if (attribute is AllowAnonymousAttribute)
            {
                base.OnResultExecuted(filterContext);
                return;
            }

            var fhirResult = filterContext.Result as FhirResult;

            _auditLogger.LogAudit(
                AuditAction.Executed,
                _fhirContextAccessor.FhirContext.RequestSubType.Code,
                fhirResult?.Resource?.TypeName,
                _fhirContextAccessor.FhirContext.RequestUri,
                (HttpStatusCode)filterContext.HttpContext.Response.StatusCode,
                _fhirContextAccessor.FhirContext.CorrelationId,
                _claimsIndexer.Extract());

            base.OnResultExecuted(filterContext);
        }
    }
}
