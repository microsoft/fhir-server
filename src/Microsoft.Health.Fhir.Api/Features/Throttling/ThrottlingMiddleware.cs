// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Api.Features.Throttling
{
    /// <summary>
    /// Middleware to limit the number of concurrent requests that an instance of the server handles simultaneously.
    /// Also provides request queuing up to a maximum queue size and wait time in the queue.
    /// Also handles unhandled <see cref="RequestRateExceededException"/> thrown downstream and returns a 429 response.
    /// These can happen during startup before the MVC handler has been reached.
    /// </summary>
    public sealed class ThrottlingMiddleware : IAsyncDisposable, IDisposable
    {
        private const double TargetSuccessPercentage = 99;
        private const int MinRetryAfterMilliseconds = 20;
        private const int MaxRetryAfterMilliseconds = 60000;
        private const double RetryAfterGrowthRate = 1.2;
        private const double RetryAfterDecayRate = 1.1;
        private const int SamplePeriodMilliseconds = 500;

        // hard-coding these to minimize resource consumption when throttling
        private const string ThrottledContentType = "application/json; charset=utf-8";
        private static readonly ReadOnlyMemory<byte> _throttledBody = CreateThrottledBody(Resources.TooManyConcurrentRequests);

        private readonly RequestDelegate _next;
        private readonly ILogger<ThrottlingMiddleware> _logger;
        private readonly HashSet<(string method, string path)> _excludedEndpoints;
        private readonly bool _securityEnabled;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly Task _samplingLoopTask;
        private readonly LinkedList<TaskCompletionSource<object>> _queue = new LinkedList<TaskCompletionSource<object>>();

        private int _requestsInFlight;
        private int _currentPeriodSuccessCount;
        private int _currentPeriodRejectedCount;
        private int _currentRetryAfterMilliseconds = MinRetryAfterMilliseconds;
        private readonly int _concurrentRequestLimit;
        private readonly int _maxQueueSize;
        private readonly int _maxMillisecondsInQueue;
        private bool _throttlingEnabled;

        public ThrottlingMiddleware(
            RequestDelegate next,
            IOptions<ThrottlingConfiguration> throttlingConfiguration,
            IOptions<SecurityConfiguration> securityConfiguration,
            ILogger<ThrottlingMiddleware> logger)
        {
            _next = EnsureArg.IsNotNull(next, nameof(next));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            ThrottlingConfiguration configuration = EnsureArg.IsNotNull(throttlingConfiguration?.Value, nameof(throttlingConfiguration));
            EnsureArg.IsNotNull(securityConfiguration?.Value, nameof(securityConfiguration));

            _throttlingEnabled = throttlingConfiguration.Value.Enabled;

            _securityEnabled = securityConfiguration.Value.Enabled;

            _excludedEndpoints = new HashSet<(string method, string path)>(new MethodPathTupleOrdinalIgnoreCaseEqualityComparer());

            if (configuration?.ExcludedEndpoints != null)
            {
                foreach (var excludedEndpoint in configuration.ExcludedEndpoints)
                {
                    _excludedEndpoints.Add((excludedEndpoint.Method, excludedEndpoint.Path));
                }
            }

            // snapshot the configuration values to reduce the number of instructions that need to execute in the lock.
            _concurrentRequestLimit = configuration.ConcurrentRequestLimit;
            _maxMillisecondsInQueue = configuration.MaxMillisecondsInQueue;
            _maxQueueSize = _maxMillisecondsInQueue == 0 ? 0 : configuration.MaxQueueSize;

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
            try
            {
                if (!_throttlingEnabled)
                {
                    await _next(context);
                    return;
                }

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

                bool canRun = false;
                bool queueSizeExceeded = false;
                LinkedListNode<TaskCompletionSource<object>> queueNode = null;

                for (int i = 0; i < 2; i++)
                {
                    // we do two loop iterations only when we need to queue up the request.
                    lock (_queue)
                    {
                        _logger.LogInformation("Requests in flight {Flight}, concurrent request limit {Limit}.", _requestsInFlight, _concurrentRequestLimit);
                        if (_requestsInFlight < _concurrentRequestLimit)
                        {
                            canRun = true;
                            _requestsInFlight++;
                            break;
                        }

                        if (_queue.Count >= _maxQueueSize)
                        {
                            queueSizeExceeded = true;
                            break;
                        }

                        if (queueNode != null)
                        {
                            _queue.AddLast(queueNode);
                            break;
                        }
                    }

                    // allocate outside of the lock
                    queueNode = new LinkedListNode<TaskCompletionSource<object>>(new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously));
                }

                if (canRun)
                {
                    // No throttling or no queueing necessary. Execute the request.
                    await RunRequest(context);
                }
                else if (queueSizeExceeded)
                {
                    await Return429(context);
                }
                else
                {
                    Debug.Assert(queueNode != null);

                    // start the timeout clock now while we wait for other requests ahead of us to finish.
                    if (await Task.WhenAny(queueNode.Value.Task, Task.Delay(_maxMillisecondsInQueue, context.RequestAborted)) != queueNode.Value.Task)
                    {
                        // timed out or request canceled
                        lock (_queue)
                        {
                            if (queueNode.List != null)
                            {
                                _queue.Remove(queueNode);
                            }
                            else
                            {
                                // this was a race condition and the request is actually ready to go now.
                                canRun = true;
                            }
                        }

                        if (!canRun)
                        {
                            await Return429(context);
                            return;
                        }
                    }

                    // the request has been dequeued and we can now execute.
                    await RunRequest(context);
                }
            }
            catch (RequestRateExceededException e)
            {
                await Return429FromRequestRateExceededException(e, context);
            }
        }

        private async Task RunRequest(HttpContext context)
        {
            try
            {
                Interlocked.Increment(ref _currentPeriodSuccessCount);
                await _next(context);
            }
            finally
            {
                // we are done with this request. Either let the first one in the queue go next or decrement _requestsInFlight if the queue is empty.

                TaskCompletionSource<object> completionSource = null;
                lock (_queue)
                {
                    if (_queue.Count == 0)
                    {
                        // there are no requests in the queue, so just decrement the number of executing requests.

                        Debug.Assert(_requestsInFlight != 0, "_requestsInFlight will reach a negative number");

                        _requestsInFlight--;
                    }
                    else
                    {
                        // with this request done, take the next item off the queue
                        completionSource = _queue.First.Value;
                        Debug.Assert(!completionSource.Task.IsCompleted, "Broken invariant: completion source should not be set if in the queue.");
                        _queue.RemoveFirst();
                    }
                }

                // complete the task to let the request proceed.
                completionSource?.SetResult(true);
            }
        }

        private async Task Return429(HttpContext context)
        {
            Interlocked.Increment(ref _currentPeriodRejectedCount);

            _logger.LogWarning(Resources.TooManyConcurrentRequests + " Limit is {Limit}. Requests in flight {Requests}", _concurrentRequestLimit, _requestsInFlight);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;

            context.Response.Headers.AddRetryAfterHeaders(TimeSpan.FromMilliseconds(_currentRetryAfterMilliseconds));

            context.Response.ContentLength = _throttledBody.Length;
            context.Response.ContentType = ThrottledContentType;

            await context.Response.Body.WriteAsync(_throttledBody);
        }

        private async Task Return429FromRequestRateExceededException(RequestRateExceededException exception, HttpContext context)
        {
            _logger.LogWarning($"Returning 429 from unhandled {nameof(RequestRateExceededException)}");

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;

            context.Response.Headers.AddRetryAfterHeaders(exception.RetryAfter);

            Memory<byte> body = CreateThrottledBody(exception.Message);

            context.Response.ContentLength = body.Length;
            context.Response.ContentType = ThrottledContentType;

            await context.Response.Body.WriteAsync(body);
        }

        private static Memory<byte> CreateThrottledBody(string message) => Encoding.UTF8.GetBytes($@"{{""resourceType"":""OperationOutcome"",""issue"":[{{""severity"":""error"",""code"":""throttled"",""diagnostics"":""{message}""}}]}}").AsMemory();

        public async ValueTask DisposeAsync()
        {
            _cancellationTokenSource.Cancel();
            await _samplingLoopTask;
            _cancellationTokenSource.Dispose();
            _samplingLoopTask.Dispose();
        }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        private class MethodPathTupleOrdinalIgnoreCaseEqualityComparer : IEqualityComparer<(string method, string path)>
        {
            public bool Equals((string method, string path) x, (string method, string path) y)
            {
                return StringComparer.OrdinalIgnoreCase.Equals(x.method, y.method) && StringComparer.OrdinalIgnoreCase.Equals(x.path, y.path);
            }

            public int GetHashCode((string method, string path) obj)
            {
                return HashCode.Combine(obj.method.GetHashCode(StringComparison.OrdinalIgnoreCase), obj.path.GetHashCode(StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}
