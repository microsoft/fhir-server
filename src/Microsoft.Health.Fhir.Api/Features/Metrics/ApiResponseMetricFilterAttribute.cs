// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Api.Features.ApiNotifications;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Api.Features.Metrics
{
    internal class ApiResponseMetricFilterAttribute : IAsyncActionFilter
    {
        private readonly IMediator _mediator;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly ILogger<ApiResponseMetricFilterAttribute> _logger;

        public ApiResponseMetricFilterAttribute(IMediator mediator, IFhirRequestContextAccessor fhirRequestContextAccessor, ILogger<ApiResponseMetricFilterAttribute> logger)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _mediator = mediator;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _logger = logger;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            using (var timer = _logger.BeginTimedScope("ApiNotificationMiddleware") as ActionTimer)
            {
                try
                {
                    await next();
                }
                finally
                {
                    var apiNotification = new ApiResponseMetricNotification
                    {
                        Latency = timer.GetElapsedTime(),
                    };

                    try
                    {
                        IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.FhirRequestContext;

                        // For now, we will only emit metrics for audited actions (e.g., metadata will not emit metrics).
                        if (fhirRequestContext?.AuditEventType != null)
                        {
                            apiNotification.Authentication = fhirRequestContext.Principal?.Identity.AuthenticationType;
                            apiNotification.FhirOperation = fhirRequestContext.AuditEventType;
                            apiNotification.Protocol = context.HttpContext.Request.Scheme;
                            apiNotification.ResourceType = fhirRequestContext.ResourceType;
                            apiNotification.StatusCode = (HttpStatusCode)context.HttpContext.Response.StatusCode;

                            await _mediator.Publish(apiNotification, CancellationToken.None);
                        }
                    }
                    catch (Exception e)
                    {
                        // Failures in publishing API notifications should not cause the API to return an error.
                        _logger.LogCritical(e, "Failure while publishing API notification.");
                    }
                }
            }
        }
    }
}
