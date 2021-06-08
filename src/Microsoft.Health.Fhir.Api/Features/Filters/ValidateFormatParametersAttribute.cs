// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Net.Http.Headers;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    /// <summary>
    /// Validate format related parameters in query string and headers.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class ValidateFormatParametersAttribute : ActionFilterAttribute
    {
        private readonly IFormatParametersValidator _parametersValidator;

        public ValidateFormatParametersAttribute(IFormatParametersValidator parametersValidator)
        {
            EnsureArg.IsNotNull(parametersValidator, nameof(parametersValidator));

            _parametersValidator = parametersValidator;
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            HttpContext httpContext = context.HttpContext;

            _parametersValidator.CheckPrettyParameter(httpContext);
            _parametersValidator.CheckSummaryParameter(httpContext);
            _parametersValidator.CheckElementsParameter(httpContext);
            await _parametersValidator.CheckRequestedContentTypeAsync(httpContext);

            // If the request is a put or post and has a content-type, check that it's supported
            if (httpContext.Request.Method.Equals(HttpMethod.Post.Method, StringComparison.OrdinalIgnoreCase) ||
                httpContext.Request.Method.Equals(HttpMethod.Put.Method, StringComparison.OrdinalIgnoreCase))
            {
                if (httpContext.Request.Headers.TryGetValue(HeaderNames.ContentType, out StringValues headerValue))
                {
                    if (!await _parametersValidator.IsFormatSupportedAsync(headerValue[0]))
                    {
                        throw new UnsupportedMediaTypeException(string.Format(Resources.UnsupportedHeaderValue, HeaderNames.ContentType));
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
    }
}
