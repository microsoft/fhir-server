// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using EnsureThat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Api.Features.Exceptions;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    /// <summary>
    /// Exception filter for OAuth2 endpoints that returns RFC 6749 compliant error responses.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class OAuth2ExceptionFilterAttribute : ActionFilterAttribute
    {
        private readonly ILogger<OAuth2ExceptionFilterAttribute> _logger;

        public OAuth2ExceptionFilterAttribute(ILogger<OAuth2ExceptionFilterAttribute> logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));
            _logger = logger;
        }

        public override void OnActionExecuted(ActionExecutedContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (context.Exception == null || context.ExceptionHandled)
            {
                return;
            }

            if (context.Exception is OAuth2BadRequestException oauth2Exception)
            {
                _logger.LogWarning(
                    oauth2Exception,
                    "OAuth2 bad request: {Error} - {ErrorDescription}",
                    oauth2Exception.Error,
                    oauth2Exception.ErrorDescription);

                context.Result = CreateOAuth2ErrorResult(
                    HttpStatusCode.BadRequest,
                    oauth2Exception.Error,
                    oauth2Exception.ErrorDescription);
                context.ExceptionHandled = true;
            }
            else
            {
                _logger.LogError(context.Exception, "Unexpected error in OAuth2 endpoint");

                context.Result = CreateOAuth2ErrorResult(
                    HttpStatusCode.InternalServerError,
                    "server_error",
                    "An unexpected error occurred.");
                context.ExceptionHandled = true;
            }
        }

        private static ObjectResult CreateOAuth2ErrorResult(HttpStatusCode statusCode, string error, string errorDescription)
        {
            var errorResponse = new
            {
                error,
                error_description = errorDescription,
            };

            return new ObjectResult(errorResponse)
            {
                StatusCode = (int)statusCode,
                ContentTypes = { "application/json" },
            };
        }
    }
}
