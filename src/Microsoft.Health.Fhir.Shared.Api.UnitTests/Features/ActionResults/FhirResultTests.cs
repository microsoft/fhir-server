// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
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
            var result = FhirResult.Gone();
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
            var result = FhirResult.NoContent();
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
            var result = FhirResult.NotFound();
            var context = new ActionContext
            {
                HttpContext = new DefaultHttpContext(),
            };

            result.ExecuteResult(context);

            Assert.Null(result.Result);
            Assert.Equal(HttpStatusCode.NotFound, result.StatusCode.GetValueOrDefault());
        }

        [Fact]
        public void GivenAFhirResult_WhenHeadersThatAlreadyExistsInResponseArePassed_ThenDuplicteHeadersAreRemoved()
        {
            var result = FhirResult.Gone();
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

            result.Headers.Add("testKey1", "3");
            result.Headers.Add("testKey2", "2");
            context.HttpContext.Response.Headers.Add("testKey2", "1");

            result.ExecuteResultAsync(context);

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
    }
}
