// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Api.UnitTests.Features.Filters;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Validation;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Api.UnitTests.Features.Filters
{
    public class ValidationQueryFilterAndParameterParserAttributeTests
    {
        private FeatureConfiguration _featureConfiguration = new FeatureConfiguration()
        {
            SupportsValidate = true,
        };

        [Theory]
        [InlineData("CREATE", "CREATE is not a supported validation mode.", typeof(OperationNotImplementedException))]
        [InlineData("UPDATE", "Resources can not be validated for update or delete at the default endpoint. An id must be provided in the URL.", typeof(BadRequestException))]
        [InlineData("DELETE", "Resources can not be validated for update or delete at the default endpoint. An id must be provided in the URL.", typeof(BadRequestException))]
        [InlineData("invalid", "invalid is not a valid validation mode.", typeof(BadRequestException))]
        [InlineData("CREATE", "CREATE is not a supported validation mode.", typeof(OperationNotImplementedException), true)]
        [InlineData("UPDATE", "Resources can not be validated for update or delete at the default endpoint. An id must be provided in the URL.", typeof(BadRequestException), true)]
        [InlineData("DELETE", "Resources can not be validated for update or delete at the default endpoint. An id must be provided in the URL.", typeof(BadRequestException), true)]
        [InlineData("invalid", "invalid is not a valid validation mode.", typeof(BadRequestException), true)]

        public void GivenARequest_WhenAModeIsPassed_ThenAnExceptionIsReturned(string mode, string message, Type issueType, bool parameters = false)
        {
            var filter = new ValidationQueryFilterAndParameterParserAttribute(Options.Create(_featureConfiguration));

            var context = CreateContext(mode, null, false, parameters);

            var exception = Assert.Throws(issueType, () => filter.OnActionExecuting(context));

            Assert.Equal(message, exception.Message);
        }

        [Theory]
        [InlineData("CREATE", "CREATE is not a supported validation mode.", typeof(OperationNotImplementedException))]
        [InlineData("UPDATE", "UPDATE is not a supported validation mode.", typeof(OperationNotImplementedException))]
        [InlineData("invalid", "invalid is not a valid validation mode.", typeof(BadRequestException))]
        [InlineData("CREATE", "CREATE is not a supported validation mode.", typeof(OperationNotImplementedException), true)]
        [InlineData("UPDATE", "UPDATE is not a supported validation mode.", typeof(OperationNotImplementedException), true)]
        [InlineData("invalid", "invalid is not a valid validation mode.", typeof(BadRequestException), true)]
        public void GivenARequestWithId_WhenAModeIsPassed_ThenAnExceptionIsReturned(string mode, string message, Type issueType, bool parameters = false)
        {
            var filter = new ValidationQueryFilterAndParameterParserAttribute(Options.Create(_featureConfiguration));

            var context = CreateContext(mode, null, true, parameters);

            var exception = Assert.Throws(issueType, () => filter.OnActionExecuting(context));

            Assert.Equal(message, exception.Message);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GivenARequestWithId_WhenAModeOfDeleteIsPassed_ThenAnOkMessageIsReturned(bool parameters)
        {
            var filter = new ValidationQueryFilterAndParameterParserAttribute(Options.Create(_featureConfiguration));

            var context = CreateContext("DELETE", null, true, parameters);

            var exception = Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));

            Assert.True(exception.Issues.Contains(ValidateOperationHandler.ValidationPassed));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GivenARequest_WhenAProfileIsPassed_ThenAnExceptionIsReturned(bool parameters)
        {
            var filter = new ValidationQueryFilterAndParameterParserAttribute(Options.Create(_featureConfiguration));

            var context = CreateContext(null, "test", false, parameters);

            var exception = Assert.Throws<OperationNotImplementedException>(() => filter.OnActionExecuting(context));

            Assert.Equal("Validation against a profile is not supported.", exception.Message);
        }

        [Fact]
        public void GivenARequest_WhenTwoModesArePassed_ThenAnExceptionIsReturned()
        {
            var queryParams = new Dictionary<string, StringValues>();
            queryParams.Add(KnownQueryParameterNames.Mode, "CREATE");

            var httpRequest = Substitute.For<HttpRequest>();
            httpRequest.Query = new QueryCollection(queryParams);

            var httpContext = Substitute.For<HttpContext>();
            httpContext.Request.Returns(httpRequest);

            var parameters = new Parameters();
            parameters.Add(KnownQueryParameterNames.Mode, new Code("UPDATE"));

            var actionContext = new ActionExecutingContext(
                new ActionContext(
                    httpContext,
                    new RouteData { Values = { [KnownActionParameterNames.ResourceType] = "Observation" } },
                    new ActionDescriptor() { DisplayName = string.Empty }),
                new List<IFilterMetadata>(),
                new Dictionary<string, object> { { "resource", parameters } },
                FilterTestsHelper.CreateMockFhirController());

            var filter = new ValidationQueryFilterAndParameterParserAttribute(Options.Create(_featureConfiguration));

            var exception = Assert.Throws<BadRequestException>(() => filter.OnActionExecuting(actionContext));

            Assert.Equal("Only one mode can be provided between a Parameters resource and the URL", exception.Message);
        }

        [Fact]
        public void GivenARequest_WhenTwoProfilesArePassed_ThenAnExceptionIsReturned()
        {
            var queryParams = new Dictionary<string, StringValues>();
            queryParams.Add(KnownQueryParameterNames.Profile, "test");

            var httpRequest = Substitute.For<HttpRequest>();
            httpRequest.Query = new QueryCollection(queryParams);

            var httpContext = Substitute.For<HttpContext>();
            httpContext.Request.Returns(httpRequest);

            var parameters = new Parameters();
            parameters.Add(KnownQueryParameterNames.Profile, new FhirUri("otherTest"));

            var actionContext = new ActionExecutingContext(
                new ActionContext(
                    httpContext,
                    new RouteData { Values = { [KnownActionParameterNames.ResourceType] = "Observation" } },
                    new ActionDescriptor() { DisplayName = string.Empty }),
                new List<IFilterMetadata>(),
                new Dictionary<string, object> { { "resource", parameters } },
                FilterTestsHelper.CreateMockFhirController());

            var filter = new ValidationQueryFilterAndParameterParserAttribute(Options.Create(_featureConfiguration));

            var exception = Assert.Throws<BadRequestException>(() => filter.OnActionExecuting(actionContext));

            Assert.Equal("Only one profile can be provided between a Parameters resource and the URL", exception.Message);
        }

        [Fact]
        public void GivenARequest_WhenValidationIsNotSupported_ThenAnExceptionIsReturned()
        {
            var featureConfiguration = new FeatureConfiguration()
            {
                SupportsValidate = false,
            };
            var filter = new ValidationQueryFilterAndParameterParserAttribute(Options.Create(featureConfiguration));

            var context = CreateContext();
            var exception = Assert.Throws<OperationNotImplementedException>(() => filter.OnActionExecuting(context));

            Assert.Equal("$validate is not a supported endpoint.", exception.Message);
        }

        private static ActionExecutingContext CreateContext(string mode = null, string profile = null, bool idMode = false, bool isParameters = false)
        {
            var queryParams = new Dictionary<string, StringValues>();

            if (!isParameters && !string.IsNullOrEmpty(mode))
            {
                queryParams.Add(KnownQueryParameterNames.Mode, mode);
            }

            if (!isParameters && !string.IsNullOrEmpty(profile))
            {
                queryParams.Add(KnownQueryParameterNames.Profile, profile);
            }

            var httpRequest = Substitute.For<HttpRequest>();
            httpRequest.Query = new QueryCollection(queryParams);

            var httpContext = Substitute.For<HttpContext>();
            httpContext.Request.Returns(httpRequest);

            Resource resource = new Observation();

            if (isParameters)
            {
                var parameters = new Parameters();
                parameters.Add(KnownQueryParameterNames.Mode, new Code(mode));
                parameters.Add(KnownQueryParameterNames.Profile, new FhirUri(profile));
                resource = parameters;
            }

            return new ActionExecutingContext(
                new ActionContext(
                    httpContext,
                    new RouteData { Values = { [KnownActionParameterNames.ResourceType] = "Observation" } },
                    new ActionDescriptor() { DisplayName = idMode ? "ValidateById" : string.Empty }),
                new List<IFilterMetadata>(),
                new Dictionary<string, object> { { "resource", resource } },
                FilterTestsHelper.CreateMockFhirController());
        }
    }
}
