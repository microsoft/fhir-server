// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Io;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Throttling;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

// import just the type, as Model namespace collides with System.Threading.Tasks
using OperationOutcome = Hl7.Fhir.Model.OperationOutcome;

namespace Microsoft.Health.Fhir.Shared.Api.UnitTests.Features.Throttling
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Throttling)]
    [Trait(Traits.Category, Categories.Web)]
    public class ThrottlingMiddlewareTests : IAsyncLifetime
    {
        private HttpContext _httpContext = new DefaultHttpContext();
        private Lazy<ThrottlingMiddleware> _middleware;
        private CancellationTokenSource _cts;
        private ServiceCollection _collection = new ServiceCollection();
        private ServiceProvider _provider;
        private ThrottlingConfiguration _throttlingConfiguration;
        private bool _securityEnabled = true;

        public ThrottlingMiddlewareTests()
        {
            _throttlingConfiguration = new ThrottlingConfiguration
            {
                ConcurrentRequestLimit = 5,
                Enabled = true,
            };

            _cts = new CancellationTokenSource();
            _httpContext.RequestAborted = _cts.Token;
            _httpContext.User = new ClaimsPrincipal(new ClaimsIdentity("authenticationType", "nametype", "roletype"));

            _throttlingConfiguration.ExcludedEndpoints.Add(new ExcludedEndpoint { Method = "get", Path = "/health/check" });

            _middleware = new Lazy<ThrottlingMiddleware>(
                () => new ThrottlingMiddleware(
                    async x =>
                    {
                        x.Response.StatusCode = 200;
                        try
                        {
                            if (!int.TryParse(Regex.Match(x.Request.Path, "/duration/(\\d+)").Groups[1].Value, out var duration))
                            {
                                duration = 5000;
                            }

                            var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, x.RequestAborted);
                            await Task.Delay(duration, linkedTokenSource.Token);
                        }
                        catch (TaskCanceledException) when (_cts.Token.IsCancellationRequested)
                        {
                        }
                        catch (TaskCanceledException) when (x.RequestAborted.IsCancellationRequested)
                        {
                            x.Response.StatusCode = StatusCodes.Status408RequestTimeout;
                        }
                    },
                    Substitute.For<IConfiguration>(),
                    Options.Create(_throttlingConfiguration),
                    Options.Create(new SecurityConfiguration { Enabled = _securityEnabled }),
                    NullLogger<ThrottlingMiddleware>.Instance));

            IActionResultExecutor<ObjectResult> executor = Substitute.For<IActionResultExecutor<ObjectResult>>();
            executor.ExecuteAsync(Arg.Any<ActionContext>(), Arg.Any<ObjectResult>()).ReturnsForAnyArgs(Task.CompletedTask);
            _collection.AddSingleton<IActionResultExecutor<ObjectResult>>(executor);
            _provider = _collection.BuildServiceProvider();
            _httpContext.RequestServices = _provider;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(4)]
        public void GivenRequestsBelowThreshold_WhenInvoked_Executes(int numberOfConcurrentRequests)
        {
            var tasks = SetupPreexistingRequests(numberOfConcurrentRequests);

            tasks.Add((_middleware.Value.Invoke(_httpContext), _httpContext, _cts));

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

            await _middleware.Value.Invoke(_httpContext);

            Assert.Equal(429, _httpContext.Response.StatusCode);
            _cts.Cancel();
            _cts.Dispose();
        }

        [Fact]
        public async Task GivenRequestsAtOrAboveThreshold_AndSecurityDisabled_Executes()
        {
            _securityEnabled = false;
            var mapping = SetupPreexistingRequests(numberOfConcurrentRequests: 10);

            await _middleware.Value.Invoke(_httpContext);
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
            var task = _middleware.Value.Invoke(_httpContext);
            _cts.Cancel();
            Assert.Equal(200, _httpContext.Response.StatusCode);

            await Task.WhenAll(mapping.Select(x => x.task));

            Assert.Contains(mapping, x => x.httpContext.Response.StatusCode == 429);
        }

        [Fact]
        public async Task GivenARequestWhenMaxRequestsAlreadyInFlightAndQueueingEnabled_WhenExistingRequestCompletes_TheQueuedRequestCompletes()
        {
            _throttlingConfiguration.MaxMillisecondsInQueue = 5000000;
            _throttlingConfiguration.MaxQueueSize = 10;

            // fill up to max
            var existingRequests = SetupPreexistingRequests(5);

            // make this a short request
            _httpContext.Request.Path = "/duration/1";
            Task requestTask = _middleware.Value.Invoke(_httpContext);

            // cancel one of the existing requests. This should allow the request above to go through
            existingRequests[0].cancellationTokenSource.Cancel();

            await requestTask;
            Assert.Equal(200, _httpContext.Response.StatusCode);
            Assert.All(existingRequests.Skip(1), tuple => Assert.False(tuple.task.IsCompleted));
        }

        [Fact]
        public async Task GivenARequestWhenMaxRequestsAlreadyInFlightAndQueueingEnabled_WhenMaxQueueTimeElapses_TheQueuedRequestReturns429()
        {
            _throttlingConfiguration.MaxMillisecondsInQueue = 1;
            _throttlingConfiguration.MaxQueueSize = 10;

            // fill up to max
            var existingRequests = SetupPreexistingRequests(5);

            // make this a short request
            _httpContext.Request.Path = "/duration/1";
            Task requestTask = _middleware.Value.Invoke(_httpContext);

            await requestTask;
            Assert.Equal(429, _httpContext.Response.StatusCode);
            Assert.All(existingRequests, tuple => Assert.False(tuple.task.IsCompleted));
        }

        [Fact]
        public async Task GivenARequestWhenMaxRequestsAlreadyInFlightAndQueueingEnabled_WhenQueueIsSaturated_RequestsAreRejected()
        {
            _throttlingConfiguration.MaxMillisecondsInQueue = 5000000;
            _throttlingConfiguration.MaxQueueSize = 1;

            // fill up to max
            var existingRequests = SetupPreexistingRequests(6);

            // make this a short request
            _httpContext.Request.Path = "/duration/1";
            await _middleware.Value.Invoke(_httpContext);
            Assert.Equal(429, _httpContext.Response.StatusCode);

            // cancel one of the existing requests. This should allow the request above to go through
            existingRequests[0].cancellationTokenSource.Cancel();
            await existingRequests[0].task;

            // try the request again
            await _middleware.Value.Invoke(_httpContext);
            Assert.Equal(200, _httpContext.Response.StatusCode);
        }

        [Fact]
        public async Task GivenARequest_ThatResultsInRequestRateExceeded_Returns429()
        {
            var throttlingMiddleware = new ThrottlingMiddleware(
                context => throw new RequestRateExceededException(TimeSpan.FromSeconds(1)),
                Substitute.For<IConfiguration>(),
                Options.Create(_throttlingConfiguration),
                Options.Create(new SecurityConfiguration()),
                NullLogger<ThrottlingMiddleware>.Instance);

            await throttlingMiddleware.Invoke(_httpContext);

            Assert.Equal(429, _httpContext.Response.StatusCode);
            Assert.True(_httpContext.Response.Headers.TryGetValue("Retry-After", out var values));
            Assert.Equal("1", values.ToString());
        }

        [Fact]
        public async Task GivenARequest_ThatResultsInRequestRateExceeded_ReturnsValidFhirResource()
        {
            var throttlingMiddleware = new ThrottlingMiddleware(
                context => throw new RequestRateExceededException(TimeSpan.FromSeconds(1)),
                Substitute.For<IConfiguration>(),
                Options.Create(_throttlingConfiguration),
                Options.Create(new SecurityConfiguration()),
                NullLogger<ThrottlingMiddleware>.Instance);

            _httpContext.Response.Body = new MemoryStream();
            await throttlingMiddleware.Invoke(_httpContext);

            // reset pointer to beginning of buffer, otherwise ReadToEnd will return an empty string
            _httpContext.Response.Body.Position = 0;
            var responseBody = new StreamReader(_httpContext.Response.Body).ReadToEnd();

            JsonSerializerOptions options = new JsonSerializerOptions().ForFhir(typeof(OperationOutcome).Assembly);
            OperationOutcome resourceType = JsonSerializer.Deserialize<OperationOutcome>(responseBody, options);

            Assert.Equal(OperationOutcome.IssueType.Throttled, resourceType.Issue[0].Code);
        }

        private List<(Task task, HttpContext httpContext, CancellationTokenSource cancellationTokenSource)> SetupPreexistingRequests(int numberOfConcurrentRequests, string path = "")
        {
            var output = new List<(Task, HttpContext, CancellationTokenSource cancellationTokenSource)>();

            for (int count = 0; count < numberOfConcurrentRequests; count++)
            {
                var cts = new CancellationTokenSource();
                var context = new DefaultHttpContext();
                context.RequestAborted = cts.Token;
                context.User = new ClaimsPrincipal(new ClaimsIdentity("authenticationType", "nametype", "roletype"));
                context.Request.Path = $"/{path}";
                context.Request.Method = HttpMethod.Get.ToString();
                context.RequestServices = _provider;
                output.Add((_middleware.Value.Invoke(context), context, cts));
            }

            return output;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        async Task IAsyncLifetime.DisposeAsync() => await _middleware.Value.DisposeAsync();
    }
}
