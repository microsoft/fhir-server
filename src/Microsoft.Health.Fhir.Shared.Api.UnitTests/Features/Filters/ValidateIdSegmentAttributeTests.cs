// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    [Trait(Traits.Category, Categories.Web)]
    public class ValidateIdSegmentAttributeTests
    {
        [Theory]
        [InlineData(" ")]
        [InlineData(null)]
        public void GivenAPatientAction_WhenPuttingAPatinetObjectWithNullResourceId_ThenAResourceNotValidExceptionShouldBeThrown(string id)
        {
            var filter = new ValidateIdSegmentAttribute();

            var patient = new Patient
            {
                Id = Guid.NewGuid().ToString(),
            };

            var context = CreateContext(patient, id);

            Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenAPatientAction_WhenPuttingAPatinetObjectwithValidResourceId_ThenTheResultIsSuccessful()
        {
            var filter = new ValidateIdSegmentAttribute();

            var patient = new Patient
            {
                Id = Guid.NewGuid().ToString(),
            };
            var context = CreateContext(patient, patient.Id);

            var exception = Record.Exception(() => filter.OnActionExecuting(context));

            Assert.Null(exception);
        }

        [Theory]
        [InlineData(" ")]
        [InlineData(null)]
        public void GivenAPatinetAction_WhenPuttingAParametersPatientObjectwithNullResourceId_ThenAResourceNotValidExceptionShouldBeThrown(string id)
        {
            var filter = new ValidateIdSegmentAttribute(true);

            var patient = new Patient
            {
                Id = Guid.NewGuid().ToString(),
            };

            var parameters = new Parameters();
            parameters.Add("resource", patient);
            var context = CreateContext(parameters, id);

            Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenAPatientAction_WhenPuttingAParametersPatientObjectwithValidResourceId_ThenTheResultIsSuccessful()
        {
            var filter = new ValidateResourceIdFilterAttribute(true);

            var patient = new Patient
            {
                Id = Guid.NewGuid().ToString(),
            };

            var parameters = new Parameters();
            parameters.Add("resource", patient);
            var context = CreateContext(parameters, patient.Id);

            filter.OnActionExecuting(context);
        }

        [Theory]
        [InlineData(false, "64")]
        [InlineData(true, "128")]
        public void GivenARegisteredResourceIdPolicy_WhenTheIdIsEmpty_ThenTheMessageStatesThePolicyLimit(bool useLongResourceIds, string expectedLimit)
        {
            // Arrange
            var filter = new ValidateIdSegmentAttribute();
            var context = CreateContext(new Patient(), " ");
            context.HttpContext.RequestServices = new ServiceCollection()
                .AddSingleton(ResourceIdPolicy.From(useLongResourceIds))
                .BuildServiceProvider();

            // Act
            var exception = Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));

            // Assert
            Assert.Contains(expectedLimit, exception.Issues.Single().Diagnostics, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(false, 63)]
        [InlineData(false, 64)]
        [InlineData(true, 127)]
        [InlineData(true, 128)]
        public void GivenARegisteredResourceIdPolicy_WhenTheRouteIdIsWithinTheLimit_ThenTheRequestProceeds(bool useLongResourceIds, int length)
        {
            // Arrange
            var filter = new ValidateIdSegmentAttribute();
            var context = CreateContext(new Patient(), new string('a', length), ResourceIdPolicy.From(useLongResourceIds));

            // Act
            var exception = Record.Exception(() => filter.OnActionExecuting(context));

            // Assert
            Assert.Null(exception);
        }

        [Theory]
        [InlineData(false, 65, "64")]
        [InlineData(true, 129, "128")]
        public void GivenARegisteredResourceIdPolicy_WhenTheRouteIdExceedsTheLimit_ThenTheRequestIsRejectedWithoutEchoingTheId(bool useLongResourceIds, int length, string expectedLimit)
        {
            // Arrange
            var filter = new ValidateIdSegmentAttribute();
            string id = new string('a', length);
            var context = CreateContext(new Patient(), id, ResourceIdPolicy.From(useLongResourceIds));

            // Act
            var exception = Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));

            // Assert
            string diagnostics = exception.Issues.Single().Diagnostics;
            Assert.Contains(expectedLimit, diagnostics, StringComparison.Ordinal);
            Assert.DoesNotContain(id, diagnostics, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("a b")]
        [InlineData("a/b")]
        [InlineData("a$b")]
        public void GivenARouteIdOutsideTheIdGrammar_WhenExecuting_ThenTheRequestIsRejected(string id)
        {
            // Arrange
            var filter = new ValidateIdSegmentAttribute();
            var context = CreateContext(new Patient(), id);

            // Act & Assert
            Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenARouteWithoutAnIdSegment_WhenExecuting_ThenTheRequestProceeds()
        {
            // Arrange
            var filter = new ValidateIdSegmentAttribute();
            var context = new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(ResourceIdPolicy.Standard).BuildServiceProvider() }, new RouteData { Values = { [KnownActionParameterNames.ResourceType] = "Patient" } }, new ActionDescriptor()),
                new List<IFilterMetadata>(),
                new Dictionary<string, object>(),
                FilterTestsHelper.CreateMockFhirController());

            // Act
            var exception = Record.Exception(() => filter.OnActionExecuting(context));

            // Assert
            Assert.Null(exception);
        }

        private static ActionExecutingContext CreateContext(Resource type, string id, ResourceIdPolicy policy)
        {
            ActionExecutingContext context = CreateContext(type, id);
            context.HttpContext.RequestServices = new ServiceCollection().AddSingleton(policy).BuildServiceProvider();
            return context;
        }

        private static ActionExecutingContext CreateContext(Resource type, string id)
        {
            return new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(ResourceIdPolicy.Standard).BuildServiceProvider() }, new RouteData { Values = { [KnownActionParameterNames.ResourceType] = "Patient", [KnownActionParameterNames.Id] = id } }, new ActionDescriptor()),
                new List<IFilterMetadata>(),
                new Dictionary<string, object> { { "resource", type } },
                FilterTestsHelper.CreateMockFhirController());
        }
    }
}
