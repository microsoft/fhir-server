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
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    public class ValidateResourceIdFilterTests
    {
        [Fact]
        public void GivenAnObservationAction_WhenPuttingAnObservationObjectWithNonMatchingId_ThenAResourceNotValidExceptionShouldBeThrown()
        {
            var filter = new ValidateResourceIdFilterAttribute();

            var observation = new Observation
            {
                Id = Guid.NewGuid().ToString(),
            };

            var context = CreateContext(observation, Guid.NewGuid().ToString());

            Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenAnObservationAction_WhenPuttingAnObservationObjectWithMiscasedMatchingId_ThenAResourceNotValidExceptionShouldBeThrown()
        {
            var filter = new ValidateResourceIdFilterAttribute();

            var observation = new Observation
            {
                Id = Guid.NewGuid().ToString(),
            };

            var context = CreateContext(observation, observation.Id.ToUpper());

            var exception = Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
            Assert.Equal("Observation.id", exception.Issues.First<OperationOutcomeIssue>().Expression.First());
        }

        [Fact]
        public void GivenAnObservationAction_WhenPuttingAnObservationObject_ThenTheResultIsSuccessful()
        {
            var filter = new ValidateResourceIdFilterAttribute();

            var observation = new Observation
            {
                Id = Guid.NewGuid().ToString(),
            };

            var context = CreateContext(observation, observation.Id);

            filter.OnActionExecuting(context);
        }

        [Fact]
        public void GivenAnObservationAction_WhenPuttingAnObservationObjectWithoutAnId_ThenAResourceNotValidExceptionShouldBeThrown()
        {
            var filter = new ValidateResourceIdFilterAttribute();

            var observation = new Observation();

            var context = CreateContext(observation, Guid.NewGuid().ToString());

            var exception = Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
            Assert.Equal("Observation.id", exception.Issues.First<OperationOutcomeIssue>().Expression.First());
        }

        [Fact]
        public void GivenAnObservationAction_WhenPuttingAParametersObservationObjectWithNonMatchingId_ThenAResourceNotValidExceptionShouldBeThrown()
        {
            var filter = new ValidateResourceIdFilterAttribute(true);

            var observation = new Observation
            {
                Id = Guid.NewGuid().ToString(),
            };

            var parameters = new Parameters();
            parameters.Add("resource", observation);
            var context = CreateContext(parameters, Guid.NewGuid().ToString());

            Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
        }

        [Fact]
        public void GivenAnObservationAction_WhenPuttingAParametersObservationObject_ThenTheResultIsSuccessful()
        {
            var filter = new ValidateResourceIdFilterAttribute(true);

            var observation = new Observation
            {
                Id = Guid.NewGuid().ToString(),
            };

            var parameters = new Parameters();
            parameters.Add("resource", observation);
            var context = CreateContext(parameters, observation.Id);

            filter.OnActionExecuting(context);
        }

        [Fact]
        public void GivenAnObservationAction_WhenPuttingAParametersObject_AndParametersAreNotParsed_ThenTheResultIsSuccessful()
        {
            var filter = new ValidateResourceIdFilterAttribute();

            var observation = new Observation
            {
                Id = Guid.NewGuid().ToString(),
            };

            var parameters = new Parameters();
            parameters.Add("resource", observation);
            var context = CreateContext(parameters, observation.Id);

            Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
        }

        private static ActionExecutingContext CreateContext(Resource type, string id)
        {
            return new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext(), new RouteData { Values = { [KnownActionParameterNames.ResourceType] = "Observation", [KnownActionParameterNames.Id] = id } }, new ActionDescriptor()),
                new List<IFilterMetadata>(),
                new Dictionary<string, object> { { "resource", type } },
                FilterTestsHelper.CreateMockFhirController());
        }
    }
}
