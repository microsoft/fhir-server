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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Api.Features.ApiNotifications
{
    public class ApiNotificationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IMediator _mediator;
        private readonly ILogger<ApiNotificationMiddleware> _logger;

        public ApiNotificationMiddleware(
            RequestDelegate next,
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IMediator mediator,
            ILogger<ApiNotificationMiddleware> logger)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _next = next;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _mediator = mediator;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            if (context.Request.Path.HasValue && context.Request.Path.StartsWithSegments(FhirServerApplicationBuilderExtensions.HealthCheckPath, System.StringComparison.InvariantCultureIgnoreCase))
            {
                // Don't emit events for health check

                await _next(context);
                return;
            }

            var apiNotification = new ApiResponseNotification();

            using (var timer = _logger.BeginTimedScope("ApiNotificationMiddleware") as ActionTimer)
            {
                try
                {
                    await _next(context);
                }
                finally
                {
                    apiNotification.Latency = timer.GetElapsedTime();

                    try
                    {
                        IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.FhirRequestContext;

                        // For now, we will only emit metrics for audited actions (e.g., metadata will not emit metrics).
                        if (fhirRequestContext?.AuditEventType != null)
                        {
                            apiNotification.Authentication = fhirRequestContext.Principal?.Identity.AuthenticationType;
                            apiNotification.FhirOperation = fhirRequestContext.AuditEventType;
                            apiNotification.Protocol = context.Request.Scheme;
                            apiNotification.ResourceType = fhirRequestContext.ResourceType;
                            apiNotification.StatusCode = (HttpStatusCode)context.Response.StatusCode;

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
