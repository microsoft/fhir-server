// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.Metrics;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Metrics
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class SecurityAbuseExceptionMetricEmissionFilterTests
    {
        private readonly SecurityAbuseExceptionMetricEmissionFilter _filter = new();

        [Fact]
        public void GivenNullException_ShouldEmitReturnsTrue()
        {
            Assert.True(_filter.ShouldEmit(exception: null, new DefaultHttpContext()));
        }

        [Fact]
        public void GivenServerSideRequestForgeryException_ShouldEmitReturnsFalse()
        {
            Assert.False(_filter.ShouldEmit(new ServerSideRequestForgeryException("blocked"), new DefaultHttpContext()));
        }

        [Fact]
        public void GivenServerSideRequestForgeryExceptionAsInner_ShouldEmitReturnsFalse()
        {
            var inner = new ServerSideRequestForgeryException("blocked");
            var exception = new InvalidOperationException("wrapper", inner);
            Assert.False(_filter.ShouldEmit(exception, new DefaultHttpContext()));
        }

        [Fact]
        public void GivenServerSideRequestForgeryExceptionInAggregateInnerExceptions_ShouldEmitReturnsFalse()
        {
            var exception = new AggregateException(
                new InvalidOperationException("first"),
                new ServerSideRequestForgeryException("blocked"));

            Assert.False(_filter.ShouldEmit(exception, new DefaultHttpContext()));
        }

        [Fact]
        public void GivenServerSideRequestForgeryException_AndNullHttpContext_ShouldEmitReturnsFalse()
        {
            // The filter does not require an HttpContext — the exception type alone is sufficient.
            Assert.False(_filter.ShouldEmit(new ServerSideRequestForgeryException("blocked"), httpContext: null));
        }

        [Fact]
        public void GivenUnrelatedException_ShouldEmitReturnsTrue()
        {
            Assert.True(_filter.ShouldEmit(new InvalidOperationException("not security"), new DefaultHttpContext()));
        }

        [Theory]
        [InlineData(HttpStatusCode.OK)]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public void GivenServerSideRequestForgeryException_IsSuppressedRegardlessOfStatusCode(HttpStatusCode statusCode)
        {
            var context = new DefaultHttpContext();
            context.Response.StatusCode = (int)statusCode;
            Assert.False(_filter.ShouldEmit(new ServerSideRequestForgeryException("blocked"), context));
        }

        [Fact]
        public void GivenSubclassThatRecognizesAdditionalType_ShouldEmitReturnsFalseForExtendedType()
        {
            var extended = new ExtendedFilter();
            Assert.False(extended.ShouldEmit(new SimulatedPaasAbuseException(), new DefaultHttpContext()));
            Assert.False(extended.ShouldEmit(new ServerSideRequestForgeryException("blocked"), new DefaultHttpContext()));
            Assert.True(extended.ShouldEmit(new InvalidOperationException("other"), new DefaultHttpContext()));
        }

        private sealed class ExtendedFilter : SecurityAbuseExceptionMetricEmissionFilter
        {
            protected override bool IsSecurityAbuseException(Exception exception)
            {
                if (base.IsSecurityAbuseException(exception))
                {
                    return true;
                }

                for (Exception current = exception; current != null; current = current.InnerException)
                {
                    if (current is SimulatedPaasAbuseException)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private sealed class SimulatedPaasAbuseException : Exception
        {
        }
    }
}
