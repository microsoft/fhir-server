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

namespace Microsoft.Health.Fhir.Api.Features.Throttling
{
    public class ThrottlingMiddleware
    {
        private readonly ILogger<ThrottlingMiddleware> _logger;
        private readonly ThrottlingConfiguration _configuration;
        private readonly HashSet<(string method, string path)> _excludedEndpoints = new HashSet<(string method, string path)>();
        private int _requestsInFlight = 0;
        private RequestDelegate _next;

        public ThrottlingMiddleware(RequestDelegate next, IOptions<ThrottlingConfiguration> configuration, ILogger<ThrottlingMiddleware> logger)
        {
            _next = EnsureArg.IsNotNull(next, nameof(next));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _configuration = EnsureArg.IsNotNull(configuration?.Value, nameof(configuration));
            _excludedEndpoints = new HashSet<(string method, string path)>(new StringTupleOrdinalIgnoreCaseEqualityComparer());

            if (_configuration?.ExcludedEndpoints != null)
            {
                foreach (var excludedEndpoint in _configuration.ExcludedEndpoints)
                {
                    var mapping = excludedEndpoint.Split(separator: ':', count: 2);

                    if (mapping.Length != 2)
                    {
                        throw new ArgumentException($"Entry '{excludedEndpoint}' is invalid. It should be of the format [request method]:[request path].", nameof(configuration));
                    }

                    _excludedEndpoints.Add((mapping[0], mapping[1]));
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
                    _logger.LogWarning($"{Resources.TooManyConcurrentRequests}. Limit is {_configuration.ConcurrentRequestLimit}.");

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

        private class StringTupleOrdinalIgnoreCaseEqualityComparer : IEqualityComparer<ValueTuple<string, string>>
        {
            public bool Equals(ValueTuple<string, string> x, ValueTuple<string, string> y)
            {
                return StringComparer.OrdinalIgnoreCase.Equals(x.Item1, y.Item1) && StringComparer.OrdinalIgnoreCase.Equals(x.Item2, y.Item2);
            }

            public int GetHashCode(ValueTuple<string, string> obj)
            {
                return StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2);
            }
        }
    }
}
