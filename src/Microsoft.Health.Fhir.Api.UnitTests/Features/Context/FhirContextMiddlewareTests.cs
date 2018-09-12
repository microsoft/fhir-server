// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Context
{
    public class FhirContextMiddlewareTests
    {
        [Fact]
        public async Task WhenExecutingFhirContextMiddleware_GivenAnHttpContext_TheFhirContextObjectShouldBeInitialized()
        {
            IFhirContextAccessor fhirContextAccessor = await SetupAsync(new DefaultHttpContext());

            Assert.Equal(ValueSets.AuditEventType.RestFulOperation.System, fhirContextAccessor.FhirContext.RequestType.System);
            Assert.Equal(ValueSets.AuditEventType.RestFulOperation.Code, fhirContextAccessor.FhirContext.RequestType.Code);
        }

        [Fact]
        public async Task WhenExecutingFhirContextMiddleware_GivenAnHttpRequest_TheFhirContextObjectShouldBeInitialized()
        {
            HttpContext httpContext = new DefaultHttpContext();

            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("localhost", 30);
            httpContext.Request.PathBase = new PathString("/stu3");

            IFhirContextAccessor fhirContextAccessor = await SetupAsync(httpContext);

            Assert.Equal(new Uri("https://localhost:30/stu3"), fhirContextAccessor.FhirContext.BaseUri);
        }

        private async Task<IFhirContextAccessor> SetupAsync(HttpContext httpContext)
        {
            var fhirContextAccessor = Substitute.For<IFhirContextAccessor>();
            var fhirContextMiddlware = new FhirContextMiddleware(next: (innerHttpContext) => Task.CompletedTask);
            string Provider() => Guid.NewGuid().ToString();

            await fhirContextMiddlware.Invoke(httpContext, fhirContextAccessor, Provider);

            Assert.NotNull(fhirContextAccessor.FhirContext);

            return fhirContextAccessor;
        }
    }
}
