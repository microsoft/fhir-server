// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    public class ValidateExportRequestFilterAttributeTests
    {
        private const string CorrectAcceptHeaderValue = ContentType.JSON_CONTENT_HEADER;
        private const string CorrectPreferHeaderValue = "respond-async";
        private const string PreferHeaderName = "Prefer";

        private readonly ValidateExportRequestFilterAttribute _filter;

        public ValidateExportRequestFilterAttributeTests()
        {
            _filter = new ValidateExportRequestFilterAttribute();
        }

        [Theory]
        [InlineData("application/fhir+xml")]
        [InlineData("application/xml")]
        [InlineData("text/xml")]
        [InlineData("application/json")]
        [InlineData("*/*")]
        public void GivenARequestWithInvalidAcceptHeader_WhenGettingAnExportOperationRequest_ThenARequestNotValidExceptionShouldBeThrown(string acceptHeader)
        {
            var context = CreateContext();

            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, acceptHeader);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenARequestWithNoAcceptHeader_WhenGettingAnExportOperationRequest_ThenARequestNotValidExceptionShouldBeThrown()
        {
            var context = CreateContext();

            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Theory]
        [InlineData("respond-async, wait = 10")]
        [InlineData("return-content")]
        [InlineData("*")]
        public void GiveARequestWithInvalidPreferHeader_WhenGettingAnExportOperationRequest_ThenARequestNotValidExceptionShouldBeThrown(string preferHeader)
        {
            var context = CreateContext();

            context.HttpContext.Request.Headers.Add(PreferHeaderName, preferHeader);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenARequestWithNoPreferHeader_WhenGettingAnExportOperationRequest_ThenARequestNotValidExceptionShouldBeThrown()
        {
            var context = CreateContext();

            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [InlineData("since")]
        [InlineData("_SINCE")]
        [InlineData("queryParam")]
        [Theory]
        public void GivenARequestWithCorrectHeadersAndUnsupportedQueryParam_WhenGettingAnExportOperationRequest_ThenARequestNotValidExceptionShouldBeThrown(string queryParamName)
        {
            var context = CreateContext();
            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            var queryParams = new Dictionary<string, StringValues>()
            {
                { queryParamName, "paramValue" },
            };
            context.HttpContext.Request.Query = new QueryCollection(queryParams);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenARequestWithCorrectHeaderAndSinceQueryParam_WhenGettingAnExportOperationRequest_ThenTheResultIsSuccessful()
        {
            var context = CreateContext();
            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            var queryParams = new Dictionary<string, StringValues>()
            {
                { KnownQueryParameterNames.Since, PartialDateTime.MinValue.ToString() },
            };
            context.HttpContext.Request.Query = new QueryCollection(queryParams);

            _filter.OnActionExecuting(context);
        }

        [Fact]
        public void GivenARequestWithCorrectHeaderAndNoQueryParams_WhenGettingAnExportOperationRequest_ThenTheResultIsSuccessful()
        {
            var context = CreateContext();
            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            _filter.OnActionExecuting(context);
        }

        private static ActionExecutingContext CreateContext()
        {
            var context = new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>(),
                new Dictionary<string, object>(),
                FilterTestsHelper.CreateMockExportController());

            return context;
        }
    }
}
