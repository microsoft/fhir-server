// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Security
{
    /// <summary>
    /// Middleware that detects access tokens (JWTs) passed as URL path segments and rejects the request.
    /// This prevents tokens from being logged in URLs and ensures they are not inadvertently exposed.
    /// </summary>
    public class AccessTokenUrlValidationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<AccessTokenUrlValidationMiddleware> _logger;

        // JWT pattern: three base64url-encoded segments separated by dots.
        // Each segment contains at least 10 characters of [A-Za-z0-9_-] (base64url alphabet).
        private static readonly Regex JwtPattern = new Regex(
            @"eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}",
            RegexOptions.Compiled,
            matchTimeout: System.TimeSpan.FromSeconds(1));



        public AccessTokenUrlValidationMiddleware(
            RequestDelegate next,
            ILogger<AccessTokenUrlValidationMiddleware> logger)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            if (ContainsJwt(context.Request.Path.Value))
            {
                _logger.LogWarning("Request rejected: access token detected in URL path. The token has been redacted.");
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Access tokens must not be passed in the URL path.");
                return;
            }

            if (ContainsJwt(context.Request.QueryString.Value))
            {
                _logger.LogWarning("Request rejected: access token detected in URL query string. The token has been redacted.");
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Access tokens must not be passed in the URL query string.");
                return;
            }

            await _next(context);
        }

        /// <summary>
        /// Determines whether the given URL component contains a JWT token.
        /// </summary>
        /// <param name="value">The URL path or query string value to check.</param>
        /// <returns>True if a JWT pattern is detected; otherwise, false.</returns>
        internal static bool ContainsJwt(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            return JwtPattern.IsMatch(value);
        }
    }
}
