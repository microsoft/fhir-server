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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Api.Features.ApiNotifications
{
    public class ApiNotificationMiddleware : IMiddleware
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IMediator _mediator;
        private readonly ILogger<ApiNotificationMiddleware> _logger;

        public ApiNotificationMiddleware(
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IMediator mediator,
            ILogger<ApiNotificationMiddleware> logger)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _mediator = mediator;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            EnsureArg.IsNotNull(context, nameof(context));
            EnsureArg.IsNotNull(next, nameof(next));

            if (!context.Request.IsFhirRequest())
            {
                // Don't emit events for internal calls.
                await next(context);
                return;
            }

            await PublishNotificationAsync(context, next);
        }

        protected virtual async Task PublishNotificationAsync(HttpContext context, RequestDelegate next)
        {
            var apiNotification = new ApiResponseNotification();

            using ActionTimer timer = _logger.BeginTimedScope(nameof(ApiNotificationMiddleware));
            try
            {
                await next(context);
            }
            finally
            {
                apiNotification.Latency = timer.ElapsedTime;

                try
                {
                    IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.RequestContext;

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
