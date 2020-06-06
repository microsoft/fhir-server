// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;

namespace Microsoft.Health.Fhir.Api.Features.Throttling
{
    public class ThrottlingMiddleware
    {
        private readonly ILogger<ThrottlingMiddleware> _logger;
        private readonly ThrottlingConfiguration _configuration;
        private int _requestsInFlight = 0;
        private RequestDelegate _next;

        public ThrottlingMiddleware(RequestDelegate next, IOptions<ThrottlingConfiguration> configuration, ILogger<ThrottlingMiddleware> logger)
        {
            _next = EnsureArg.IsNotNull(next, nameof(next));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _configuration = EnsureArg.IsNotNull(configuration?.Value, nameof(configuration));
        }

        public async Task Invoke(HttpContext context)
        {
            if (_configuration.ExcludedEndpoints.Contains($"{context.Request.Method}:{context.Request.Path.Value?.TrimEnd('/')}", StringComparer.OrdinalIgnoreCase))
            {
                // Endpoint is exempt from concurrent request limits.
                await _next(context);
                return;
            }

            try
            {
                if (Interlocked.Increment(ref _requestsInFlight) <= _configuration.ConcurrentRequestLimit)
                {
                    // Still within the concurrent request limit, let the request through.
                    await _next(context);
                }
                else
                {
                    // Exceeded the concurrent request limit, return 429.
                    context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                    _logger.LogWarning(Resources.TooManyConcurrentRequests, _configuration.ConcurrentRequestLimit);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _requestsInFlight);
            }
        }
    }
}
