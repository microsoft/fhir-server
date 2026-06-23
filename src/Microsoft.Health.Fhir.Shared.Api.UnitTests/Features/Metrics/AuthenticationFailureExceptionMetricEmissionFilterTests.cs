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
        public void GivenSecurityTokenException_AndNullHttpContext_ShouldEmitReturnsFalse()
        {
            // The filter must work even when the log record is emitted outside an HTTP request scope
            // and even when the response status code has not yet been written (the flood-path scenario,
            // where the log fires from inside token introspection before the 401 challenge runs).
            Assert.False(_filter.ShouldEmit(new SecurityTokenException("expired"), httpContext: null));
        }

        [Fact]
        public void GivenSecurityTokenException_ShouldEmitReturnsFalse()
        {
            Assert.False(_filter.ShouldEmit(new SecurityTokenException("expired"), new DefaultHttpContext()));
        }

        [Fact]
        public void GivenSecurityTokenExceptionAsInner_ShouldEmitReturnsFalse()
        {
            var inner = new SecurityTokenException("expired");
            var exception = new InvalidOperationException("wrapper", inner);
            Assert.False(_filter.ShouldEmit(exception, new DefaultHttpContext()));
        }

        [Fact]
        public void GivenSecurityTokenExceptionInAggregateInnerExceptions_ShouldEmitReturnsFalse()
        {
            var exception = new AggregateException(
                new InvalidOperationException("first"),
                new SecurityTokenException("expired"));

            Assert.False(_filter.ShouldEmit(exception, new DefaultHttpContext()));
        }

        [Theory]
        [InlineData(HttpStatusCode.OK)]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public void GivenSecurityTokenException_IsSuppressedRegardlessOfStatusCode(HttpStatusCode statusCode)
        {
            // The flood log is emitted before the 401 challenge writes the response, so a status-code
            // condition would never match the flood. Suppression is keyed on the exception type alone.
            var context = new DefaultHttpContext();
            context.Response.StatusCode = (int)statusCode;

            Assert.False(_filter.ShouldEmit(new SecurityTokenException("expired"), context));
        }

        [Fact]
        public void GivenNonAuthenticationException_ShouldEmitReturnsTrue()
        {
            Assert.True(_filter.ShouldEmit(new InvalidOperationException("not auth"), new DefaultHttpContext()));
        }

        [Fact]
        public void GivenSubclassThatRecognizesAdditionalType_ShouldEmitReturnsFalseForExtendedType()
        {
            var extended = new ExtendedFilter();
            Assert.False(extended.ShouldEmit(new SimulatedPaasAuthException(), new DefaultHttpContext()));
            Assert.False(extended.ShouldEmit(new SecurityTokenException("expired"), new DefaultHttpContext()));
            Assert.True(extended.ShouldEmit(new InvalidOperationException("other"), new DefaultHttpContext()));
        }

        private sealed class ExtendedFilter : AuthenticationFailureExceptionMetricEmissionFilter
        {
            protected override bool IsAuthenticationException(Exception exception)
            {
                if (base.IsAuthenticationException(exception))
                {
                    return true;
                }

                for (Exception current = exception; current != null; current = current.InnerException)
                {
                    if (current is SimulatedPaasAuthException)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private sealed class SimulatedPaasAuthException : Exception
        {
        }
    }
}
