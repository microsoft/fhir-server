// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.UnitTests.Features.Filters;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Audit
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    public class AuditLoggingFilterAttributeTests
    {
        private readonly IClaimsExtractor _claimsExtractor = Substitute.For<IClaimsExtractor>();
        private readonly IAuditHelper _auditHelper = Substitute.For<IAuditHelper>();

        private readonly AuditLoggingFilterAttribute _filter;

        private readonly HttpContext _httpContext = new DefaultHttpContext();

        public AuditLoggingFilterAttributeTests()
        {
            _filter = new AuditLoggingFilterAttribute(_claimsExtractor, _auditHelper);
        }

        [Fact]
        public void GivenAController_WhenExecutingAction_ThenAuditLogShouldBeLogged()
        {
            var actionExecutingContext = new ActionExecutingContext(
                new ActionContext(_httpContext, new RouteData(), new ControllerActionDescriptor() { DisplayName = "Executing Context Test Descriptor" }),
                new List<IFilterMetadata>(),
                new Dictionary<string, object>(),
                FilterTestsHelper.CreateMockFhirController());

            _filter.OnActionExecuting(actionExecutingContext);

            _auditHelper.Received(1).LogExecuting(_httpContext, _claimsExtractor);
        }

        [Fact]
        public void GivenAController_WhenExecutedAction_ThenAuditLogShouldBeLogged()
        {
            var fhirResult = new FhirResult(new Patient() { Name = { new HumanName() { Text = "TestPatient" } } }.ToResourceElement());

            var resultExecutedContext = new ResultExecutedContext(
                new ActionContext(_httpContext, new RouteData(), new ControllerActionDescriptor() { DisplayName = "Executed Context Test Descriptor" }),
                new List<IFilterMetadata>(),
                fhirResult,
                FilterTestsHelper.CreateMockFhirController());

            _filter.OnResultExecuted(resultExecutedContext);

            _auditHelper.Received(1).LogExecuted(_httpContext, _claimsExtractor);
        }
    }
}
