// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
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

        public ThrottlingMiddlewareTests()
        {
            _middleware = new ThrottlingMiddleware(
                Options.Create(new ThrottlingConfiguration
                {
                    ConcurrentRequestLimit = 5,
                    ExcludedEndpoints = new HashSet<string>
                    {
                        "get:/health/check",
                    },
                }),
                NullLogger<ThrottlingMiddleware>.Instance);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(4)]
        public async Task GivenRequestsBelowThreshold_WhenInvoked_Executes(int numberOfConcurrentRequests)
        {
            var tasks = SetupPreexistingRequests(numberOfConcurrentRequests);

            await _middleware.InvokeAsync(_httpContext, x =>
            {
                x.Response.StatusCode = 200;
                return Task.CompletedTask;
            });

            Assert.Equal(200, _httpContext.Response.StatusCode);
        }

        [Theory]
        [InlineData(5)]
        [InlineData(10)]
        public async Task GivenRequestsAtOrAboveThreshold_WhenInvoked_ReturnsTooManyRequests(int numberOfConcurrentRequests)
        {
            var tasks = SetupPreexistingRequests(numberOfConcurrentRequests);

            await _middleware.InvokeAsync(_httpContext, (x) =>
            {
                x.Response.StatusCode = 200;
                return Task.CompletedTask;
            });

            Assert.Equal(429, _httpContext.Response.StatusCode);
        }

        private List<Task> SetupPreexistingRequests(int numberOfConcurrentRequests)
        {
            int count = 0;
            List<Task> tasks = new List<Task>();

            while (count < numberOfConcurrentRequests)
            {
                var context = new DefaultHttpContext();
                tasks.Add(_middleware.InvokeAsync(context, x => Task.Delay(5000)));
            }

            return tasks;
        }
    }
}
