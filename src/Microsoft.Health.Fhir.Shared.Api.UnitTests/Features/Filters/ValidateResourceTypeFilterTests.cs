// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    [Trait(Traits.Category, Categories.Web)]
    public class ValidateResourceTypeFilterTests
    {
        [Fact]
        public void GivenAnObservationAction_WhenPostingAPatientObject_ThenATypeMistatchExceptionShouldBeThrown()
        {
            var filter = new ValidateResourceTypeFilterAttribute();

            var context = CreateContext(new Patient());

            Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenAnObservationAction_WhenPostingAnObservationObject_ThenTheResultIsSuccessful()
        {
            var filter = new ValidateResourceTypeFilterAttribute();

            var context = CreateContext(new Observation());

            filter.OnActionExecuting(context);
        }

        [Fact]
        public void GivenAnObservationAction_WhenPostingAParametersPatientObject_ThenATypeMistatchExceptionShouldBeThrown()
        {
            var filter = new ValidateResourceTypeFilterAttribute(true);

            var parameters = new Parameters();
            parameters.Add("resource", new Patient());
            var context = CreateContext(parameters);

            Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenAnObservationAction_WhenPostingAParametersObservationObject_ThenTheResultIsSuccessful()
        {
            var filter = new ValidateResourceTypeFilterAttribute(true);

            var parameters = new Parameters();
            parameters.Add("resource", new Observation());
            var context = CreateContext(parameters);

            filter.OnActionExecuting(context);
        }

        [Fact]
        public void GivenAnObservationAction_WhenPostingAParametersObject_AndNotParsingParameters_ThenATypeMistatchExceptionShouldBeThrown()
        {
            var filter = new ValidateResourceTypeFilterAttribute();

            var parameters = new Parameters();
            parameters.Add("resource", new Observation());
            var context = CreateContext(parameters);

            Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenAnPatchFhirAction_WhenPostingAParametersObject_ThenTheResultIsSuccessful()
        {
            var filter = new ValidateResourceTypeFilterAttribute();

            var parameters = new Parameters();
            parameters.Add("resource", new Observation());
            var context = CreateContext(parameters, true);

            filter.OnActionExecuting(context);
        }

        [Fact]
        public void GivenAnPatchFhirAction_WhenPostingAParametersObservationObject_ThenATypeMistatchExceptionShouldBeThrown()
        {
            var filter = new ValidateResourceTypeFilterAttribute();

            var context = CreateContext(new Observation(), true);

            Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenAnObservationAction_WhenPostingParametersWithNullParameterCollection_ThenShouldNotThrowNullReferenceException()
        {
            var filter = new ValidateResourceTypeFilterAttribute(true);

            // Create Parameters with null Parameter collection
            var parameters = new Parameters();
            parameters.Parameter = null; // This should not cause NullReferenceException
            var context = CreateContext(parameters);

            // Should throw ResourceNotValidException, not NullReferenceException
            Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenAnObservationAction_WhenPostingParametersWithMissingResourceParameter_ThenShouldNotThrowNullReferenceException()
        {
            var filter = new ValidateResourceTypeFilterAttribute(true);

            // Create Parameters with no "resource" parameter
            var parameters = new Parameters();
            parameters.Parameter = new List<Parameters.ParameterComponent>
            {
                new Parameters.ParameterComponent { Name = "otherParam", Value = new FhirString("test") },
            };
            var context = CreateContext(parameters);

            // Should throw ResourceNotValidException, not NullReferenceException
            Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenAnObservationAction_WhenPostingParametersWithNullResourceParameter_ThenShouldNotThrowNullReferenceException()
        {
            var filter = new ValidateResourceTypeFilterAttribute(true);

            // Create Parameters with "resource" parameter but null Resource
            var parameters = new Parameters();
            parameters.Parameter = new List<Parameters.ParameterComponent>
            {
                new Parameters.ParameterComponent { Name = "resource", Resource = null }, // This should not cause NullReferenceException
            };
            var context = CreateContext(parameters);

            // Should throw ResourceNotValidException, not NullReferenceException
            Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
        }

        [Fact]
        public void ParseResource_WithNullParameterCollection_ShouldNotThrowNullReferenceException()
        {
            var filter = new TestParameterCompatibleFilter(true);

            // Create Parameters with null Parameter collection
            var parameters = new Parameters();
            parameters.Parameter = null; // This should not cause NullReferenceException

            var result = filter.TestParseResource(parameters);

            // Should return the original Parameters resource, not throw NullReferenceException
            Assert.NotNull(result);
            Assert.Same(parameters, result);
        }

        [Fact]
        public void ParseResource_WithMissingResourceParameter_ShouldNotThrowNullReferenceException()
        {
            var filter = new TestParameterCompatibleFilter(true);

            // Create Parameters with no "resource" parameter
            var parameters = new Parameters();
            parameters.Parameter = new List<Parameters.ParameterComponent>
            {
                new Parameters.ParameterComponent { Name = "otherParam", Value = new FhirString("test") },
            };

            var result = filter.TestParseResource(parameters);

            // Should return the original Parameters resource, not throw NullReferenceException
            Assert.NotNull(result);
            Assert.Same(parameters, result);
        }

        [Fact]
        public void ParseResource_WithNullResourceParameter_ShouldNotThrowNullReferenceException()
        {
            var filter = new TestParameterCompatibleFilter(true);

            // Create Parameters with "resource" parameter but null Resource
            var parameters = new Parameters();
            parameters.Parameter = new List<Parameters.ParameterComponent>
            {
                new Parameters.ParameterComponent { Name = "resource", Resource = null }, // This should not cause NullReferenceException
            };

            var result = filter.TestParseResource(parameters);

            // Should return the original Parameters resource, not throw NullReferenceException
            Assert.NotNull(result);
            Assert.Same(parameters, result);
        }

        [Fact]
        public void ParseResource_WithValidResourceParameter_ShouldExtractInnerResource()
        {
            var filter = new TestParameterCompatibleFilter(true);

            // Create Parameters with valid "resource" parameter
            var innerResource = new Patient { Id = "123" };
            var parameters = new Parameters();
            parameters.Parameter = new List<Parameters.ParameterComponent>
            {
                new Parameters.ParameterComponent { Name = "resource", Resource = innerResource },
            };

            var result = filter.TestParseResource(parameters);

            // Should extract and return the inner resource
            Assert.NotNull(result);
            Assert.Same(innerResource, result);
        }

        [Fact]
        public void ParseResource_WhenAllowParametersResourceIsFalse_ShouldReturnOriginalResource()
        {
            var filter = new TestParameterCompatibleFilter(false); // allowParametersResource = false

            // Create Parameters with valid "resource" parameter
            var innerResource = new Patient { Id = "123" };
            var parameters = new Parameters();
            parameters.Parameter = new List<Parameters.ParameterComponent>
            {
                new Parameters.ParameterComponent { Name = "resource", Resource = innerResource },
            };

            var result = filter.TestParseResource(parameters);

            // Should return the original Parameters resource since allowParametersResource is false
            Assert.NotNull(result);
            Assert.Same(parameters, result);
        }

        private static ActionExecutingContext CreateContext(Base type, bool paramsResource = false)
        {
            return new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext(), new RouteData { Values = { [KnownActionParameterNames.ResourceType] = "Observation" } }, new ActionDescriptor()),
                new List<IFilterMetadata>(),
                new Dictionary<string, object> { { (!paramsResource) ? "resource" : "paramsResource", type } },
                FilterTestsHelper.CreateMockFhirController());
        }

        // Test helper class to expose protected ParseResource method
        private class TestParameterCompatibleFilter : ParameterCompatibleFilter
        {
            public TestParameterCompatibleFilter(bool allowParametersResource)
                : base(allowParametersResource)
            {
            }

            public Resource TestParseResource(Resource resource)
            {
                return ParseResource(resource);
            }
        }
    }
}
