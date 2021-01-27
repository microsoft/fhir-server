// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Api.Features.Throttling
{
    /// <summary>
    /// Middleware to limit the number of concurrent requests that an instance of the server handles simultaneously.
    /// </summary>
    public sealed class ThrottlingMiddleware : IAsyncDisposable, IDisposable
    {
        private const double TargetSuccessPercentage = 99;
        private const int MinRetryAfterMilliseconds = 20;
        private const int MaxRetryAfterMilliseconds = 60000;
        private const double RetryAfterGrowthRate = 1.2;
        private const double RetryAfterDecayRate = 1.1;
        private const int SamplePeriodMilliseconds = 500;

        private readonly ILogger<ThrottlingMiddleware> _logger;
        private readonly ThrottlingConfiguration _configuration;
        private readonly HashSet<(string method, string path)> _excludedEndpoints;
        private readonly bool _securityEnabled;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private int _requestsInFlight = 0;
        private RequestDelegate _next;
        private int _currentPeriodSuccessCount;
        private int _currentPeriodRejectedCount;
        private int _currentRetryAfterMilliseconds = MinRetryAfterMilliseconds;

        private readonly Task _samplingLoopTask;

        public ThrottlingMiddleware(
            RequestDelegate next,
            IOptions<ThrottlingConfiguration> throttlingConfiguration,
            IOptions<SecurityConfiguration> securityConfiguration,
            ILogger<ThrottlingMiddleware> logger)
        {
            _next = EnsureArg.IsNotNull(next, nameof(next));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _configuration = EnsureArg.IsNotNull(throttlingConfiguration?.Value, nameof(throttlingConfiguration));
            EnsureArg.IsNotNull(securityConfiguration?.Value, nameof(securityConfiguration));

            _securityEnabled = securityConfiguration.Value.Enabled;

            _excludedEndpoints = new HashSet<(string method, string path)>(new StringTupleOrdinalIgnoreCaseEqualityComparer());

            if (_configuration?.ExcludedEndpoints != null)
            {
                foreach (var excludedEndpoint in _configuration.ExcludedEndpoints)
                {
                    _excludedEndpoints.Add((excludedEndpoint.Method, excludedEndpoint.Path));
                }
            }

            _samplingLoopTask = SamplingLoop();
        }

        /// <summary>
        /// Samples the success rate (i.e. requests not throttled) over a period, and adjusts the value we return as the retry after header.
        /// This is an extremely simple approach that exponentially grows or decays the value depending on whether the success rate is
        /// less than or greater than TargetSuccessPercentage, respectively.
        /// The value grows to be as high as necessary to keep the success rate over 99%, but we want it to decrease when the request rate backs off,
        /// so overall latency for clients is not unnecessarily high.
        /// </summary>
        public async Task SamplingLoop()
        {
            while (true)
            {
                try
                {
                    await Task.Delay(SamplePeriodMilliseconds, _cancellationTokenSource.Token);

                    var successCount = Interlocked.Exchange(ref _currentPeriodSuccessCount, 0);
                    var failureCount = Interlocked.Exchange(ref _currentPeriodRejectedCount, 0);

                    var totalCount = successCount + failureCount;
                    double successRate = totalCount == 0 ? 100.0 : successCount * 100.0 / totalCount;

                    // see if we should raise of lower the value
                    _currentRetryAfterMilliseconds =
                        successRate >= TargetSuccessPercentage
                            ? Math.Max(MinRetryAfterMilliseconds, (int)(_currentRetryAfterMilliseconds / RetryAfterDecayRate))
                            : Math.Min(MaxRetryAfterMilliseconds, (int)(_currentRetryAfterMilliseconds * RetryAfterGrowthRate));
                }
                catch (TaskCanceledException) when (_cancellationTokenSource.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Unexpected failure in background sampling loop");
                }
            }
        }

        public async Task Invoke(HttpContext context)
        {
            if (_excludedEndpoints.Contains((context.Request.Method, context.Request.Path.Value)))
            {
                // Endpoint is exempt from concurrent request limits.
                await _next(context);
                return;
            }

            if (_securityEnabled && !context.User.Identity.IsAuthenticated)
            {
                // Ignore Unauthenticated users if security is enabled
                await _next(context);
                return;
            }

            try
            {
                if (Interlocked.Increment(ref _requestsInFlight) <= _configuration.ConcurrentRequestLimit)
                {
                    // Still within the concurrent request limit, let the request through.
                    Interlocked.Increment(ref _currentPeriodSuccessCount);
                    await _next(context);
                }
                else
                {
                    // Exceeded the concurrent request limit, return 429.
                    Interlocked.Increment(ref _currentPeriodRejectedCount);
                    context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                    _logger.LogWarning($"{Resources.TooManyConcurrentRequests}. Limit is {_configuration.ConcurrentRequestLimit}.");

                    // note we are aligning with Cosmos DB and not returning the standard header (which is in seconds)
                    context.Response.Headers[KnownHeaders.RetryAfterMilliseconds] = _currentRetryAfterMilliseconds.ToString();

                    // Output an OperationOutcome in the body.
                    var result = TooManyRequestsActionResult.TooManyRequests;
                    await result.ExecuteResultAsync(new ActionContext { HttpContext = context, RouteData = context.GetRouteData() ?? new RouteData() });
                }
            }
            finally
            {
                Interlocked.Decrement(ref _requestsInFlight);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cancellationTokenSource.Cancel();
            await _samplingLoopTask;
            _cancellationTokenSource.Dispose();
            _samplingLoopTask.Dispose();
        }

        public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

        private class StringTupleOrdinalIgnoreCaseEqualityComparer : IEqualityComparer<ValueTuple<string, string>>
        {
            public bool Equals(ValueTuple<string, string> x, ValueTuple<string, string> y)
            {
                return StringComparer.OrdinalIgnoreCase.Equals(x.Item1, y.Item1) && StringComparer.OrdinalIgnoreCase.Equals(x.Item2, y.Item2);
            }

            public int GetHashCode(ValueTuple<string, string> obj)
            {
                return HashCode.Combine(obj.Item1.GetHashCode(StringComparison.OrdinalIgnoreCase), obj.Item2.GetHashCode(StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}
