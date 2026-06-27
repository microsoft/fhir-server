// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Context
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class RequestConfigurationOverrideMiddlewareTests
    {
        private const string ContainmentKey = "EnableFhirDateContainment";

        [Fact]
        public async Task GivenTheGateIsDisabled_WhenAQueryOverrideIsSupplied_ThenItIsIgnoredAndNotStripped()
        {
            HttpContext httpContext = CreateHttpContext("?code=123&_config.EnableFhirDateContainment=true");
            IFhirRequestContext requestContext = new DefaultFhirRequestContext();
            bool nextCalled = false;

            await InvokeAsync(httpContext, requestContext, gateEnabled: false, () => nextCalled = true);

            Assert.True(nextCalled);

            // No override recorded.
            Assert.False(requestContext.TryGetRequestConfigurationOverride(ContainmentKey, out _));

            // Query string left untouched (the override parameter is NOT stripped).
            Assert.Contains("_config.EnableFhirDateContainment", httpContext.Request.QueryString.Value, System.StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenTheGateIsEnabled_WhenAQueryOverrideIsSupplied_ThenItIsRecordedAndStripped()
        {
            HttpContext httpContext = CreateHttpContext("?code=123&_config.EnableFhirDateContainment=true");
            IFhirRequestContext requestContext = new DefaultFhirRequestContext();

            await InvokeAsync(httpContext, requestContext, gateEnabled: true);

            Assert.True(requestContext.GetBooleanConfigurationOverride(ContainmentKey));

            // The override parameter is stripped, but other query parameters are preserved.
            string query = httpContext.Request.QueryString.Value;
            Assert.DoesNotContain("_config.", query, System.StringComparison.Ordinal);
            Assert.Contains("code=123", query, System.StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenTheGateIsEnabled_WhenAHeaderOverrideIsSupplied_ThenItIsRecordedAndQueryIsUnchanged()
        {
            HttpContext httpContext = CreateHttpContext("?code=123");
            httpContext.Request.Headers["X-FHIRServer-Config-EnableFhirDateContainment"] = "true";
            IFhirRequestContext requestContext = new DefaultFhirRequestContext();

            await InvokeAsync(httpContext, requestContext, gateEnabled: true);

            Assert.True(requestContext.GetBooleanConfigurationOverride(ContainmentKey));
            Assert.Equal("?code=123", httpContext.Request.QueryString.Value);
        }

        [Fact]
        public async Task GivenTheGateIsEnabled_WhenBothAQueryAndHeaderOverrideAreSupplied_ThenTheHeaderWins()
        {
            HttpContext httpContext = CreateHttpContext("?_config.EnableFhirDateContainment=false");
            httpContext.Request.Headers["X-FHIRServer-Config-EnableFhirDateContainment"] = "true";
            IFhirRequestContext requestContext = new DefaultFhirRequestContext();

            await InvokeAsync(httpContext, requestContext, gateEnabled: true);

            Assert.True(requestContext.GetBooleanConfigurationOverride(ContainmentKey));
        }

        [Fact]
        public async Task GivenTheGateIsEnabled_WhenNoOverridesAreSupplied_ThenNothingIsRecordedOrStripped()
        {
            HttpContext httpContext = CreateHttpContext("?code=123");
            IFhirRequestContext requestContext = new DefaultFhirRequestContext();

            await InvokeAsync(httpContext, requestContext, gateEnabled: true);

            Assert.False(requestContext.TryGetRequestConfigurationOverride(ContainmentKey, out _));
            Assert.Equal("?code=123", httpContext.Request.QueryString.Value);
        }

        private static async Task InvokeAsync(HttpContext httpContext, IFhirRequestContext requestContext, bool gateEnabled, System.Action onNext = null)
        {
            var accessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            accessor.RequestContext.Returns(requestContext);

            var configuration = new FhirServerConfiguration();
            configuration.Features.SupportsRequestConfigurationOverrides = gateEnabled;

            var middleware = new RequestConfigurationOverrideMiddleware(
                next: _ =>
                {
                    onNext?.Invoke();
                    return Task.CompletedTask;
                },
                Options.Create(configuration),
                Substitute.For<ILogger<RequestConfigurationOverrideMiddleware>>());

            await middleware.Invoke(httpContext, accessor);
        }

        private static HttpContext CreateHttpContext(string queryString)
        {
            HttpContext httpContext = new DefaultHttpContext();

            httpContext.Request.Method = "GET";
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("localhost", 30);
            httpContext.Request.PathBase = new PathString("/stu3");
            httpContext.Request.Path = new PathString("/Observation");
            httpContext.Request.QueryString = new QueryString(queryString);

            return httpContext;
        }
    }
}
