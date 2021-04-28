// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using EnsureThat;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Extensions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.ExceptionNotifications
{
    public class ExceptionNotificationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionNotificationMiddleware> _logger;
        private readonly IMediator _mediator;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;

        public ExceptionNotificationMiddleware(
            RequestDelegate next,
            ILogger<ExceptionNotificationMiddleware> logger,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IMediator mediator)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            _next = next;
            _logger = logger;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _mediator = mediator;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception exception)
            {
                var exceptionNotification = new ExceptionNotification();

                try
                {
                    IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.RequestContext;
                    var innerMostException = exception.GetInnerMostException();

                    exceptionNotification.CorrelationId = fhirRequestContext?.CorrelationId;
                    exceptionNotification.FhirOperation = fhirRequestContext?.AuditEventType;
                    exceptionNotification.OuterExceptionType = exception.GetType().ToString();
                    exceptionNotification.ResourceType = fhirRequestContext?.ResourceType;
                    exceptionNotification.StatusCode = (HttpStatusCode)context.Response.StatusCode;
                    exceptionNotification.ExceptionMessage = exception.Message;
                    exceptionNotification.StackTrace = exception.StackTrace;
                    exceptionNotification.InnerMostExceptionType = innerMostException.GetType().ToString();
                    exceptionNotification.InnerMostExceptionMessage = innerMostException.Message;
                    exceptionNotification.HResult = exception.HResult;
                    exceptionNotification.Source = exception.Source;
                    exceptionNotification.OuterMethod = exception.TargetSite?.Name;
                    exceptionNotification.IsRequestEntityTooLarge = exception.IsRequestEntityTooLarge();
                    exceptionNotification.IsRequestRateExceeded = exception.IsRequestRateExceeded();

                    await _mediator.Publish(exceptionNotification, CancellationToken.None);
                }
                catch (Exception e)
                {
                    // Failures in publishing exception notifications should not cause the API to return an error.
                    _logger.LogWarning(e, "Failure while publishing Exception notification.");
                }

                // Rethrowing the exception so the BaseExceptionMiddleware can handle it. We are only notifying there is an exception at this point.
                throw;
            }
        }
    }
}
