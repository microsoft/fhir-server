// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Net.Http.Headers;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    internal class ValidateContentTypeFilterAttribute : ActionFilterAttribute
    {
        private readonly IConformanceProvider _conformanceProvider;

        public ValidateContentTypeFilterAttribute(IConformanceProvider conformanceProvider)
        {
            EnsureArg.IsNotNull(conformanceProvider, nameof(conformanceProvider));

            _conformanceProvider = conformanceProvider;
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            // Check the _format first since it takes precedence over the accept header.
            var format = GetSpecifiedFormat(context);

            if (!string.IsNullOrEmpty(format))
            {
                var resourceFormat = ContentType.GetResourceFormatFromFormatParam(format);
                if (!await IsFormatSupported(resourceFormat))
                {
                    throw new UnsupportedMediaTypeException(Resources.UnsupportedFormatParameter);
                }

                // Overrides output format type
                context.HttpContext.Response.ContentType = ContentType.BuildContentType(resourceFormat, true);
            }
            else
            {
                var acceptHeaders = context.HttpContext.Request.GetTypedHeaders().Accept;
                if (acceptHeaders != null && acceptHeaders.All(a => a.MediaType != "*/*"))
                {
                    var isAcceptHeaderValid = false;

                    foreach (var acceptHeader in acceptHeaders)
                    {
                        var resourceFormat = ContentType.GetResourceFormatFromContentType(acceptHeader.MediaType.ToString());

                        isAcceptHeaderValid = isAcceptHeaderValid || await IsFormatSupported(resourceFormat);
                    }

                    if (!isAcceptHeaderValid)
                    {
                        throw new UnsupportedMediaTypeException(Resources.UnsupportedAcceptHeader);
                    }
                }
            }

            // If the request is a put or post and has a content-type, check that it's supported
            if (context.HttpContext.Request.Method.Equals(HttpMethod.Post.Method, StringComparison.InvariantCultureIgnoreCase) ||
                context.HttpContext.Request.Method.Equals(HttpMethod.Put.Method, StringComparison.InvariantCultureIgnoreCase))
            {
                if (context.HttpContext.Request.Headers.TryGetValue(HeaderNames.ContentType, out StringValues headerValue))
                {
                    var resourceFormat = ContentType.GetResourceFormatFromContentType(headerValue[0]);

                    if (!await IsFormatSupported(resourceFormat) && context.ActionDescriptor?.AttributeRouteInfo?.Name != RouteNames.SearchResourcesPost)
                    {
                        throw new UnsupportedMediaTypeException(Resources.UnsupportedContentTypeHeader);
                    }
                }
                else
                {
                    // If no content type is supplied, then the server should respond with an unsupported media type exception.
                    throw new UnsupportedMediaTypeException(Resources.ContentTypeHeaderRequired);
                }
            }

            await base.OnActionExecutionAsync(context, next);
        }

        private static string GetSpecifiedFormat(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            // If executing in a rethrown error context, ensure we carry the specified format
            var previous = context.HttpContext.Features.Get<IStatusCodeReExecuteFeature>()?.OriginalQueryString;
            var previousQuery = QueryHelpers.ParseNullableQuery(previous);

            if (previousQuery?.TryGetValue(KnownQueryParameterNames.Format, out var originFormatValues) == true)
            {
                return originFormatValues.FirstOrDefault();
            }

            // Check the current query string
            if (context.HttpContext.Request.Query.TryGetValue(KnownQueryParameterNames.Format, out var queryValues))
            {
                return queryValues.FirstOrDefault();
            }

            return null;
        }

        private async Task<bool> IsFormatSupported(ResourceFormat resourceFormat)
        {
            var statement = await _conformanceProvider.GetCapabilityStatementAsync();

            switch (resourceFormat)
            {
                case ResourceFormat.Json:
                    return statement.Format.Any(f => f.Contains("json", StringComparison.Ordinal));

                case ResourceFormat.Xml:
                    return statement.Format.Any(f => f.Contains("xml", StringComparison.Ordinal));

                default:
                    return false;
            }
        }
    }
}
