// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Api.Features.HealthCheck;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.HealthCheck
{
    public class HealthCheckCachingMiddlewareTests
    {
        private readonly RequestDelegate _requestDelegate;
        private readonly HealthCheckCachingMiddleware _middleware;
        private readonly DefaultHttpContext _context;

        public HealthCheckCachingMiddlewareTests()
        {
            _requestDelegate = Substitute.For<RequestDelegate>();

            _middleware = new HealthCheckCachingMiddleware(_requestDelegate, NullLogger<HealthCheckCachingMiddleware>.Instance);

            _context = new DefaultHttpContext();
            _context.Response.Body = new MemoryStream();
            _context.Request.Path = FhirServerApplicationBuilderExtensions.HealthCheckPath;

            _requestDelegate.When(x => x.Invoke(_context)).Do(x =>
            {
                var message = Encoding.UTF8.GetBytes("test");
                _context.Response.Body.Write(message, 0, message.Length);
            });
        }

        [Fact]
        public async Task GivenTheHealthCheckMiddleware_WhenCallingWithMultipleRequests_ThenOnlyOneResultShouldBeExecuted()
        {
            await Task.WhenAll(
                _middleware.Invoke(_context),
                _middleware.Invoke(_context),
                _middleware.Invoke(_context),
                _middleware.Invoke(_context));

            await _requestDelegate.Received(1).Invoke(_context);
        }

        [Fact]
        public async Task GivenTheHealthCheckMiddleware_WhenRequestingStatus_ThenTheResultIsWrittenCorrectly()
        {
            await _middleware.Invoke(_context);

            byte[] actual = ((MemoryStream)_context.Response.Body).ToArray();

            Assert.Equal("test", Encoding.UTF8.GetString(actual));
        }

        [Fact]
        public async Task GivenTheHealthCheckMiddleware_WhenRequestingStatusFromCache_ThenTheResultIsWrittenCorrectly()
        {
            // Populate cache
            await _middleware.Invoke(_context);

            // Reset response
            _context.Response.Body = new MemoryStream();
            await _middleware.Invoke(_context);

            byte[] actual = ((MemoryStream)_context.Response.Body).ToArray();

            Assert.Equal("test", Encoding.UTF8.GetString(actual));
        }

        [Fact]
        public async Task GivenTheHealthCheckMiddleware_WhenMoreThan1SecondApart_ThenSecondRequestGetsFreshResults()
        {
            // Mocks the time a second ago so we can call the middleware in the past
            using (Mock.Property(() => Clock.UtcNowFunc, () => DateTimeOffset.UtcNow.AddSeconds(-1)))
            {
                await Task.WhenAll(
                    _middleware.Invoke(_context),
                    _middleware.Invoke(_context));
            }

            // Call the middleware again to ensure we get new results
            await _middleware.Invoke(_context);

            await _requestDelegate.Received(2).Invoke(_context);
        }
    }
}
