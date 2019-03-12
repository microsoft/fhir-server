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
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    public class ValidateExportHeadersFilterAttributeTests
    {
        private string _correctAcceptHeaderValue = "application/fhir+json";
        private string _correctPreferHeaderValue = "respond-async";
        private string _preferHeaderName = "Prefer";

        [Theory]
        [InlineData("application/fhir+xml")]
        [InlineData("application/xml")]
        [InlineData("text/xml")]
        [InlineData("application/json")]
        [InlineData("*/*")]
        public void GiveARequestWithInvalidAcceptHeader_WhenGettingAnExportOperationRequest_ThenAResourceNotValidExceptionShouldBeThrown(string acceptHeader)
        {
            var filter = new ValidateExportHeadersFilterAttribute();
            var context = CreateContext();

            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, acceptHeader);

            Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
        }

        [Fact]
        public void GiveARequestWithNoAcceptHeader_WhenGettingAnExportOperationRequest_ThenAResourceNotValidExceptionShouldBeThrown()
        {
            var filter = new ValidateExportHeadersFilterAttribute();
            var context = CreateContext();

            context.HttpContext.Request.Headers.Add(_preferHeaderName, _correctPreferHeaderValue);

            Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
        }

        [Theory]
        [InlineData("respond-async, wait = 10")]
        [InlineData("return-content")]
        [InlineData("*")]
        public void GiveARequestWithInvalidPreferHeader_WhenGettingAnExportOperationRequest_ThenAResourceNotValidExceptionShouldBeThrown(string preferHeader)
        {
            var filter = new ValidateExportHeadersFilterAttribute();
            var context = CreateContext();

            context.HttpContext.Request.Headers.Add(_preferHeaderName, preferHeader);

            Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
        }

        [Fact]
        public void GiveARequestWithNoPreferHeader_WhenGettingAnExportOperationRequest_ThenAResourceNotValidExceptionShouldBeThrown()
        {
            var filter = new ValidateExportHeadersFilterAttribute();
            var context = CreateContext();

            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, _correctAcceptHeaderValue);

            Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
        }

        [Fact]
        public void GiveARequestWithValidAcceptAndPreferHeader_WhenGettingAnExportOperationRequest_ThenTheResultIsSuccessful()
        {
            var filter = new ValidateExportHeadersFilterAttribute();
            var context = CreateContext();

            context.HttpContext.Request.Headers.Add(HeaderNames.Accept, _correctAcceptHeaderValue);
            context.HttpContext.Request.Headers.Add(_preferHeaderName, _correctPreferHeaderValue);

            filter.OnActionExecuting(context);
        }

        private static ActionExecutingContext CreateContext()
        {
            return new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>(),
                new Dictionary<string, object>(),
                FilterTestsHelper.CreateMockOperationsController());
        }
    }
}
