// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Net.Http.Headers;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    public class ValidateExportRequestFilterAttributeTests
    {
        private const string CorrectAcceptHeaderValue = ContentType.JSON_CONTENT_HEADER;
        private const string CorrectPreferHeaderValue = "respond-async";
        private const string PreferHeaderName = "Prefer";
        private const string SupportedDestinationType = "AnySupportedDestinationType";

        private readonly IExportDestinationClientFactory _exportDestinationClientFactory = Substitute.For<IExportDestinationClientFactory>();
        private readonly ValidateExportRequestFilterAttribute _filter;

        public ValidateExportRequestFilterAttributeTests()
        {
            _exportDestinationClientFactory.IsSupportedDestinationType(SupportedDestinationType).Returns(true);

            _filter = new ValidateExportRequestFilterAttribute(_exportDestinationClientFactory);
        }

        [Theory]
        [InlineData("application/fhir+xml")]
        [InlineData("application/xml")]
        [InlineData("text/xml")]
        [InlineData("application/json")]
        [InlineData("*/*")]
        public void GiveARequestWithInvalidAcceptHeader_WhenGettingAnExportOperationRequest_ThenARequestNotValidExceptionShouldBeThrown(string acceptHeader)
        {
            var context = CreateContextWithParams();

            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, acceptHeader);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Fact]
        public void GiveARequestWithNoAcceptHeader_WhenGettingAnExportOperationRequest_ThenARequestNotValidExceptionShouldBeThrown()
        {
            var context = CreateContextWithParams();

            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Theory]
        [InlineData("respond-async, wait = 10")]
        [InlineData("return-content")]
        [InlineData("*")]
        public void GiveARequestWithInvalidPreferHeader_WhenGettingAnExportOperationRequest_ThenARequestNotValidExceptionShouldBeThrown(string preferHeader)
        {
            var context = CreateContextWithParams();

            context.HttpContext.Request.Headers.Add(PreferHeaderName, preferHeader);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Fact]
        public void GiveARequestWithNoPreferHeader_WhenGettingAnExportOperationRequest_ThenARequestNotValidExceptionShouldBeThrown()
        {
            var context = CreateContextWithParams();

            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Fact]
        public void GiveARequestWithValidAcceptAndPreferHeader_WhenGettingAnExportOperationRequest_ThenTheResultIsSuccessful()
        {
            var context = CreateContextWithParams();

            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            _filter.OnActionExecuting(context);
        }

        [Fact]
        public void GivenARequestWithCorrectHeadersAndMissingDestinationTypeParam_WhenGettingAnExportOperationRequest_ThenARequestNotValidExceptionShouldBeThrown()
        {
            var queryParams = new Dictionary<string, StringValues>()
            {
                { KnownQueryParameterNames.DestinationConnectionSettings, "destination" },
            };

            var context = CreateContextWithParams(queryParams);

            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenARequestWithCorrectHeadersAndMissingDestinationConnectionParam_WhenGettingAnExportOperationRequest_ThenARequestNotValidExceptionShouldBeThrown()
        {
            var queryParams = new Dictionary<string, StringValues>()
            {
                { KnownQueryParameterNames.DestinationType, SupportedDestinationType },
            };

            var context = CreateContextWithParams(queryParams);

            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenARequestWithCorrectHeadersAndDestinationTypeThatIsNotSupported_WhenGettingAnExportOperationRequest_ThenARequestNotValidExceptionShouldBeThrown()
        {
            var queryParams = new Dictionary<string, StringValues>()
            {
                { KnownQueryParameterNames.DestinationType, "Azure" },
                { KnownQueryParameterNames.DestinationConnectionSettings, "destination" },
            };

            var context = CreateContextWithParams(queryParams);

            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenARequestWithCorrectHeadersAndUnsupportedQueryParameter_WhenGettingAnExportOperationRequest_ThenARequestNotValidExceptionShouldBeThrown()
        {
            var queryParams = new Dictionary<string, StringValues>()
            {
                { KnownQueryParameterNames.DestinationType, SupportedDestinationType },
                { KnownQueryParameterNames.DestinationConnectionSettings, "destination" },
                { KnownQueryParameterNames.Since, "forever" },
            };

            var context = CreateContextWithParams(queryParams);

            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, CorrectAcceptHeaderValue);
            context.HttpContext.Request.Headers.Add(PreferHeaderName, CorrectPreferHeaderValue);

            Assert.Throws<RequestNotValidException>(() => _filter.OnActionExecuting(context));
        }

        private static ActionExecutingContext CreateContextWithParams(Dictionary<string, StringValues> queryParams = null)
        {
            var context = new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>(),
                new Dictionary<string, object>(),
                FilterTestsHelper.CreateMockExportController());

            if (queryParams == null)
            {
                queryParams = new Dictionary<string, StringValues>();
                queryParams.Add(KnownQueryParameterNames.DestinationType, SupportedDestinationType);
                queryParams.Add(KnownQueryParameterNames.DestinationConnectionSettings, "connectionString");
            }

            context.HttpContext.Request.Query = new QueryCollection(queryParams);
            return context;
        }
    }
}
