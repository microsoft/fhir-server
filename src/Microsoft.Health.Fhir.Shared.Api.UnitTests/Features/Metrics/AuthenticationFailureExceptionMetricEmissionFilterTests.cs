// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.Metrics;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Metrics
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class AuthenticationFailureExceptionMetricEmissionFilterTests
    {
        private readonly AuthenticationFailureExceptionMetricEmissionFilter _filter = new();

        [Fact]
        public void GivenNullException_ShouldEmitReturnsTrue()
        {
            Assert.True(_filter.ShouldEmit(exception: null, new DefaultHttpContext()));
        }

        [Fact]
        public void GivenNullHttpContext_ShouldEmitReturnsTrue()
        {
            Assert.True(_filter.ShouldEmit(new SecurityTokenException("expired"), httpContext: null));
        }

        [Fact]
        public void GivenSecurityTokenException_When401_ShouldEmitReturnsFalse()
        {
            var context = new DefaultHttpContext();
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;

            Assert.False(_filter.ShouldEmit(new SecurityTokenException("expired"), context));
        }

        [Fact]
        public void GivenSecurityTokenExceptionAsInner_When401_ShouldEmitReturnsFalse()
        {
            var inner = new SecurityTokenException("expired");
            var exception = new InvalidOperationException("wrapper", inner);
            var context = new DefaultHttpContext();
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;

            Assert.False(_filter.ShouldEmit(exception, context));
        }

        [Theory]
        [InlineData(HttpStatusCode.OK)]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public void GivenSecurityTokenException_WhenStatusIsNot401_ShouldEmitReturnsTrue(HttpStatusCode statusCode)
        {
            var context = new DefaultHttpContext();
            context.Response.StatusCode = (int)statusCode;

            Assert.True(_filter.ShouldEmit(new SecurityTokenException("expired"), context));
        }

        [Fact]
        public void GivenNonAuthenticationException_When401_ShouldEmitReturnsTrue()
        {
            var context = new DefaultHttpContext();
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;

            Assert.True(_filter.ShouldEmit(new InvalidOperationException("not auth"), context));
        }
    }
}
