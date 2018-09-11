// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Exceptions
{
    public class BaseExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<BaseExceptionMiddleware> _logger;
        private readonly IFhirContextAccessor _fhirContextAccessor;
        private readonly FhirJsonSerializer _fhirJsonSerializer;
        private readonly FhirXmlSerializer _fhirXmlSerializer;
        private readonly CorrelationIdProvider _correlationIdProvider;

        public BaseExceptionMiddleware(
            RequestDelegate next,
            ILogger<BaseExceptionMiddleware> logger,
            IFhirContextAccessor fhirContextAccessor,
            FhirJsonSerializer fhirJsonSerializer,
            FhirXmlSerializer fhirXmlSerializer,
            CorrelationIdProvider correlationIdProvider)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(fhirContextAccessor, nameof(fhirContextAccessor));
            EnsureArg.IsNotNull(fhirJsonSerializer, nameof(fhirJsonSerializer));
            EnsureArg.IsNotNull(fhirXmlSerializer, nameof(fhirXmlSerializer));
            EnsureArg.IsNotNull(correlationIdProvider, nameof(correlationIdProvider));

            _next = next;
            _logger = logger;
            _fhirContextAccessor = fhirContextAccessor;
            _fhirJsonSerializer = fhirJsonSerializer;
            _fhirXmlSerializer = fhirXmlSerializer;
            _correlationIdProvider = correlationIdProvider;
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

                var localCorrelationId = _fhirContextAccessor.FhirContext?.CorrelationId;

                if (string.IsNullOrWhiteSpace(localCorrelationId))
                {
                    localCorrelationId = _correlationIdProvider.Invoke();
                    _logger.LogError($"No correlation id available in exception middleware. Setting to {localCorrelationId}");
                }

                context.Response.Clear();
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

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

                var contentType = GetResourceFormatFromContentType(context);

                switch (contentType)
                {
                    case ResourceFormat.Xml:
                        context.Response.ContentType = ContentType.XML_CONTENT_HEADER;
                        await context.Response.WriteAsync(_fhirXmlSerializer.SerializeToString(operationOutcome));
                        break;
                    default:
                        context.Response.ContentType = ContentType.JSON_CONTENT_HEADER;
                        await context.Response.WriteAsync(_fhirJsonSerializer.SerializeToString(operationOutcome));
                        break;
                }
            }
        }

        private static ResourceFormat GetResourceFormatFromContentType(HttpContext context)
        {
            try
            {
                return ContentType.GetResourceFormatFromContentType(context.Request.ContentType ?? context.Request.Headers["Accept"]);
            }
            catch
            {
                return ResourceFormat.Unknown;
            }
        }
    }
}
