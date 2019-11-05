// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.Bundle;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Bundle
{
    public class BundleAwareJwtBearerHandlerTests
    {
        private readonly BundleAwareJwtBearerHandler _bundleAwareJwtBearerHandler;
        private readonly DefaultHttpContext _httpContext;
        private readonly IBundleHttpContextAccessor _bundleHttpContextAccessor;

        public BundleAwareJwtBearerHandlerTests()
        {
            var jwtBearerOptions = new JwtBearerOptions();
            var options = Substitute.For<IOptionsMonitor<JwtBearerOptions>>();
            options.CurrentValue.Returns(jwtBearerOptions);
            var logger = NullLoggerFactory.Instance;
            var encoder = UrlEncoder.Default;
            var dataProtection = Substitute.For<IDataProtectionProvider>();
            var clock = Substitute.For<ISystemClock>();
            _bundleHttpContextAccessor = Substitute.For<IBundleHttpContextAccessor>();
            _httpContext = new DefaultHttpContext();

            _bundleAwareJwtBearerHandler = new BundleAwareJwtBearerHandler(options, logger, encoder, dataProtection, clock, _bundleHttpContextAccessor);
            _bundleAwareJwtBearerHandler.InitializeAsync(new AuthenticationScheme("jwt", "jwt", typeof(BundleAwareJwtBearerHandler)), _httpContext);
        }

        [Fact]
        public async Task GivenAnEmptyBundleHttpContextAccessor_WhenHandlingForbidden_StatusCodeShouldBeSet()
        {
            await _bundleAwareJwtBearerHandler.ForbidAsync(new AuthenticationProperties());

            Assert.Equal(403, _httpContext.Response.StatusCode);
        }

        [Fact]
        public async Task GivenAnFilledBundleHttpContextAccessor_WhenHandlingForbidden_BothStatusCodesShouldBeSet()
        {
            _bundleHttpContextAccessor.HttpContext.Returns(new DefaultHttpContext());

            await _bundleAwareJwtBearerHandler.ForbidAsync(new AuthenticationProperties());

            Assert.Equal(403, _httpContext.Response.StatusCode);
            Assert.Equal(403, _bundleHttpContextAccessor.HttpContext.Response.StatusCode);
        }
    }
}
