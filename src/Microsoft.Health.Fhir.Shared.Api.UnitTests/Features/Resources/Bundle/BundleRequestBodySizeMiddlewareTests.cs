// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Resources.Bundle
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Bundle)]
    public class BundleRequestBodySizeMiddlewareTests
    {
        private const long DefaultKestrelLimit = 30_000_000;
        private const long BundleLimit = 52_428_800;

        private readonly IFailureMetricHandler _failureMetricHandler = Substitute.For<IFailureMetricHandler>();

        [Theory]
        [InlineData("", "/")]
        [InlineData("", "")]
        [InlineData("/fhir", "/fhir")]
        [InlineData("/fhir", "/fhir/")]
        public async Task GivenSizeRestrictionsEnabled_WhenABundleIsPosted_ThenTheBundleLimitIsApplied(string pathBase, string path)
        {
            (HttpContext context, TestBodySizeFeature feature) = CreateContext(HttpMethods.Post, path);

            await CreateMiddleware(enabled: true, BundleLimit, pathBase).Invoke(context);

            Assert.Equal(BundleLimit, feature.MaxRequestBodySize);
            Assert.Equal(BundleLimit, context.Items[BundleRequestBodySizeMiddleware.AppliedLimitItemKey]);
        }

        [Theory]
        [InlineData(false, BundleLimit, "POST", "/")]
        [InlineData(true, 0, "POST", "/")]
        [InlineData(true, BundleLimit, "POST", "/Patient")]
        [InlineData(true, BundleLimit, "PUT", "/")]
        [InlineData(true, BundleLimit, "GET", "/")]
        public async Task GivenANonBundleRequestOrDisabledRestrictions_WhenInvoked_ThenTheLimitIsUnchanged(bool enabled, long limit, string method, string path)
        {
            (HttpContext context, TestBodySizeFeature feature) = CreateContext(method, path);

            await CreateMiddleware(enabled, limit).Invoke(context);

            Assert.Equal(DefaultKestrelLimit, feature.MaxRequestBodySize);
            Assert.False(context.Items.ContainsKey(BundleRequestBodySizeMiddleware.AppliedLimitItemKey));
        }

        [Fact]
        public async Task GivenTheBodyWasAlreadyRead_WhenInvoked_ThenTheLimitIsUnchanged()
        {
            (HttpContext context, TestBodySizeFeature feature) = CreateContext(HttpMethods.Post, "/");
            feature.IsReadOnly = true;

            await CreateMiddleware(enabled: true, BundleLimit).Invoke(context);

            Assert.Equal(DefaultKestrelLimit, feature.MaxRequestBodySize);
            Assert.False(context.Items.ContainsKey(BundleRequestBodySizeMiddleware.AppliedLimitItemKey));
        }

        [Fact]
        public async Task GivenABundleRequest_WhenTheNextMiddlewareSucceeds_ThenNoMetricIsEmitted()
        {
            (HttpContext context, _) = CreateContext(HttpMethods.Post, "/");

            await CreateMiddleware(enabled: true, BundleLimit).Invoke(context);

            _failureMetricHandler.DidNotReceive().EmitException(Arg.Any<IExceptionMetricNotification>());
        }

        private BundleRequestBodySizeMiddleware CreateMiddleware(bool enabled, long limit, string pathBase = "", RequestDelegate next = null)
        {
            var bundleConfiguration = new BundleConfiguration { EnableSizeRestrictions = enabled, MaxBundleSizeBytes = limit };
            var fhirServerConfiguration = new FhirServerConfiguration { PathBase = pathBase };

            return new BundleRequestBodySizeMiddleware(
                next ?? (_ => Task.CompletedTask),
                Options.Create(bundleConfiguration),
                Options.Create(fhirServerConfiguration),
                _failureMetricHandler,
                Substitute.For<IFormatParametersValidator>(),
                NullLogger<BundleRequestBodySizeMiddleware>.Instance);
        }

        private static (HttpContext Context, TestBodySizeFeature Feature) CreateContext(string method, string path)
        {
            var feature = new TestBodySizeFeature { MaxRequestBodySize = DefaultKestrelLimit };
            var context = new DefaultHttpContext();
            context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
            context.Request.Method = method;
            context.Request.Path = path;

            return (context, feature);
        }

        private sealed class TestBodySizeFeature : IHttpMaxRequestBodySizeFeature
        {
            public bool IsReadOnly { get; set; }

            public long? MaxRequestBodySize { get; set; }
        }
    }
}
