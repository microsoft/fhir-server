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

        private static ActionExecutingContext CreateContext(Resource type, string id)
        {
            return new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext(), new RouteData { Values = { [KnownActionParameterNames.ResourceType] = "Patient", [KnownActionParameterNames.Id] = id } }, new ActionDescriptor()),
                new List<IFilterMetadata>(),
                new Dictionary<string, object> { { "resource", type } },
                FilterTestsHelper.CreateMockFhirController());
        }
    }
}
