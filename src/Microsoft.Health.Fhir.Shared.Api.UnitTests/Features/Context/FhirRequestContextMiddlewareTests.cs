// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Context
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class FhirRequestContextMiddlewareTests
    {
        [Fact]
        public async Task GivenAnHttpRequest_WhenExecutingFhirRequestContextMiddleware_ThenCorrectUriShouldBeSet()
        {
            IFhirRequestContext fhirRequestContext = await SetupAsync(CreateHttpContext());

            Assert.Equal(new Uri("https://localhost:30/stu3/Observation?code=123"), fhirRequestContext.Uri);
        }

        [Fact]
        public async Task GivenAnHttpRequest_WhenExecutingFhirRequestContextMiddleware_ThenCorrectBaseUriShouldBeSet()
        {
            IFhirRequestContext fhirRequestContext = await SetupAsync(CreateHttpContext());

            Assert.Equal(new Uri("https://localhost:30/stu3/"), fhirRequestContext.BaseUri);
        }

        [Fact]
        public async Task GivenAnHttpRequest_WhenExecutingFhirRequestContextMiddleware_ThenRequestIdHeaderShouldBeSet()
        {
            const string expectedRequestId = "123";

            HttpContext httpContext = CreateHttpContext();

            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            var instanceConfiguration = Substitute.For<IFhirServerInstanceConfiguration>();
            var fhirContextMiddlware = new FhirRequestContextMiddleware(next: (innerHttpContext) => Task.CompletedTask);
            string Provider() => expectedRequestId;

            await fhirContextMiddlware.Invoke(httpContext, fhirRequestContextAccessor, instanceConfiguration, Provider);

            Assert.True(httpContext.Response.Headers.TryGetValue("X-Request-Id", out StringValues value));
            Assert.Equal(new StringValues(expectedRequestId), value);
        }

        [Fact]
        public async Task GivenAnHttpRequestWithVanityUrlHeader_WhenExecutingFhirRequestContextMiddleware_ThenVanityUrlHeaderShouldBeSetInResponseAndInstanceConfiguration()
        {
            const string expectedVanityUrl = "https://custom.example.com/fhir/";

            HttpContext httpContext = CreateHttpContext();
            httpContext.Request.Headers["x-ms-vanity-url"] = expectedVanityUrl;

            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            var instanceConfiguration = new FhirServerInstanceConfiguration();
            var fhirContextMiddlware = new FhirRequestContextMiddleware(next: (innerHttpContext) => Task.CompletedTask);
            string Provider() => Guid.NewGuid().ToString();

            await fhirContextMiddlware.Invoke(httpContext, fhirRequestContextAccessor, instanceConfiguration, Provider);

            Assert.True(httpContext.Response.Headers.TryGetValue("x-ms-vanity-url", out StringValues value));
            Assert.Equal(new StringValues(expectedVanityUrl), value);

            // Verify that vanity URL was stored in instance configuration
            Assert.True(instanceConfiguration.IsInitialized);
            Assert.Equal(new Uri(expectedVanityUrl), instanceConfiguration.VanityUrl);
        }

        [Fact]
        public async Task GivenAnHttpRequestWithoutVanityUrlHeader_WhenExecutingFhirRequestContextMiddleware_ThenVanityUrlShouldBeNull()
        {
            HttpContext httpContext = CreateHttpContext();

            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            var instanceConfiguration = new FhirServerInstanceConfiguration();
            var fhirContextMiddlware = new FhirRequestContextMiddleware(next: (innerHttpContext) => Task.CompletedTask);
            string Provider() => Guid.NewGuid().ToString();

            await fhirContextMiddlware.Invoke(httpContext, fhirRequestContextAccessor, instanceConfiguration, Provider);

            // Verify that vanity URL is NOT set in response headers when not provided in request
            Assert.False(httpContext.Response.Headers.TryGetValue("x-ms-vanity-url", out _));

            // Verify that vanity URL is null in instance configuration
            Assert.True(instanceConfiguration.IsInitialized);
            Assert.Null(instanceConfiguration.VanityUrl);
            Assert.NotNull(instanceConfiguration.BaseUri);
        }

        [Theory]
        [InlineData("127.0.0.1")]
        [InlineData("::1")]
        [InlineData("localhost")]
        [InlineData("192.168.1.1")]
        [InlineData("10.0.0.1")]
        [InlineData("172.16.0.1")]
        public async Task GivenALoopbackOrLocalRequest_WhenExecutingFhirRequestContextMiddleware_ThenInstanceConfigurationShouldNotBeInitialized(string host)
        {
            HttpContext httpContext = CreateHttpContext();
            httpContext.Request.Host = new HostString(host, 30);

            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            var instanceConfiguration = new FhirServerInstanceConfiguration();
            var fhirContextMiddlware = new FhirRequestContextMiddleware(next: (innerHttpContext) => Task.CompletedTask);
            string Provider() => Guid.NewGuid().ToString();

            await fhirContextMiddlware.Invoke(httpContext, fhirRequestContextAccessor, instanceConfiguration, Provider);

            // Verify that instance configuration was NOT initialized for loopback/local requests
            Assert.False(instanceConfiguration.IsInitialized);
        }

        [Fact]
        public async Task GivenAnExternalHostRequest_WhenExecutingFhirRequestContextMiddleware_ThenInstanceConfigurationShouldBeInitialized()
        {
            HttpContext httpContext = CreateHttpContext();
            httpContext.Request.Host = new HostString("api.example.com", 443);

            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            var instanceConfiguration = new FhirServerInstanceConfiguration();
            var fhirContextMiddlware = new FhirRequestContextMiddleware(next: (innerHttpContext) => Task.CompletedTask);
            string Provider() => Guid.NewGuid().ToString();

            await fhirContextMiddlware.Invoke(httpContext, fhirRequestContextAccessor, instanceConfiguration, Provider);

            // Verify that instance configuration WAS initialized for external requests
            Assert.True(instanceConfiguration.IsInitialized);
            Assert.NotNull(instanceConfiguration.BaseUri);
        }

        private async Task<IFhirRequestContext> SetupAsync(HttpContext httpContext)
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            var instanceConfiguration = Substitute.For<IFhirServerInstanceConfiguration>();
            var fhirContextMiddlware = new FhirRequestContextMiddleware(next: (innerHttpContext) => Task.CompletedTask);
            string Provider() => Guid.NewGuid().ToString();

            await fhirContextMiddlware.Invoke(httpContext, fhirRequestContextAccessor, instanceConfiguration, Provider);

            Assert.NotNull(fhirRequestContextAccessor.RequestContext);

            return fhirRequestContextAccessor.RequestContext;
        }

        private HttpContext CreateHttpContext()
        {
            HttpContext httpContext = new DefaultHttpContext();

            httpContext.Request.Method = "GET";
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("localhost", 30);
            httpContext.Request.PathBase = new PathString("/stu3");
            httpContext.Request.Path = new PathString("/Observation");
            httpContext.Request.QueryString = new QueryString("?code=123");

            return httpContext;
        }
    }
}
