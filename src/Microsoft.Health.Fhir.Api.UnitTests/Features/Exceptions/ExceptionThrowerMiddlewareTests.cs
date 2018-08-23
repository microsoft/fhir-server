// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Api.Features.Exceptions;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Exceptions
{
    public class ExceptionThrowerMiddlewareTests
    {
        [Fact]
        public async Task WhenExecutingExceptionThrowerMiddleware_GivenNoQueryStringValue_NoExceptionShouldBeThrown()
        {
            var exceptionThrowerMiddleware = new ExceptionThrowerMiddleware((innerHttpContext) => Task.CompletedTask);

            await exceptionThrowerMiddleware.Invoke(new DefaultHttpContext());
        }

        [Fact]
        public async Task WhenExecutingExceptionThrowerMiddleware_GivenAMiddlewareQueryString_AnExceptionShouldBeThrown()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = new QueryString("?throw=middleware");

            var exceptionThrowerMiddleware = new ExceptionThrowerMiddleware(innerHttpContext => Task.CompletedTask);

            await Assert.ThrowsAsync<Exception>(async () => await exceptionThrowerMiddleware.Invoke(context));
        }

        [Fact]
        public async Task WhenExecutingExceptionThrowerMiddleware_GivenAnInternalQueryString_AnExceptionShouldBeThrownTheFirstTimeOnly()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = new QueryString("?throw=internal");

            var exceptionThrowerMiddleware = new ExceptionThrowerMiddleware(innerHttpContext => Task.CompletedTask);

            await Assert.ThrowsAsync<Exception>(async () => await exceptionThrowerMiddleware.Invoke(context));

            await exceptionThrowerMiddleware.Invoke(context);
        }
    }
}
