// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Io;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Throttling;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Api.UnitTests.Features.Throttling
{
    public class ThrottlingMiddlewareTests : IAsyncLifetime
    {
        private HttpContext _httpContext = new DefaultHttpContext();
        private ThrottlingMiddleware _middleware;
        private CancellationTokenSource _cts;
        private IActionResultExecutor<ObjectResult> _executor;
        private ServiceCollection _collection = new ServiceCollection();
        private ServiceProvider _provider;

        public ThrottlingMiddlewareTests()
        {
            Init(securityEnabled: true);
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
        public async Task GivenRequestsAtOrAboveThreshold_AndSecurityDisabled_Executes()
        {
            Init(securityEnabled: false);
            var mapping = SetupPreexistingRequests(numberOfConcurrentRequests: 10);

            await _middleware.Invoke(_httpContext);
            Assert.Equal(429, _httpContext.Response.StatusCode);
            _cts.Cancel();
            Assert.Contains(429, mapping.Select(x => x.httpContext.Response.StatusCode));

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
                context.User = new ClaimsPrincipal(new ClaimsIdentity("authenticationType", "nametype", "roletype"));
                context.Request.Path = $"/{path}";
                context.Request.Method = HttpMethod.Get.ToString();
                context.RequestServices = _provider;
                output.Add((_middleware.Invoke(context), context));
            }

            return output;
        }

        private void Init(bool securityEnabled)
        {
            _cts = new CancellationTokenSource();
            _httpContext.RequestAborted = _cts.Token;
            _httpContext.User = new ClaimsPrincipal(new ClaimsIdentity("authenticationType", "nametype", "roletype"));

            var throttlingConfiguration = new ThrottlingConfiguration
            {
                ConcurrentRequestLimit = 5,
            };
            throttlingConfiguration.ExcludedEndpoints.Add(new ExcludedEndpoint { Method = "get", Path = "/health/check" });

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
                Options.Create(new Microsoft.Health.Fhir.Core.Configs.SecurityConfiguration { Enabled = securityEnabled }),
                NullLogger<ThrottlingMiddleware>.Instance);

            _executor = Substitute.For<IActionResultExecutor<ObjectResult>>();
            _executor.ExecuteAsync(Arg.Any<ActionContext>(), Arg.Any<ObjectResult>()).ReturnsForAnyArgs(Task.CompletedTask);
            _collection.AddSingleton<IActionResultExecutor<ObjectResult>>(_executor);
            _provider = _collection.BuildServiceProvider();
            _httpContext.RequestServices = _provider;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        async Task IAsyncLifetime.DisposeAsync() => await _middleware.DisposeAsync();
    }
}
