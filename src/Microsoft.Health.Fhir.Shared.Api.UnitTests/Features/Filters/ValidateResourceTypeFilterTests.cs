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
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
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

        private static ActionExecutingContext CreateContext(Base type)
        {
            return new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext(), new RouteData { Values = { [KnownActionParameterNames.ResourceType] = "Observation" } }, new ActionDescriptor()),
                new List<IFilterMetadata>(),
                new Dictionary<string, object> { { "resource", type } },
                FilterTestsHelper.CreateMockFhirController());
        }
    }
}
