// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.ActionResults
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class FhirResultTests
    {
        [Fact]
        public void GivenAGoneStatus_WhenReturningAResult_ThenTheContentShouldBeEmpty()
        {
            var result = FhirResult.Gone(NullLogger.Instance);
            var context = new ActionContext
            {
                HttpContext = new DefaultHttpContext(),
            };

            result.ExecuteResult(context);

            Assert.Null(result.Result);
            Assert.Equal(HttpStatusCode.Gone, result.StatusCode.GetValueOrDefault());
            Assert.Equal(0, context.HttpContext.Request.Body.Length);
        }

        [Fact]
        public void GivenANoContentStatus_WhenReturningAResult_ThenTheStatusCodeIsSetCorrectly()
        {
            var result = FhirResult.NoContent(NullLogger.Instance);
            var context = new ActionContext
            {
                HttpContext = new DefaultHttpContext(),
            };

            result.ExecuteResult(context);

            Assert.Null(result.Result);
            Assert.Equal(HttpStatusCode.NoContent, result.StatusCode.GetValueOrDefault());
        }

        [Fact]
        public void GivenANotFoundStatus_WhenReturningAResult_ThenTheStatusCodeIsSetCorrectly()
        {
            var result = FhirResult.NotFound(NullLogger.Instance);
            var context = new ActionContext
            {
                HttpContext = new DefaultHttpContext(),
            };

            result.ExecuteResult(context);

            Assert.Null(result.Result);
            Assert.Equal(HttpStatusCode.NotFound, result.StatusCode.GetValueOrDefault());
        }

        [Fact]
        public async Task GivenAFhirResult_WhenHeadersThatAlreadyExistsInResponseArePassed_ThenDuplicteHeadersAreRemoved()
        {
            var result = FhirResult.Gone(NullLogger.Instance);
            var context = new ActionContext
            {
                HttpContext = new DefaultHttpContext(),
            };

            IActionResultExecutor<ObjectResult> executor = Substitute.For<IActionResultExecutor<ObjectResult>>();
            executor.ExecuteAsync(Arg.Any<ActionContext>(), Arg.Any<ObjectResult>()).ReturnsForAnyArgs(Task.CompletedTask);

            ServiceCollection collection = new ServiceCollection();
            collection.AddSingleton<IActionResultExecutor<ObjectResult>>(executor);
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            collection.AddSingleton<RequestContextAccessor<IFhirRequestContext>>(contextAccessor);

            ServiceProvider provider = collection.BuildServiceProvider();
            context.HttpContext.RequestServices = provider;

            result.Headers["testKey1"] = "3";
            result.Headers["testKey2"] = "2";
            context.HttpContext.Response.Headers["testKey2"] = "1";

            await result.ExecuteResultAsync(context);

            Assert.Null(result.Result);
            Assert.Equal(HttpStatusCode.Gone, result.StatusCode.GetValueOrDefault());

            Assert.True(context.HttpContext.Response.Headers.ContainsKey("testKey2"));
            Assert.True(context.HttpContext.Response.Headers.ContainsKey("testKey1"));
            Assert.Equal(2, context.HttpContext.Response.Headers.Count);
            Assert.True(context.HttpContext.Response.Headers.TryGetValue("testKey1", out StringValues testKey1));
            Assert.True(context.HttpContext.Response.Headers.TryGetValue("testKey2", out StringValues testKey2));
            Assert.Equal(new StringValues("3"), testKey1);
            Assert.Equal(new StringValues("2"), testKey2);
        }

        [Fact]
        public async Task GivenDefaultHttpContext_WhenResponseHeadersAreModifiedConcurrently_ThenExceptionIsLogged()
        {
            ILogger logger = Substitute.For<ILogger>();
            var result = FhirResult.Gone(logger);
            var context = new ActionContext
            {
                HttpContext = new DefaultHttpContext(),
            };

            IHeaderDictionary responseHeaders = Substitute.For<IHeaderDictionary>();
            var exception = new InvalidOperationException("Collection was modified.");
            responseHeaders
                .When(headers => headers[Arg.Any<string>()] = Arg.Any<StringValues>())
                .Do(_ => throw exception);
            context.HttpContext.Features.Set<IHttpResponseFeature>(new HttpResponseFeature { Headers = responseHeaders });

            ServiceCollection collection = new ServiceCollection();
            collection.AddSingleton(Substitute.For<RequestContextAccessor<IFhirRequestContext>>());
            context.HttpContext.RequestServices = collection.BuildServiceProvider();
            result.Headers["test"] = "value";

            await result.ExecuteResultAsync(context);

            var logCall = Assert.Single(logger.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(ILogger.Log));
            Assert.Equal(LogLevel.Warning, logCall.GetArguments()[0]);
            Assert.Same(exception, logCall.GetArguments()[3]);
        }
    }
}
