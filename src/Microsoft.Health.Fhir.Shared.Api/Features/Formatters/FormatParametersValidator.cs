// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
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
        private readonly IEnumerable<TextOutputFormatter> _outputFormatters;
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

                string closestClientMediaType = _outputFormatters.GetClosestClientMediaType(resourceFormat.ToContentType(), acceptHeaders?.Select(x => x.MediaType.Value)?.ToList());

                // Overrides output format type
                httpContext.Response.ContentType = closestClientMediaType;
            }
            else
            {
                if (acceptHeaders?.Any() == true)
                {
                    var orderedAcceptHeaders = acceptHeaders
                        .Where(a => (a.Quality ?? 1.0) > 0)
                        .OrderByDescending(a => a.Quality ?? 1.0)
                        .ToList();

                    string matchedMediaType = null;
                    MediaTypeHeaderValue matchedHeader = null;
                    Encoding resolvedEncoding = null;
                    bool matchedWildcard = false;
                    bool skippedUnsupportedCharset = false;

                    foreach (MediaTypeHeaderValue acceptHeader in orderedAcceptHeaders)
                    {
                        if (acceptHeader.MediaType == "*/*")
                        {
                            matchedWildcard = true;
                            break;
                        }

                        var headerValue = acceptHeader.MediaType.ToString();
                        if (await IsFormatSupportedAsync(headerValue))
                        {
                            if (!TryResolveSupportedEncoding(headerValue, acceptHeader.Charset, out Encoding candidateEncoding))
                            {
                                skippedUnsupportedCharset = true;
                                continue;
                            }

                            matchedMediaType = headerValue;
                            matchedHeader = acceptHeader;
                            resolvedEncoding = candidateEncoding;
                            break;
                        }
                    }

                    if (matchedMediaType == null && !matchedWildcard)
                    {
                        var headerValue = string.Join(',', acceptHeaders.Select(x => x.MediaType.Value));
                        throw new NotAcceptableException(string.Format(Api.Resources.UnsupportedHeaderValue, headerValue, HeaderNames.Accept));
                    }

                    if (matchedMediaType != null)
                    {
                        // When the matched Accept entry contains non-quality parameters (e.g., charset=UTF-8)
                        // or a higher-priority entry was skipped due to an unsupported charset, ASP.NET Core's
                        // content negotiation may fail or pick the skipped formatter. Explicitly set the compatible
                        // content type and validated charset so the correct formatter and encoding are used.
                        bool hasNonQualityParams = matchedHeader.Parameters.Any(
                            p => !string.Equals(p.Name.Value, "q", StringComparison.OrdinalIgnoreCase));

                        if (hasNonQualityParams || skippedUnsupportedCharset)
                        {
                            var compatibleMediaTypes = orderedAcceptHeaders
                                .Where(x => x.MediaType != "*/*" && TryResolveSupportedEncoding(x.MediaType.Value, x.Charset, out _))
                                .Select(x => x.MediaType.Value)
                                .ToList();

                            string closestClientMediaType = _outputFormatters.GetClosestClientMediaType(
                                matchedMediaType,
                                compatibleMediaTypes);

                            if (!string.IsNullOrEmpty(closestClientMediaType))
                            {
                                httpContext.Response.ContentType = resolvedEncoding != null
                                    ? new MediaTypeHeaderValue(closestClientMediaType) { Charset = resolvedEncoding.WebName }.ToString()
                                    : closestClientMediaType;
                            }
                        }
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

        private bool TryResolveSupportedEncoding(string mediaType, StringSegment requestedCharset, out Encoding resolvedEncoding)
        {
            string charset = HeaderUtilities.RemoveQuotes(requestedCharset).Value;
            if (string.IsNullOrEmpty(charset))
            {
                resolvedEncoding = null;
                return true;
            }

            var candidateFormatters = _outputFormatters
                .Where(f =>
                    f.GetSupportedContentTypes(mediaType, typeof(Hl7.Fhir.Model.Resource)) != null ||
                    f.SupportedMediaTypes.Any(m => string.Equals(m, mediaType, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            Encoding matched = candidateFormatters
                .SelectMany(f => f.SupportedEncodings)
                .FirstOrDefault(e => string.Equals(e.WebName, charset, StringComparison.OrdinalIgnoreCase));

            if (matched != null)
            {
                resolvedEncoding = matched;
                return true;
            }

            if (candidateFormatters.Any(f => f.SupportedEncodings.Count > 0))
            {
                resolvedEncoding = null;
                return false;
            }

            try
            {
                resolvedEncoding = Encoding.GetEncoding(charset);
                return true;
            }
            catch (ArgumentException)
            {
                resolvedEncoding = null;
                return false;
            }
        }
    }
}
