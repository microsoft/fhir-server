﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Extensions;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Exceptions
{
    public class BaseExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<BaseExceptionMiddleware> _logger;
        private readonly IFailureMetricHandler _failureMetricHandler;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IFormatParametersValidator _parametersValidator;

        public BaseExceptionMiddleware(
            RequestDelegate next,
            ILogger<BaseExceptionMiddleware> logger,
            IFailureMetricHandler failureMetricHandler,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IFormatParametersValidator parametersValidator)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(failureMetricHandler, nameof(failureMetricHandler));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(parametersValidator, nameof(parametersValidator));

            _next = next;
            _logger = logger;
            _failureMetricHandler = failureMetricHandler;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _parametersValidator = parametersValidator;
        }

        public async Task Invoke(HttpContext context)
        {
            bool doesOperationOutcomeHaveError = false;
            try
            {
                await _next(context);
            }
            catch (Exception exception)
            {
                if (context.Response.HasStarted)
                {
                    _logger.LogWarning("The response has already started, the base exception middleware will not be executed.");
                    throw;
                }

                var localCorrelationId = _fhirRequestContextAccessor.RequestContext?.CorrelationId;

                Debug.Assert(!string.IsNullOrWhiteSpace(localCorrelationId), "The correlation id should have been generated.");

                context.Response.Clear();

                var diagnostics = Api.Resources.GeneralInternalError;

                // If any of these exceptions are encountered, show a more specific diagnostic message
                if (exception.Message.StartsWith("IDX10803: Unable to obtain configuration from:", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics = Api.Resources.UnableToObtainOpenIdConfiguration;
                }
                else if (exception.Message.StartsWith("The MetadataAddress or Authority must use HTTPS", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics = Api.Resources.RequireHttpsMetadataError;
                }

                var operationOutcome = new OperationOutcome
                {
                    Id = localCorrelationId,
                    Issue = new List<OperationOutcome.IssueComponent>
                    {
                        new OperationOutcome.IssueComponent
                        {
                            Severity = OperationOutcome.IssueSeverity.Fatal,
                            Code = OperationOutcome.IssueType.Exception,
                            Diagnostics = diagnostics,
                        },
                    },
                };

                try
                {
                    await _parametersValidator.CheckRequestedContentTypeAsync(context);
                }
                catch (UnsupportedMediaTypeException)
                {
                    context.Response.ContentType = KnownContentTypes.JsonContentType;
                }

                var result = new OperationOutcomeResult(
                    operationOutcome,
                    exception is ServiceUnavailableException ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.InternalServerError);

                doesOperationOutcomeHaveError = true;

                await ExecuteResultAsync(context, result);
            }
            finally
            {
                EmitHttpFailureMetricInCaseOfError(context, doesOperationOutcomeHaveError);
            }
        }

        protected internal virtual async Task ExecuteResultAsync(HttpContext context, IActionResult result)
        {
            await result.ExecuteResultAsync(new ActionContext { HttpContext = context });
        }

        private void EmitHttpFailureMetricInCaseOfError(HttpContext context, bool doesOperationOutcomeHaveError)
        {
            if (context.Response.StatusCode >= 500 || doesOperationOutcomeHaveError)
            {
                string operationName = context.Request.GetOperationName(includeRouteValues: false);

                _failureMetricHandler.EmitHttpFailure(
                    new HttpErrorMetricNotification() { OperationName = operationName });
            }
        }
    }
}
