// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using EnsureThat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    /// <summary>
    /// Provides helper methods for auditing.
    /// </summary>
    public class AuditHelper : IAuditHelper, IStartable
    {
        private readonly IActionDescriptorCollectionProvider _actionDescriptorCollectionProvider;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IClaimsIndexer _claimsIndexer;
        private readonly IAuditLogger _auditLogger;
        private readonly ILogger<AuditHelper> _logger;

        private IReadOnlyDictionary<(string ControllerName, string ActionName), Attribute> _attributeDictionary;

        public AuditHelper(
            IActionDescriptorCollectionProvider actionDescriptorCollectionProvider,
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IClaimsIndexer claimsIndexer,
            IAuditLogger auditLogger,
            ILogger<AuditHelper> logger)
        {
            EnsureArg.IsNotNull(actionDescriptorCollectionProvider, nameof(actionDescriptorCollectionProvider));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(claimsIndexer, nameof(claimsIndexer));
            EnsureArg.IsNotNull(auditLogger, nameof(auditLogger));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _actionDescriptorCollectionProvider = actionDescriptorCollectionProvider;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _claimsIndexer = claimsIndexer;
            _auditLogger = auditLogger;
            _logger = logger;
        }

        /// <inheritdoc />
        public string GetAuditEventType(string controllerName, string actionName)
        {
            Attribute attribute = GetAttribute(controllerName, actionName);

            if (attribute is AuditEventTypeAttribute auditEventTypeAttribute)
            {
                return auditEventTypeAttribute.AuditEventType;
            }

            return null;
        }

        /// <inheritdoc />
        public void LogExecuting(string controllerName, string actionName)
        {
            Log(AuditAction.Executing, controllerName, actionName, statusCode: null, resourceType: null);
        }

        /// <inheritdoc />
        public void LogExecuted(string controllerName, string actionName, HttpStatusCode statusCode, string resourceType)
        {
            Log(AuditAction.Executed, controllerName, actionName, statusCode, resourceType);
        }

        void IStartable.Start()
        {
            _attributeDictionary = _actionDescriptorCollectionProvider.ActionDescriptors.Items
                .OfType<ControllerActionDescriptor>()
                .Select(ad =>
                {
                    Attribute attribute = ad.MethodInfo?.GetCustomAttributes<AllowAnonymousAttribute>().FirstOrDefault() ??
                        (Attribute)ad.MethodInfo?.GetCustomAttributes<AuditEventTypeAttribute>().FirstOrDefault();

                    return (ad.ControllerName, ad.ActionName, Attribute: attribute);
                })
                .Where(item => item.Attribute != null)
                .ToDictionary(
                    item => (item.ControllerName, item.ActionName),
                    item => item.Attribute);
        }

        private Attribute GetAttribute(string controllerName, string actionName)
        {
            if (_attributeDictionary.TryGetValue((controllerName, actionName), out Attribute attribute))
            {
                return attribute;
            }

            throw new AuditException(controllerName, actionName);
        }

        private void Log(AuditAction auditAction, string controllerName, string actionName, HttpStatusCode? statusCode, string resourceType)
        {
            Attribute attribute = GetAttribute(controllerName, actionName);

            // If anonymous allowed, don't audit.
            if (attribute is AllowAnonymousAttribute)
            {
                return;
            }
            else if (attribute is AuditEventTypeAttribute auditEventTypeAttribute)
            {
                IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.FhirRequestContext;

                _auditLogger.LogAudit(
                    auditAction,
                    action: auditEventTypeAttribute.AuditEventType,
                    resourceType: resourceType,
                    requestUri: fhirRequestContext.Uri,
                    statusCode: statusCode,
                    correlationId: fhirRequestContext.CorrelationId,
                    claims: _claimsIndexer.Extract());
            }
        }
    }
}
