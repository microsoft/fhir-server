// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    [Trait(Traits.Category, Categories.Validate)]
    [Trait(Traits.Category, Categories.Web)]
    public class ValidateBulkImportRequestFilterAttributeTests
    {
        private const string CorrectPreferHeaderValue = "respond-async";
        private const string CorrectContentTypeHeaderValue = "application/fhir+json";
        private const string PreferHeaderName = "Prefer";

        private readonly ValidateImportRequestFilterAttribute _filter;

        public ValidateBulkImportRequestFilterAttributeTests()
        {
            _filter = new ValidateImportRequestFilterAttribute();
        }

        [Theory]
        [InlineData("respond-async, wait = 10")]
        [InlineData("return-content")]
        [InlineData("*")]
        public void GiveARequestWithInvalidPreferHeader_WhenGettingABulkImportOperationRequest_ThenARequestNotValidExceptionShouldBeThrown(string preferHeader)
        {
            var context = CreateContext();
            context.HttpContext.Request.Method = "GET";
            context.HttpContext.Request.Headers.Add(PreferHeaderName, preferHeader);
            context.HttpContext.Request.Headers.Add(HeaderNames.ContentType, CorrectContentTypeHeaderValue);

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
            context.HttpContext.Request.Headers.Add(PreferHeaderName, preferHeader);
            context.HttpContext.Request.Headers.Add(HeaderNames.ContentType, CorrectContentTypeHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Theory]
        [InlineData("respond-async, wait = 10")]
        [InlineData("return-content")]
        [InlineData("*")]
        public void GiveARequestWithInvalidPreferHeader_WhenCancelABulkImportRequest_ThenARequestNotValidExceptionShouldBeThrown(string preferHeader)
        {
            var context = CreateContext();
            context.HttpContext.Request.Method = "DELETE";
            context.HttpContext.Request.Headers.Add(PreferHeaderName, preferHeader);
            context.HttpContext.Request.Headers.Add(HeaderNames.ContentType, CorrectContentTypeHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenARequestWithNoPreferHeader_WhenGettingABulkImportOperationRequest_ThenARequestNotValidExceptionShouldBeThrown()
        {
            var context = CreateContext();
            context.HttpContext.Request.Method = "GET";
            context.HttpContext.Request.Headers.Add(HeaderNames.ContentType, CorrectContentTypeHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenARequestWithNoPreferHeader_WhenCreatingABulkImportRequest_ThenARequestNotValidExceptionShouldBeThrown()
        {
            var context = CreateContext();
            context.HttpContext.Request.Method = "POST";
            context.HttpContext.Request.Headers.Add(HeaderNames.ContentType, CorrectContentTypeHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenARequestWithNoPreferHeader_WhenCancelABulkImportRequest_ThenARequestNotValidExceptionShouldBeThrown()
        {
            var context = CreateContext();
            context.HttpContext.Request.Method = "DELETE";
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
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);
            context.HttpContext.Request.Headers.Add(HeaderNames.ContentType, contentTypeHeader);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenARequestWithNoContentTypeHeader_WhenCreatingABulkImportRequest_ThenARequestNotValidExceptionShouldBeThrown()
        {
            var context = CreateContext();
            context.HttpContext.Request.Method = "POST";
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenARequestWithNoContentTypeHeader_WhenGetABulkImportRequest_ThenTheResultIsSuccessful()
        {
            var context = CreateContext();
            context.HttpContext.Request.Method = "GET";
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            _filter.OnActionExecuting(context);
        }

        [Fact]
        public void GivenARequestWithNoContentTypeHeader_WhenCancelABulkImportRequest_ThenTheResultIsSuccessful()
        {
            var context = CreateContext();
            context.HttpContext.Request.Method = "DELETE";
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            _filter.OnActionExecuting(context);
        }

        [Fact]
        public void GivenARequestWithCorrectHeader_WhenCreatingABulkImportRequest_ThenTheResultIsSuccessful()
        {
            var context = CreateContext();
            context.HttpContext.Request.Method = "POST";
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
