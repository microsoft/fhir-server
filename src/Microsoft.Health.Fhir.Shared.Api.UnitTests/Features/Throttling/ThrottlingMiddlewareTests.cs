// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
                    await Task.Delay(5000, _cts.Token);
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

            tasks.Add(_middleware.Invoke(_httpContext));

            Assert.Equal(200, _httpContext.Response.StatusCode);
            _cts.Cancel();
            _cts.Dispose();
        }

        [Theory]
        [InlineData(5)]
        [InlineData(10)]
        public async Task GivenRequestsAtOrAboveThreshold_WhenInvoked_ReturnsTooManyRequests(int numberOfConcurrentRequests)
        {
            var tasks = SetupPreexistingRequests(numberOfConcurrentRequests);

            await _middleware.Invoke(_httpContext);

            Assert.Equal(429, _httpContext.Response.StatusCode);
            _cts.Cancel();
            _cts.Dispose();
        }

        private List<Task> SetupPreexistingRequests(int numberOfConcurrentRequests)
        {
            List<Task> tasks = new List<Task>();

            for (int count = 0; count < numberOfConcurrentRequests; count++)
            {
                var context = new DefaultHttpContext();
                tasks.Add(_middleware.Invoke(context));
            }

            return tasks;
        }
    }
}
