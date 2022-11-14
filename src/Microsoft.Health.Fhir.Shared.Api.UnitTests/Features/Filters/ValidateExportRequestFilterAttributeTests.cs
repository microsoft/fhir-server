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
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    [Trait(Traits.Category, Categories.Validate)]
    [Trait(Traits.Category, Categories.Web)]
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

        [InlineData(KnownQueryParameterNames.AnonymizationConfigurationLocation)]
        [InlineData(KnownQueryParameterNames.AnonymizationConfigurationLocation, KnownQueryParameterNames.AnonymizationConfigurationFileEtag)]
        [Theory]
        public void GivenARequestWithAnonymizedExportQueryParam_WhenGettingAnDefaultExportOperationRequest_ThenTheResultIsSuccessful(params string[] queryParamNames)
        {
            var context = CreateContext();
            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            var queryParams = new Dictionary<string, StringValues>();
            foreach (string queryParamName in queryParamNames)
            {
                queryParams.Add(queryParamName, "test");
            }

            context.HttpContext.Request.Query = new QueryCollection(queryParams);
            context.HttpContext.Request.Path = new PathString("/$export");

            _filter.OnActionExecuting(context);
        }

        [InlineData(KnownQueryParameterNames.Since)]
        [InlineData(KnownQueryParameterNames.Type)]
        [InlineData(KnownQueryParameterNames.Since, KnownQueryParameterNames.Type)]
        [Theory]
        public void GivenARequestWithCorrectHeaderAndSupportedQueryParam_WhenGettingAnExportOperationRequest_ThenTheResultIsSuccessful(params string[] queryParamNames)
        {
            var context = CreateContext();
            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            var queryParams = new Dictionary<string, StringValues>();
            foreach (string queryParamName in queryParamNames)
            {
                queryParams.Add(queryParamName, "test");
            }

            context.HttpContext.Request.Query = new QueryCollection(queryParams);

            _filter.OnActionExecuting(context);
        }

        [InlineData("application/fhir+ndjson")]
        [InlineData("application/ndjson")]
        [InlineData("ndjson")]
        [Theory]
        public void GivenARequestWithCorrectHeaderAndSupportedOutputFormatQueryParam_WhenGettingAnExportOperationRequest_ThenTheResultIsSuccessful(string outputFormat)
        {
            var context = CreateContext();
            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            var queryParams = new Dictionary<string, StringValues>();
            queryParams.Add(KnownQueryParameterNames.OutputFormat, outputFormat);

            context.HttpContext.Request.Query = new QueryCollection(queryParams);

            _filter.OnActionExecuting(context);
        }

        [Fact]
        public void GivenARequestWithCorrectHeaderAndUnsupportedOutputFormatQueryParam_WhenGettingAnExportOperationRequest_ThenARequestNotValidExceptionShouldBeThrown()
        {
            var context = CreateContext();
            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            var queryParams = new Dictionary<string, StringValues>();
            queryParams.Add(KnownQueryParameterNames.OutputFormat, "invalid");

            context.HttpContext.Request.Query = new QueryCollection(queryParams);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
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
