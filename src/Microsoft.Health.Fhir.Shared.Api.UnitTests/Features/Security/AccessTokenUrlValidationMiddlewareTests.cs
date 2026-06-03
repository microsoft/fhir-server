// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Security
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Security)]
    [Trait(Traits.Category, Categories.Web)]
    public class AccessTokenUrlValidationMiddlewareTests
    {
        private const string SampleJwt = "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsIng1dCI6Ilh0LW83YSJ9.eyJhdWQiOiJodHRwczovL2V4YW1wbGUuY29tIiwiaXNzIjoiaHR0cHM6Ly9zdHMud2luZG93cy5uZXQvdGVzdC8ifQ.c2lnbmF0dXJlX3ZhbHVlX2hlcmVfZm9yX3Rlc3Rpbmc";

        private readonly AccessTokenUrlValidationMiddleware _middleware;
        private bool _nextWasCalled;

        public AccessTokenUrlValidationMiddlewareTests()
        {
            _nextWasCalled = false;
            _middleware = new AccessTokenUrlValidationMiddleware(
                context =>
                {
                    _nextWasCalled = true;
                    return Task.CompletedTask;
                },
                NullLogger<AccessTokenUrlValidationMiddleware>.Instance);
        }

        [Fact]
        public async Task GivenJwtInUrlPath_WhenInvoked_Returns400AndDoesNotCallNext()
        {
            var context = new DefaultHttpContext();
            context.Request.Path = $"/Encounter/{SampleJwt}";
            context.Response.Body = new MemoryStream();

            await _middleware.Invoke(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
            Assert.False(_nextWasCalled);
        }

        [Fact]
        public async Task GivenJwtInQueryString_WhenInvoked_Returns400AndDoesNotCallNext()
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/Patient";
            context.Request.QueryString = new QueryString($"?token={SampleJwt}");
            context.Response.Body = new MemoryStream();

            await _middleware.Invoke(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
            Assert.False(_nextWasCalled);
        }

        [Fact]
        public async Task GivenNormalPath_WhenInvoked_CallsNextMiddleware()
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/Patient/12345";
            context.Response.Body = new MemoryStream();

            await _middleware.Invoke(context);

            Assert.True(_nextWasCalled);
        }

        [Theory]
        [InlineData("/Encounter/eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJhdWQiOiJ0ZXN0IiwiaXNzIjoiaXNzdWVyIn0.c2lnbmF0dXJlX3ZhbHVl")]
        [InlineData("/Patient/eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U")]
        public void GivenPathWithJwt_ContainsJwt_ReturnsTrue(string path)
        {
            Assert.True(AccessTokenUrlValidationMiddleware.ContainsJwt(path));
        }

        [Theory]
        [InlineData("/Patient/12345")]
        [InlineData("/Encounter/abc-def-ghi")]
        [InlineData("/metadata")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("/Patient?name=test")]
        [InlineData("/Observation/a1b2c3d4-e5f6-7890-abcd-ef1234567890")]
        public void GivenPathWithoutJwt_ContainsJwt_ReturnsFalse(string path)
        {
            Assert.False(AccessTokenUrlValidationMiddleware.ContainsJwt(path));
        }

        [Fact]
        public async Task GivenJwtInUrlPath_WhenInvoked_UrlIsNotLoggedByDownstreamPipeline()
        {
            // Verifies that when a JWT is in the URL, the downstream pipeline (FhirRequestContextMiddleware)
            // never executes and therefore the token URL is never logged.
            var downstreamLogMessages = new List<string>();
            var downstreamLogger = Substitute.For<ILogger>();
            downstreamLogger.When(x => x.Log(
                Arg.Any<LogLevel>(),
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception>(),
                Arg.Any<Func<object, Exception, string>>()))
                .Do(callInfo =>
                {
                    var state = callInfo.ArgAt<object>(2);
                    var formatter = callInfo.ArgAt<Func<object, Exception, string>>(4);
                    if (formatter != null && state != null)
                    {
                        downstreamLogMessages.Add(formatter(state, null));
                    }
                });

            var middleware = new AccessTokenUrlValidationMiddleware(
                context =>
                {
                    // Simulate what FhirRequestContextMiddleware does: log the request URL
                    string uri = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
                    downstreamLogger.Log(LogLevel.Information, default, uri, null, (s, _) => s);
                    return Task.CompletedTask;
                },
                NullLogger<AccessTokenUrlValidationMiddleware>.Instance);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("fhir.example.com");
            httpContext.Request.Path = $"/Encounter/{SampleJwt}";
            httpContext.Response.Body = new MemoryStream();

            // Act
            await middleware.Invoke(httpContext);

            // Assert - downstream pipeline never logged the URL containing the token
            Assert.Empty(downstreamLogMessages);
            Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        }

        [Fact]
        public async Task GivenNormalRequest_WhenInvoked_UrlIsLoggedByDownstreamPipeline()
        {
            // Verifies that normal requests DO flow to the downstream pipeline and URLs are logged normally.
            var downstreamLogMessages = new List<string>();
            var downstreamLogger = Substitute.For<ILogger>();
            downstreamLogger.When(x => x.Log(
                Arg.Any<LogLevel>(),
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception>(),
                Arg.Any<Func<object, Exception, string>>()))
                .Do(callInfo =>
                {
                    var state = callInfo.ArgAt<object>(2);
                    var formatter = callInfo.ArgAt<Func<object, Exception, string>>(4);
                    if (formatter != null && state != null)
                    {
                        downstreamLogMessages.Add(formatter(state, null));
                    }
                });

            var middleware = new AccessTokenUrlValidationMiddleware(
                context =>
                {
                    // Simulate what FhirRequestContextMiddleware does: log the request URL
                    string uri = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
                    downstreamLogger.Log(LogLevel.Information, default, uri, null, (s, _) => s);
                    return Task.CompletedTask;
                },
                NullLogger<AccessTokenUrlValidationMiddleware>.Instance);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("fhir.example.com");
            httpContext.Request.Path = "/Patient/12345";
            httpContext.Response.Body = new MemoryStream();

            // Act
            await middleware.Invoke(httpContext);

            // Assert - URL was logged by the downstream pipeline
            Assert.Single(downstreamLogMessages);
            Assert.Contains("/Patient/12345", downstreamLogMessages[0]);
        }
    }
}
