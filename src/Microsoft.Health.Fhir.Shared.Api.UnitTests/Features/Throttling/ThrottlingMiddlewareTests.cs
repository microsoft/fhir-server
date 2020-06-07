// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Io;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Throttling;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Api.UnitTests.Features.Throttling
{
    public class ThrottlingMiddlewareTests
    {
        private readonly HttpContext _httpContext = new DefaultHttpContext();
        private readonly ThrottlingMiddleware _middleware;
        private readonly CancellationTokenSource _cts;

        public ThrottlingMiddlewareTests()
        {
            _cts = new CancellationTokenSource();
            _httpContext.RequestAborted = _cts.Token;
            var throttlingConfiguration = new ThrottlingConfiguration
            {
                ConcurrentRequestLimit = 5,
            };
            throttlingConfiguration.ExcludedEndpoints.Add("get:/health/check");

            _middleware = new ThrottlingMiddleware(
                async x =>
                {
                    x.Response.StatusCode = 200;
                    try
                    {
                        await Task.Delay(5000, _cts.Token);
                    }
                    catch (TaskCanceledException) when (_cts.Token.IsCancellationRequested)
                    {
                    }
                },
                Options.Create(throttlingConfiguration),
                NullLogger<ThrottlingMiddleware>.Instance);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(4)]
        public void GivenRequestsBelowThreshold_WhenInvoked_Executes(int numberOfConcurrentRequests)
        {
            var tasks = SetupPreexistingRequests(numberOfConcurrentRequests);

            tasks.Add((_middleware.Invoke(_httpContext), _httpContext));

            Assert.Equal(200, _httpContext.Response.StatusCode);
            _cts.Cancel();
            _cts.Dispose();
        }

        [Theory]
        [InlineData(5)]
        [InlineData(10)]
        public async Task GivenRequestsAtOrAboveThreshold_WhenInvoked_ReturnsTooManyRequests(int numberOfConcurrentRequests)
        {
            _ = SetupPreexistingRequests(numberOfConcurrentRequests);

            await _middleware.Invoke(_httpContext);

            Assert.Equal(429, _httpContext.Response.StatusCode);
            _cts.Cancel();
            _cts.Dispose();
        }

        [Fact]
        public async Task GivenRequestsToExcludedEndpoint_WhenInvoked_Executes()
        {
            var mapping = SetupPreexistingRequests(10, "health/check");
            _cts.Cancel();
            await Task.WhenAll(mapping.Select(x => x.task));

            foreach (var context in mapping.Select(x => x.httpContext))
            {
                Assert.Equal(200, context.Response.StatusCode);
            }
        }

        [Fact]
        public async Task GivenRequestToExcludedEndpoint_WhenAlreadyThrottled_Succeeds()
        {
            var mapping = SetupPreexistingRequests(6);

            _httpContext.Request.Path = "/health/check";
            _httpContext.Request.Method = HttpMethod.Get.ToString();
            var task = _middleware.Invoke(_httpContext);
            _cts.Cancel();
            Assert.Equal(200, _httpContext.Response.StatusCode);

            await Task.WhenAll(mapping.Select(x => x.task));

            Assert.Contains(mapping, x => x.httpContext.Response.StatusCode == 429);
        }

        private List<(Task task, HttpContext httpContext)> SetupPreexistingRequests(int numberOfConcurrentRequests, string path = "")
        {
            List<(Task, HttpContext)> output = new List<(Task, HttpContext)>();

            for (int count = 0; count < numberOfConcurrentRequests; count++)
            {
                var context = new DefaultHttpContext();
                context.Request.Path = $"/{path}";
                context.Request.Method = HttpMethod.Get.ToString();
                output.Add((_middleware.Invoke(context), context));
            }

            return output;
        }
    }
}
