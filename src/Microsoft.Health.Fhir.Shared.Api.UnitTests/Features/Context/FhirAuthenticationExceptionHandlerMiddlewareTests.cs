// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Context
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class FhirAuthenticationExceptionHandlerMiddlewareTests
    {
        [Fact]
        public async Task Invoke_WhenNextMiddlewareThrowsExceptionWithInvalidAudience_ShouldThrowSecurityTokenInvalidAudienceException()
        {
            var ex = new Exception("Invalid audience", new SecurityTokenInvalidAudienceException("Invalid audience"));
            var context = new DefaultHttpContext();
            var next = Substitute.For<RequestDelegate>();
            next.Invoke(context).Throws(ex);
            var middleware = new FhirAuthenticationExceptionHandlerMiddleware(next);
            await Assert.ThrowsAsync<SecurityTokenInvalidAudienceException>(() => middleware.Invoke(context));
        }

        [Fact]
        public async Task Invoke_WhenNextMiddlewareThrowsExceptionWithInvalidIssuer_ShouldThrowSecurityTokenInvalidIssuerException()
        {
            var ex = new Exception("Invalid issuer", new SecurityTokenInvalidIssuerException("Invalid issuer"));
            var context = new DefaultHttpContext();
            var next = Substitute.For<RequestDelegate>();
            next.Invoke(context).Throws(ex);
            var middleware = new FhirAuthenticationExceptionHandlerMiddleware(next);
            await Assert.ThrowsAsync<SecurityTokenInvalidIssuerException>(() => middleware.Invoke(context));
        }

        [Fact]
        public async Task Invoke_WhenNextMiddlewareThrowsExceptionWithInvalidIssuerAndNoParentException_ShouldThrowSecurityTokenInvalidIssuerException()
        {
            var ex = new SecurityTokenInvalidIssuerException("Invalid issuer");
            var context = new DefaultHttpContext();
            var next = Substitute.For<RequestDelegate>();
            next.Invoke(context).Throws(ex);
            var middleware = new FhirAuthenticationExceptionHandlerMiddleware(next);
            await Assert.ThrowsAsync<SecurityTokenInvalidIssuerException>(() => middleware.Invoke(context));
        }

        [Fact]
        public async Task Invoke_WhenInnerExceptionThrowsExceptionWithInvalidIssuer_ShouldThrowSecurityTokenInvalidIssuerException()
        {
            var ex = new Exception(
                "Parent",
                new Exception(
                    "Parent",
                    new Exception(
                        "Parent",
                        new Exception(
                            "Parent",
                            new SecurityTokenInvalidIssuerException(
                                "Invalid issuer")))));
            var context = new DefaultHttpContext();
            var next = Substitute.For<RequestDelegate>();
            next.Invoke(context).Throws(ex);
            var middleware = new FhirAuthenticationExceptionHandlerMiddleware(next);

            await Assert.ThrowsAsync<SecurityTokenInvalidIssuerException>(() => middleware.Invoke(context));
        }

        [Fact]
        public async Task Invoke_WhenNextMiddlewareThrowsExceptionWithOtherInnerException_ShouldRethrowException()
        {
            var ex = new Exception("Some error", new Exception("Some error"));
            var context = new DefaultHttpContext();
            var next = Substitute.For<RequestDelegate>();
            next.Invoke(context).Throws(ex);
            var middleware = new FhirAuthenticationExceptionHandlerMiddleware(next);

            await Assert.ThrowsAsync<Exception>(() => middleware.Invoke(context));
        }

        [Fact]
        public async Task Invoke_WhenNextMiddlewareThrowsExceptionWithoutInnerException_ShouldRethrowException()
        {
            var ex = new InvalidOperationException("Some error without inner exception");
            var context = new DefaultHttpContext();
            var next = Substitute.For<RequestDelegate>();
            next.Invoke(context).Throws(ex);
            var middleware = new FhirAuthenticationExceptionHandlerMiddleware(next);

            await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.Invoke(context));
        }

        [Fact]
        public async Task Invoke_WhenInnerNextMiddlewareThrowsExceptionWithOtherInnerException_ShouldRethrowException()
        {
            var ex = new InvalidOperationException(
                "Parent",
                new Exception(
                    "Parent",
                    new Exception(
                        "Parent",
                        new Exception(
                            "Parent",
                            new Exception(
                                "Some error...")))));
            var context = new DefaultHttpContext();
            var next = Substitute.For<RequestDelegate>();
            next.Invoke(context).Throws(ex);
            var middleware = new FhirAuthenticationExceptionHandlerMiddleware(next);

            await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.Invoke(context));
        }

        [Fact]
        public async Task Invoke_WhenNextMiddlewareDoesNotThrow_ShouldCallNextMiddleware()
        {
            var context = new DefaultHttpContext();
            var next = Substitute.For<RequestDelegate>();
            next.Invoke(context).Returns(Task.CompletedTask);
            var middleware = new FhirAuthenticationExceptionHandlerMiddleware(next);
            await middleware.Invoke(context);

            Assert.Equal(next.Invoke(context), Task.CompletedTask);
        }
    }
}
