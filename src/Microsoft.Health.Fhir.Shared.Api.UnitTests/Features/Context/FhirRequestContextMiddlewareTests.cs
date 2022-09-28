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
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
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

            Assert.Equal(new Uri("https://localhost:30/stu3"), fhirRequestContext.BaseUri);
        }

        [Fact]
        public async Task GivenAnHttpRequest_WhenExecutingFhirRequestContextMiddleware_ThenRequestIdHeaderShouldBeSet()
        {
            const string expectedRequestId = "123";

            HttpContext httpContext = CreateHttpContext();

            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            var fhirContextMiddlware = new FhirRequestContextMiddleware(next: (innerHttpContext) => Task.CompletedTask);
            string Provider() => expectedRequestId;

            await fhirContextMiddlware.Invoke(httpContext, fhirRequestContextAccessor, Provider);

            Assert.True(httpContext.Response.Headers.TryGetValue("X-Request-Id", out StringValues value));
            Assert.Equal(new StringValues(expectedRequestId), value);
        }

        private async Task<IFhirRequestContext> SetupAsync(HttpContext httpContext)
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            var fhirContextMiddlware = new FhirRequestContextMiddleware(next: (innerHttpContext) => Task.CompletedTask);
            string Provider() => Guid.NewGuid().ToString();

            await fhirContextMiddlware.Invoke(httpContext, fhirRequestContextAccessor, Provider);

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
