// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Api;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using ApiResources = Microsoft.Health.Fhir.Api.Resources;

namespace Microsoft.Health.Fhir.Azure.UnitTests.Api
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchPostReroutingMiddlewareTests
    {
        private readonly SearchPostReroutingMiddleware _middleware;
        private readonly HttpContext _httpContext;
        private readonly RequestDelegate _requestDelegate;

        public SearchPostReroutingMiddlewareTests()
        {
            _httpContext = new DefaultHttpContext();
            _requestDelegate = Substitute.For<RequestDelegate>();
            _middleware = new SearchPostReroutingMiddleware(_requestDelegate, new NullLogger<SearchPostReroutingMiddleware>());
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData("application/x-www-form-urlencoded", true)]
        [InlineData("application/x-www-form-urlencoded;", true)]
        [InlineData("application/x-www-form-urlencoded;charset=UTF-8", true)]
        [InlineData("  application/x-www-form-urlencoded   ", true)]
        [InlineData("text/json", false)]
        [InlineData("application/x-www-form-urlencodedx", false)]
        [InlineData("abc", false)]
        [InlineData("", false)]
        public async Task GivenSearchRequestViaPost_WhenContentTypeIsSpecified_InvalidContentTypeShouldBeRejected(string contentType, bool valid)
        {
            _httpContext.Request.Method = HttpMethods.Post;
            _httpContext.Request.ContentType = contentType;
            _httpContext.Request.Scheme = "https";
            _httpContext.Request.Host = new HostString("localhost", 44348);
            _httpContext.Request.Path = new PathString("/Patient/_search");
            _httpContext.Response.Body = new MemoryStream();

            await _middleware.Invoke(_httpContext);
            if (valid)
            {
                await _requestDelegate.Received(1).Invoke(Arg.Any<HttpContext>());
            }
            else
            {
                await _requestDelegate.DidNotReceive().Invoke(Arg.Any<HttpContext>());

                _httpContext.Response.Body.Position = 0;
                using var reader = new StreamReader(_httpContext.Response.Body);
                var body = reader.ReadToEnd();
                var expectedString = ApiResources.ContentTypeFormUrlEncodedExpected.Replace("\"", "\\\"");
                expectedString = $"\"{expectedString}\"";
                Assert.Contains(expectedString, body);
                Assert.Equal((int)HttpStatusCode.BadRequest, _httpContext.Response.StatusCode);
            }
        }
    }
}
