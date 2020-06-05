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
    public class ThrottlingMiddleware : IMiddleware, IDisposable
    {
        private readonly ILogger<ThrottlingMiddleware> _logger;
        private readonly ThrottlingConfiguration _configuration;
        private SemaphoreSlim _sem;
        private int _requestsInFlight = 0;  // confirm mdidleware is singleton

        public ThrottlingMiddleware(IOptions<ThrottlingConfiguration> configuration, ILogger<ThrottlingMiddleware> logger)
        {
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _configuration = EnsureArg.IsNotNull(configuration?.Value, nameof(configuration));

            _sem = new SemaphoreSlim(0, configuration.Value.ConcurrentRequestLimit);
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            if (_configuration.ExcludedEndpoints.Contains($"{context.Request.Method}:{context.Request.Path.Value?.Trim('/')}", StringComparer.OrdinalIgnoreCase))
            {
                // Endpoint is exempt from concurrent request limits.
                await next(context);
                return;
            }

            try
            {
                if (Interlocked.Increment(ref _requestsInFlight) < _configuration.ConcurrentRequestLimit)
                {
                    // Still within the concurrent request limit, let the request through.
                    await next(context);
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

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sem?.Dispose();
                _sem = null;
            }
        }
    }
}
