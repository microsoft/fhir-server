﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Net.Http.Headers;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Formatters
{
    /// <summary>
    /// Checks format parameters defined for all REST API in FHIR Server.
    /// </summary>
    /// <remarks>
    /// Format parameters defined here: http://hl7.org/fhir/http.html#parameters
    /// </remarks>
    public sealed class FormatParametersValidator : IFormatParametersValidator
    {
        private readonly IConformanceProvider _conformanceProvider;
        private readonly ICollection<TextOutputFormatter> _outputFormatters;
        private readonly ConcurrentDictionary<ResourceFormat, bool> _supportedFormats = new ConcurrentDictionary<ResourceFormat, bool>();
        private readonly ConcurrentDictionary<string, bool> _supportedPatchFormats = new ConcurrentDictionary<string, bool>();

        public FormatParametersValidator(
            IConformanceProvider conformanceProvider,
            IEnumerable<TextOutputFormatter> outputFormatters)
        {
            EnsureArg.IsNotNull(conformanceProvider, nameof(conformanceProvider));
            EnsureArg.IsNotNull(outputFormatters, nameof(outputFormatters));

            _conformanceProvider = conformanceProvider;
            _outputFormatters = outputFormatters.ToArray();
        }

        public async Task CheckRequestedContentTypeAsync(HttpContext httpContext)
        {
            var acceptHeaders = httpContext.Request.GetTypedHeaders().Accept;
            string formatOverride = GetParameterValueFromQueryString(httpContext, KnownQueryParameterNames.Format);

            // Check the _format first since it takes precedence over the accept header.
            if (!string.IsNullOrEmpty(formatOverride))
            {
                ResourceFormat resourceFormat = ContentType.GetResourceFormatFromFormatParam(formatOverride);
                if (!await IsFormatSupportedAsync(resourceFormat))
                {
                    throw new NotAcceptableException(Api.Resources.UnsupportedFormatParameter);
                }

                string closestClientMediaType = _outputFormatters.GetClosestClientMediaType(resourceFormat.ToContentType(), acceptHeaders?.Select(x => x.MediaType.Value));

                // Overrides output format type
                httpContext.Response.ContentType = closestClientMediaType;
            }
            else
            {
                if (acceptHeaders?.Any() == true && acceptHeaders.All(a => a.MediaType != "*/*"))
                {
                    var isAcceptHeaderValid = false;

                    foreach (MediaTypeHeaderValue acceptHeader in acceptHeaders)
                    {
                        var headerValue = acceptHeader.MediaType.ToString();
                        isAcceptHeaderValid = await IsFormatSupportedAsync(headerValue);

                        if (isAcceptHeaderValid)
                        {
                            break;
                        }
                    }

                    if (!isAcceptHeaderValid)
                    {
                        throw new NotAcceptableException(string.Format(Api.Resources.UnsupportedHeaderValue, HeaderNames.Accept));
                    }
                }
            }
        }

        private static string GetParameterValueFromQueryString(HttpContext context, string parameterName)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            // If executing in a rethrown error context, ensure we carry the specified query string value.
            var previous = context.Features.Get<IStatusCodeReExecuteFeature>()?.OriginalQueryString;
            var previousQuery = QueryHelpers.ParseNullableQuery(previous);

            if (previousQuery != null && previousQuery.TryGetValue(parameterName, out var originValues) == true)
            {
                return originValues.FirstOrDefault();
            }

            // Check the current query string.
            if (context.Request.Query.TryGetValue(parameterName, out var queryValues))
            {
                return queryValues.FirstOrDefault();
            }

            return null;
        }

        public async Task<bool> IsFormatSupportedAsync(string format)
        {
            ResourceFormat resourceFormat = ContentType.GetResourceFormatFromContentType(format);

            return await IsFormatSupportedAsync(resourceFormat);
        }

        private async Task<bool> IsFormatSupportedAsync(ResourceFormat resourceFormat)
        {
            if (_supportedFormats.TryGetValue(resourceFormat, out var isSupported))
            {
                return isSupported;
            }

            ResourceElement typedStatement = await _conformanceProvider.GetCapabilityStatementOnStartup();

            IEnumerable<string> formats = typedStatement.Select("CapabilityStatement.format").Select(x => (string)x.Value);

            return _supportedFormats.GetOrAdd(resourceFormat, format =>
            {
                switch (resourceFormat)
                {
                    case ResourceFormat.Json:
                        return formats.Any(f => f.Contains("json", StringComparison.Ordinal));

                    case ResourceFormat.Xml:
                        return formats.Any(f => f.Contains("xml", StringComparison.Ordinal));

                    default:
                        return false;
                }
            });
        }

        public async Task<bool> IsPatchFormatSupportedAsync(string format)
        {
            if (_supportedPatchFormats.TryGetValue(format, out var isSupported))
            {
                return isSupported;
            }

            ResourceElement typedStatement = await _conformanceProvider.GetCapabilityStatementOnStartup();

            IEnumerable<string> formats = typedStatement.Select("CapabilityStatement.patchFormat").Select(x => (string)x.Value);

            return _supportedPatchFormats.GetOrAdd(format, format =>
            {
                return formats.Any(f => format.Contains(f, StringComparison.Ordinal));
            });
        }

        public void CheckPrettyParameter(HttpContext httpContext)
        {
            var prettyParameterValue = GetParameterValueFromQueryString(httpContext, KnownQueryParameterNames.Pretty);

            if (prettyParameterValue != null && !bool.TryParse(prettyParameterValue, out _))
            {
                throw new BadRequestException(Api.Resources.InvalidPrettyParameter);
            }
        }

        public void CheckSummaryParameter(HttpContext httpContext)
        {
            var summaryParameterValue = GetParameterValueFromQueryString(httpContext, KnownQueryParameterNames.Summary);

            if (summaryParameterValue != null && !Enum.TryParse<SummaryType>(summaryParameterValue, true, out _))
            {
                throw new BadRequestException(string.Format(Api.Resources.InvalidSummaryParameter, summaryParameterValue, string.Join(',', Enum.GetNames<SummaryType>())));
            }
        }

        public void CheckElementsParameter(HttpContext httpContext)
        {
            var elementsParameterValue = GetParameterValueFromQueryString(httpContext, KnownQueryParameterNames.Elements);

            // It's ok to not have parameter, but it shouldn't be empty or whitespace.
            if (elementsParameterValue != null && string.IsNullOrWhiteSpace(elementsParameterValue))
            {
                throw new BadRequestException(string.Format(Api.Resources.InvalidElementsParameter, elementsParameterValue));
            }
        }
    }
}
