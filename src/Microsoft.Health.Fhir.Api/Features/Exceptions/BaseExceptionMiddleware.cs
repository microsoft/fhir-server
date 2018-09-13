// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Exceptions
{
    public class BaseExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<BaseExceptionMiddleware> _logger;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly FhirJsonSerializer _fhirJsonSerializer;
        private readonly FhirXmlSerializer _fhirXmlSerializer;
        private readonly CorrelationIdProvider _correlationIdProvider;
        private readonly IEnumerable<TextOutputFormatter> _outputFormatters;

        public BaseExceptionMiddleware(
            RequestDelegate next,
            ILogger<BaseExceptionMiddleware> logger,
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            FhirJsonSerializer fhirJsonSerializer,
            FhirXmlSerializer fhirXmlSerializer,
            CorrelationIdProvider correlationIdProvider,
            IEnumerable<TextOutputFormatter> outputFormatters)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(fhirJsonSerializer, nameof(fhirJsonSerializer));
            EnsureArg.IsNotNull(fhirXmlSerializer, nameof(fhirXmlSerializer));
            EnsureArg.IsNotNull(correlationIdProvider, nameof(correlationIdProvider));
            EnsureArg.IsNotNull(outputFormatters, nameof(outputFormatters));

            _next = next;
            _logger = logger;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _fhirJsonSerializer = fhirJsonSerializer;
            _fhirXmlSerializer = fhirXmlSerializer;
            _correlationIdProvider = correlationIdProvider;
            _outputFormatters = outputFormatters;
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
                context.Response.ContentType = contentType.MediaType;

                switch (contentType.Format)
                {
                    case ResourceFormat.Xml:
                        await context.Response.WriteAsync(_fhirXmlSerializer.SerializeToString(operationOutcome));
                        break;
                    default:
                        context.Response.ContentType = ContentType.JSON_CONTENT_HEADER;
                        await context.Response.WriteAsync(_fhirJsonSerializer.SerializeToString(operationOutcome));
                        break;
                }
            }
        }

        private (string MediaType, ResourceFormat Format) GetResourceFormatFromContentType(HttpContext context)
        {
            var defaultFormat = (ContentType.JSON_CONTENT_HEADER, ResourceFormat.Unknown);
            try
            {
                var acceptHeaders = context.Request.GetTypedHeaders().Accept.Select(x => x.MediaType.Value);

                // Check _format override
                if (context.Request.Query.TryGetValue(KnownQueryParameterNames.Format, out var queryValues) && queryValues.Any())
                {
                    ResourceFormat resourceFormat = ContentType.GetResourceFormatFromFormatParam(queryValues.First());
                    return (_outputFormatters.GetClosestClientMediaType(resourceFormat, acceptHeaders), resourceFormat);
                }

                // Check headers
                var acceptList = new[] { context.Request.ContentType }
                    .Concat(acceptHeaders)
                    .Where(x => x != null);

                foreach (var header in acceptList)
                {
                    var format = ContentType.GetResourceFormatFromContentType(header);
                    if (format != ResourceFormat.Unknown)
                    {
                        return (header, format);
                    }
                }

                return defaultFormat;
            }
            catch
            {
                return defaultFormat;
            }
        }
    }
}
