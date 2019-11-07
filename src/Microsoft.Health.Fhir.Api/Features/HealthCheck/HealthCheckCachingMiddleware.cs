// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core;

namespace Microsoft.Health.Fhir.Api.Features.HealthCheck
{
    /// <summary>
    /// This is not the same as HealthCheckOptions.AllowCachingResponses.
    /// This Middleware provides short term caching to protect against DoS attacks on the health check endpoint.
    /// </summary>
    internal sealed class HealthCheckCachingMiddleware : IDisposable
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<HealthCheckCachingMiddleware> _logger;
        private byte[] _lastResultBuffer;
        private DateTimeOffset _lastCheckTime;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(1);

        public HealthCheckCachingMiddleware(RequestDelegate next, ILogger<HealthCheckCachingMiddleware> logger)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            Stream originalBodyStream = context.Response.Body;

            if (context.Request.Path.HasValue && context.Request.Path.StartsWithSegments(FhirServerApplicationBuilderExtensions.HealthCheckPath, StringComparison.OrdinalIgnoreCase))
            {
                if (await WriteCachedResult(originalBodyStream))
                {
                    return;
                }

                await _semaphore.WaitAsync(context.RequestAborted);
                try
                {
                    if (await WriteCachedResult(originalBodyStream))
                    {
                        return;
                    }

                    using (var cachedBody = new MemoryStream())
                    {
                        HttpResponse response = context.Response;
                        response.Body = cachedBody;

                        await _next(context);

                        _lastResultBuffer = cachedBody.ToArray();
                        _lastCheckTime = Clock.UtcNow;

                        response.Body = originalBodyStream;
                        await WriteCachedResult(originalBodyStream, true);
                    }
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            else
            {
                await _next(context);
            }
        }

        private async Task<bool> WriteCachedResult(Stream originalBodyStream, bool forceWrite = false)
        {
            if (forceWrite || (_lastResultBuffer != null && _lastCheckTime >= Clock.UtcNow.Add(-_cacheDuration)))
            {
                if (!forceWrite)
                {
                    _logger.LogDebug("Writing cached healthcheck from {0:o}", _lastCheckTime);
                }

                await originalBodyStream.WriteAsync(_lastResultBuffer, 0, _lastResultBuffer.Length);

                return true;
            }

            return false;
        }

        public void Dispose()
        {
            _semaphore?.Dispose();
        }
    }
}
