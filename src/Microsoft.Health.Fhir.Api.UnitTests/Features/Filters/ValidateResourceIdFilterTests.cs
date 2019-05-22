﻿// -------------------------------------------------------------------------------------------------
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
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Stu3.Api.Features.Filters;
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

            Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
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

            Assert.Throws<ResourceNotValidException>(() => filter.OnActionExecuting(context));
        }

        private static ActionExecutingContext CreateContext(Resource type, string id)
        {
            return new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext(), new RouteData { Values = { ["type"] = "Observation", ["id"] = id } }, new ActionDescriptor()),
                new List<IFilterMetadata>(),
                new Dictionary<string, object> { { "resource", type } },
                FilterTestsHelper.CreateMockFhirController());
        }
    }
}
