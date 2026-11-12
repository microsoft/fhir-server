// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Castle.Core.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Partitions;
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
        private readonly IPartitionManager _partitionManager = new PartitionManager(Options.Create(new PartitionConfiguration()));

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
            var fhirContextMiddlware = new FhirRequestContextMiddleware(next: (innerHttpContext) => Task.CompletedTask, Substitute.For<ILogger<FhirRequestContextMiddleware>>(), _partitionManager);
            string Provider() => expectedRequestId;

            await fhirContextMiddlware.Invoke(httpContext, fhirRequestContextAccessor, instanceConfiguration, Provider);

            Assert.True(httpContext.Response.Headers.TryGetValue("X-Request-Id", out StringValues value));
            Assert.Equal(new StringValues(expectedRequestId), value);
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
            var fhirContextMiddlware = new FhirRequestContextMiddleware(next: (innerHttpContext) => Task.CompletedTask, Substitute.For<ILogger<FhirRequestContextMiddleware>>(), _partitionManager);
            string Provider() => Guid.NewGuid().ToString();

            await fhirContextMiddlware.Invoke(httpContext, fhirRequestContextAccessor, instanceConfiguration, Provider);

            // Verify that instance configuration was NOT initialized for loopback/local requests
            Assert.Null(instanceConfiguration.BaseUri);
        }

        [Fact]
        public async Task GivenAnExternalHostRequest_WhenExecutingFhirRequestContextMiddleware_ThenBaseUriShouldBeInitialized()
        {
            HttpContext httpContext = CreateHttpContext();
            httpContext.Request.Host = new HostString("api.example.com", 443);

            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            var instanceConfiguration = new FhirServerInstanceConfiguration();
            var fhirContextMiddlware = new FhirRequestContextMiddleware(next: (innerHttpContext) => Task.CompletedTask, Substitute.For<ILogger<FhirRequestContextMiddleware>>(), _partitionManager);
            string Provider() => Guid.NewGuid().ToString();

            await fhirContextMiddlware.Invoke(httpContext, fhirRequestContextAccessor, instanceConfiguration, Provider);

            // Verify that baseUri WAS initialized for external requests
            Assert.NotNull(instanceConfiguration.BaseUri);
        }

        [Fact]
        public async Task GivenLoopbackRequestFollowedByExternalRequest_WhenExecutingFhirRequestContextMiddleware_ThenBaseUriShouldBeInitializedByExternalRequest()
        {
            // First request from loopback (health check)
            HttpContext loopbackContext = CreateHttpContext();
            loopbackContext.Request.Host = new HostString("localhost", 30);

            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            var instanceConfiguration = new FhirServerInstanceConfiguration();
            var fhirContextMiddlware = new FhirRequestContextMiddleware(next: (innerHttpContext) => Task.CompletedTask, Substitute.For<ILogger<FhirRequestContextMiddleware>>(), _partitionManager);
            string Provider() => Guid.NewGuid().ToString();

            await fhirContextMiddlware.Invoke(loopbackContext, fhirRequestContextAccessor, instanceConfiguration, Provider);

            // Verify loopback request did not initialize configuration
            Assert.Null(instanceConfiguration.BaseUri);

            // Second request from external host
            HttpContext externalContext = CreateHttpContext();
            externalContext.Request.Host = new HostString("api.example.com", 443);

            await fhirContextMiddlware.Invoke(externalContext, fhirRequestContextAccessor, instanceConfiguration, Provider);

            // Verify baseUri was initialized by the external request
            Assert.NotNull(instanceConfiguration.BaseUri);
            Assert.Equal(new Uri("https://api.example.com/stu3/"), instanceConfiguration.BaseUri);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenPartitionPrefixAndExistingPathBase_WhenMiddlewareCompletes_ThenPathsAreRestored(bool throwInNext)
        {
            var configuration = new PartitionConfiguration();
            configuration.Partitions.Add(new PartitionInfo { Id = "partition-a", DatabaseName = "database-a" });
            var manager = new PartitionManager(Options.Create(configuration));
            HttpContext httpContext = CreateHttpContext();
            httpContext.Request.Host = new HostString("api.example.com");
            httpContext.Request.Path = new PathString("/partition/partition-a/Patient");
            var accessor = new FhirRequestContextAccessor();
            var instanceConfiguration = new FhirServerInstanceConfiguration();
            IFhirRequestContext capturedContext = null;
            var afterAuthentication = new FhirRequestContextAfterAuthenticationMiddleware(
                context =>
                {
                    capturedContext = accessor.RequestContext;
                    Assert.Equal("/Patient", context.Request.Path.Value);
                    Assert.Equal("/stu3/partition/partition-a", context.Request.PathBase.Value);
                    if (throwInNext)
                    {
                        throw new InvalidOperationException("Downstream failure");
                    }

                    return Task.CompletedTask;
                },
                manager,
                Options.Create(new SecurityConfiguration { Enabled = false }),
                Substitute.For<ILogger<FhirRequestContextAfterAuthenticationMiddleware>>());
            var middleware = new FhirRequestContextMiddleware(
                context => afterAuthentication.Invoke(context, accessor),
                Substitute.For<ILogger<FhirRequestContextMiddleware>>(),
                manager);

            if (throwInNext)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.Invoke(httpContext, accessor, instanceConfiguration, () => "request-id"));
            }
            else
            {
                await middleware.Invoke(httpContext, accessor, instanceConfiguration, () => "request-id");
            }

            Assert.NotNull(capturedContext);
            Assert.Equal("partition-a", capturedContext.PartitionId);
            Assert.Equal(new Uri("https://api.example.com/stu3/partition/partition-a/Patient?code=123"), capturedContext.Uri);
            Assert.Equal(new Uri("https://api.example.com/stu3/partition/partition-a/"), capturedContext.BaseUri);
            Assert.Equal(new Uri("https://api.example.com/stu3/"), instanceConfiguration.BaseUri);
            Assert.Equal("/partition/partition-a/Patient", httpContext.Request.Path.Value);
            Assert.Equal("/stu3", httpContext.Request.PathBase.Value);
        }

        [Theory]
        [InlineData("/partition/partition-a/Patient")]
        [InlineData("/partition/unknown/Patient")]
        public async Task GivenPartitionPrefix_WhenAuthenticationHasNotRun_ThenPathIsNormalizedWithoutSelectingPartition(string path)
        {
            var configuration = new PartitionConfiguration();
            configuration.Partitions.Add(new PartitionInfo { Id = "partition-a", DatabaseName = "database-a" });
            HttpContext httpContext = CreateHttpContext();
            httpContext.Request.Path = new PathString(path);
            var accessor = new FhirRequestContextAccessor();
            bool nextCalled = false;
            var middleware = new FhirRequestContextMiddleware(
                context =>
                {
                    nextCalled = true;
                    Assert.Null(accessor.RequestContext.PartitionId);
                    Assert.Equal("/Patient", context.Request.Path.Value);
                    return Task.CompletedTask;
                },
                Substitute.For<ILogger<FhirRequestContextMiddleware>>(),
                new PartitionManager(Options.Create(configuration)));

            await middleware.Invoke(httpContext, accessor, new FhirServerInstanceConfiguration(), () => "request-id");

            Assert.True(nextCalled);
            Assert.Equal(path, httpContext.Request.Path.Value);
            Assert.Equal("/stu3", httpContext.Request.PathBase.Value);
        }

        [Fact]
        public void GivenPartitionContext_WhenCloning_ThenPartitionIdIsPreserved()
        {
            var context = new FhirRequestContext("GET", "https://localhost/partition/partition-a/Patient", "https://localhost/partition/partition-a/", "request-id", new HeaderDictionary(), new HeaderDictionary())
            {
                PartitionId = "partition-a",
                Principal = new ClaimsPrincipal(),
            };

            var clone = Assert.IsType<FhirRequestContext>(context.Clone());

            Assert.Equal("partition-a", clone.PartitionId);
            Assert.NotSame(context, clone);
        }

        private async Task<IFhirRequestContext> SetupAsync(HttpContext httpContext)
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            var instanceConfiguration = Substitute.For<IFhirServerInstanceConfiguration>();
            var fhirContextMiddlware = new FhirRequestContextMiddleware(next: (innerHttpContext) => Task.CompletedTask, Substitute.For<ILogger<FhirRequestContextMiddleware>>(), _partitionManager);
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
