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
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    public class ValidateBulkImportRequestFilterAttributeTests
    {
        private const string CorrectAcceptHeaderValue = ContentType.JSON_CONTENT_HEADER;
        private const string CorrectPreferHeaderValue = "respond-async";
        private const string CorrectContentTypeHeaderValue = "application/json";
        private const string PreferHeaderName = "Prefer";

        private readonly ValidateBulkImportRequestFilterAttribute _filter;

        public ValidateBulkImportRequestFilterAttributeTests()
        {
            _filter = new ValidateBulkImportRequestFilterAttribute();
        }

        [Theory]
        [InlineData("application/fhir+xml")]
        [InlineData("application/xml")]
        [InlineData("text/xml")]
        [InlineData("application/json")]
        [InlineData("*/*")]
        public void GivenARequestWithInvalidAcceptHeader_GettingABulkImportOperationRequest_ThenARequestNotValidExceptionShouldBeThrown(string acceptHeader)
        {
            var context = CreateContext();

            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, acceptHeader);
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Theory]
        [InlineData("application/fhir+xml")]
        [InlineData("application/xml")]
        [InlineData("text/xml")]
        [InlineData("application/json")]
        [InlineData("*/*")]
        public void GivenARequestWithInvalidAcceptHeader_CreatingABulkImportRequest_ThenARequestNotValidExceptionShouldBeThrown(string acceptHeader)
        {
            var context = CreateContext();
            context.HttpContext.Request.Method = "POST";
            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, acceptHeader);
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);
            context.HttpContext.Request.Headers.Add(HeaderNames.ContentType, CorrectContentTypeHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenARequestWithNoAcceptHeader_WhenGettingABulkImportOperationRequest_ThenARequestNotValidExceptionShouldBeThrown()
        {
            var context = CreateContext();
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenARequestWithNoAcceptHeader_WhenCreatingABulkImportRequest_ThenARequestNotValidExceptionShouldBeThrown()
        {
            var context = CreateContext();
            context.HttpContext.Request.Method = "POST";
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);
            context.HttpContext.Request.Headers.Add(HeaderNames.ContentType, CorrectContentTypeHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Theory]
        [InlineData("respond-async, wait = 10")]
        [InlineData("return-content")]
        [InlineData("*")]
        public void GiveARequestWithInvalidPreferHeader_WhenGettingABulkImportOperationRequest_ThenARequestNotValidExceptionShouldBeThrown(string preferHeader)
        {
            var context = CreateContext();
            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);
            context.HttpContext.Request.Headers.Add(PreferHeaderName, preferHeader);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Theory]
        [InlineData("respond-async, wait = 10")]
        [InlineData("return-content")]
        [InlineData("*")]
        public void GiveARequestWithInvalidPreferHeader_WhenCreatingABulkImportRequest_ThenARequestNotValidExceptionShouldBeThrown(string preferHeader)
        {
            var context = CreateContext();
            context.HttpContext.Request.Method = "POST";
            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);
            context.HttpContext.Request.Headers.Add(PreferHeaderName, preferHeader);
            context.HttpContext.Request.Headers.Add(HeaderNames.ContentType, CorrectContentTypeHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenARequestWithNoPreferHeader_WhenGettingABulkImportOperationRequest_ThenARequestNotValidExceptionShouldBeThrown()
        {
            var context = CreateContext();
            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenARequestWithNoPreferHeader_WhenCreatingABulkImportRequest_ThenARequestNotValidExceptionShouldBeThrown()
        {
            var context = CreateContext();
            context.HttpContext.Request.Method = "POST";
            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);
            context.HttpContext.Request.Headers.Add(HeaderNames.ContentType, CorrectContentTypeHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Theory]
        [InlineData("multipart/form-data")]
        [InlineData("text/plain")]
        [InlineData("text/html")]
        [InlineData("text/xml")]
        [InlineData("application/xhtml+xml")]
        [InlineData("application/xml")]
        [InlineData("*")]
        public void GiveARequestWithInvalidContentTypeHeader_WhenCreatingABulkImportRequest_ThenARequestNotValidExceptionShouldBeThrown(string contentTypeHeader)
        {
            var context = CreateContext();
            context.HttpContext.Request.Method = "POST";
            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);
            context.HttpContext.Request.Headers.Add(HeaderNames.ContentType, contentTypeHeader);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenARequestWithNoContentTypeHeader_WhenCreatingABulkImportRequest_ThenARequestNotValidExceptionShouldBeThrown()
        {
            var context = CreateContext();
            context.HttpContext.Request.Method = "POST";
            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenARequestWithCorrectHeader_WhenGettingAnBulkImportOperationRequest_ThenTheResultIsSuccessful()
        {
            var context = CreateContext();
            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            _filter.OnActionExecuting(context);
        }

        [Fact]
        public void GivenARequestWithCorrectHeader_WhenCreatingABulkImportRequest_ThenTheResultIsSuccessful()
        {
            var context = CreateContext();
            context.HttpContext.Request.Method = "POST";
            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);
            context.HttpContext.Request.Headers.Add(HeaderNames.ContentType, CorrectContentTypeHeaderValue);

            _filter.OnActionExecuting(context);
        }

        private static ActionExecutingContext CreateContext()
        {
            var context = new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>(),
                new Dictionary<string, object>(),
                FilterTestsHelper.CreateMockBulkImportController());

            return context;
        }
    }
}
