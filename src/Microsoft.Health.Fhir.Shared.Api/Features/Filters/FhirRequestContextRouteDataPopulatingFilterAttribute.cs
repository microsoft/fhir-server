// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.ValueSets;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class FhirRequestContextRouteDataPopulatingFilterAttribute : ActionFilterAttribute
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IAuditEventTypeMapping _auditEventTypeMapping;
        private readonly ILogger<FhirRequestContextRouteDataPopulatingFilterAttribute> _logger;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly SecurityConfiguration _securityConfiguration;

        public FhirRequestContextRouteDataPopulatingFilterAttribute(
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IAuditEventTypeMapping auditEventTypeMapping,
            ILogger<FhirRequestContextRouteDataPopulatingFilterAttribute> logger,
            IAuthorizationService<DataActions> authorizationService,
            IOptions<SecurityConfiguration> securityConfiguration)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(auditEventTypeMapping, nameof(auditEventTypeMapping));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(securityConfiguration?.Value, nameof(securityConfiguration));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _auditEventTypeMapping = auditEventTypeMapping;
            _logger = logger;
            _authorizationService = authorizationService;
            _securityConfiguration = securityConfiguration.Value;
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            await ApplyPartialIndexHeaderAsync(context);
            await base.OnActionExecutionAsync(context, next);
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.RequestContext;

            fhirRequestContext.RouteName = context.ActionDescriptor?.AttributeRouteInfo?.Name;

            // Set the resource type based on the route data
            RouteData routeData = context.RouteData;

            if (routeData?.Values != null)
            {
                if (routeData.Values.TryGetValue(KnownActionParameterNames.ResourceType, out object resourceType))
                {
                    fhirRequestContext.ResourceType = resourceType?.ToString();
                }
            }

            if (context.ActionDescriptor is ControllerActionDescriptor controllerActionDescriptor)
            {
                // if controllerActionDescriptor.ActionName is CustomError then retain the AuditEventType from previous context
                // e.g. In case of 500 error - we want to make sure we log the AuditEventType of the original request for which the error occurred in RequestMetric.
                fhirRequestContext.AuditEventType = KnownRoutes.CustomError.Contains(controllerActionDescriptor.ActionName, StringComparison.OrdinalIgnoreCase) ? fhirRequestContext.AuditEventType : _auditEventTypeMapping.GetAuditEventType(
                    controllerActionDescriptor.ControllerName,
                    controllerActionDescriptor.ActionName);

                // If this is a request from the batch and transaction route, we need to examine the payload to set the AuditEventType
                if (fhirRequestContext.AuditEventType == AuditEventSubType.BundlePost)
                {
                    if (context.ActionArguments.TryGetValue(KnownActionParameterNames.Bundle, out object value))
                    {
                        if (!(value is Hl7.Fhir.Model.Bundle bundle))
                        {
                            return;
                        }

                        try
                        {
                            // bundle.Type can raise InvalidCastException if the incoming value is not a valid BundleType.
                            switch (bundle.Type)
                            {
                                case Hl7.Fhir.Model.Bundle.BundleType.Batch:
                                    fhirRequestContext.AuditEventType = AuditEventSubType.Batch;
                                    break;
                                case Hl7.Fhir.Model.Bundle.BundleType.Transaction:
                                    fhirRequestContext.AuditEventType = AuditEventSubType.Transaction;
                                    break;
                            }
                        }
                        catch (InvalidCastException)
                        {
                            // I had to add the 'timer' to the Http context, as the filter Microsoft.Health.Api.Features.Audit.AuditLoggingFilterAttribute expected it to be there.
                            // This change avoid a null reference exception to happen.
                            // The correct implementation would be handling the absence of the 'timer' in AuditLoggingFilterAttribute.
                            context.HttpContext.Items["timer"] = Stopwatch.StartNew();

                            context.Result = new OperationOutcomeResult(
                                new OperationOutcome
                                {
                                    Id = fhirRequestContext.CorrelationId,
                                    Issue =
                                    {
                                        new OperationOutcome.IssueComponent
                                        {
                                            Severity = OperationOutcome.IssueSeverity.Error,
                                            Code = OperationOutcome.IssueType.Invalid,
                                            Diagnostics = Core.Resources.UnsupportedBundleType,
                                        },
                                    },
                                },
                                HttpStatusCode.BadRequest,
                                _logger);
                            return;
                        }
                    }
                }
            }

            base.OnActionExecuting(context);
        }

        /// <summary>
        /// Honors <see cref="KnownHeaders.PartiallyIndexedParamsHeaderName"/> only for callers who can reindex.
        /// Presence alone is not sufficient: a patient-scoped caller could otherwise match stale custom-search rows
        /// after a SearchParameter expression is narrowed. The header is removed when denied because the Cosmos sort
        /// validator reads it directly from the request headers.
        /// </summary>
        private async Task ApplyPartialIndexHeaderAsync(ActionExecutingContext context)
        {
            if (!context.HttpContext.Request.Headers.ContainsKey(KnownHeaders.PartiallyIndexedParamsHeaderName))
            {
                return;
            }

            IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.RequestContext;
            if (await CanUsePartialIndicesAsync(fhirRequestContext, context.HttpContext.RequestAborted))
            {
                fhirRequestContext.IncludePartiallyIndexedSearchParams = true;
                return;
            }

            fhirRequestContext.IncludePartiallyIndexedSearchParams = false;
            context.HttpContext.Request.Headers.Remove(KnownHeaders.PartiallyIndexedParamsHeaderName);
            if (fhirRequestContext.RequestHeaders != null
                && !ReferenceEquals(fhirRequestContext.RequestHeaders, context.HttpContext.Request.Headers))
            {
                fhirRequestContext.RequestHeaders.Remove(KnownHeaders.PartiallyIndexedParamsHeaderName);
            }

            _logger.LogDebug(
                "Ignored {Header} because the caller is not permitted to reindex.",
                KnownHeaders.PartiallyIndexedParamsHeaderName);
        }

        private async System.Threading.Tasks.Task<bool> CanUsePartialIndicesAsync(IFhirRequestContext fhirRequestContext, CancellationToken cancellationToken)
        {
            // Authorization-disabled hosts register DisabledFhirAuthorizationService, which permits every action.
            if (!_securityConfiguration.Enabled || _securityConfiguration.Authorization?.Enabled != true)
            {
                return true;
            }

            // RoleBasedFhirAuthorizationService dereferences Principal. A missing principal is not a reindex caller.
            if (fhirRequestContext?.Principal == null)
            {
                return false;
            }

            return await _authorizationService.CheckAccess(DataActions.Reindex, throwException: false, cancellationToken);
        }
    }
}
