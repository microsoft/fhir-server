// -------------------------------------------------------------------------------------------------
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
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Exceptions
{
    public class BaseExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<BaseExceptionMiddleware> _logger;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly CorrelationIdProvider _correlationIdProvider;
        private readonly IContentTypeService _contentTypeService;

        public BaseExceptionMiddleware(
            RequestDelegate next,
            ILogger<BaseExceptionMiddleware> logger,
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            CorrelationIdProvider correlationIdProvider,
            IContentTypeService contentTypeService)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(correlationIdProvider, nameof(correlationIdProvider));
            EnsureArg.IsNotNull(contentTypeService, nameof(contentTypeService));

            _next = next;
            _logger = logger;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _correlationIdProvider = correlationIdProvider;
            _contentTypeService = contentTypeService;
        }

        public async Task Invoke(HttpContext context)
        {
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

                var localCorrelationId = _fhirRequestContextAccessor.FhirRequestContext?.CorrelationId;

                Debug.Assert(!string.IsNullOrWhiteSpace(localCorrelationId), "The correlation id should have been generated.");

                context.Response.Clear();

                var diagnostics = Resources.GeneralInternalError;

                // If any of these exceptions are encountered, show a more specific diagnostic message
                if (exception.Message.StartsWith("IDX10803: Unable to obtain configuration from:", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics = Resources.UnableToObtainOpenIdConfiguration;
                }
                else if (exception.Message.StartsWith("The MetadataAddress or Authority must use HTTPS", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics = Resources.RequireHttpsMetadataError;
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
                    await _contentTypeService.CheckRequestedContentTypeAsync(context);
                }
                catch (UnsupportedMediaTypeException)
                {
                    context.Response.ContentType = KnownContentTypes.JsonContentType;
                }

                var result = new OperationOutcomeResult(operationOutcome, HttpStatusCode.InternalServerError);

                await ExecuteResultAsync(context, result);
            }
        }

        protected internal virtual async Task ExecuteResultAsync(HttpContext context, IActionResult result)
        {
            await result.ExecuteResultAsync(new ActionContext { HttpContext = context });
        }
    }
}
