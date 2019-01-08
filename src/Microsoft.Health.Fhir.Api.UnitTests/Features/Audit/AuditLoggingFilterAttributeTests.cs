// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Audit;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Audit
{
    public class AuditLoggingFilterAttributeTests
    {
        private const string ControllerName = "controller";
        private const string ActionName = "action";

        private readonly IAuditHelper _auditHelper = Substitute.For<IAuditHelper>();

        private readonly AuditLoggingFilterAttribute _filter;

        public AuditLoggingFilterAttributeTests()
        {
            _filter = new AuditLoggingFilterAttribute(_auditHelper);
        }

        [Fact]
        public void GivenAController_WhenExecutingAction_ThenAuditLogShouldBeLogged()
        {
            var actionExecutingContext = new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ControllerActionDescriptor() { DisplayName = "Executing Context Test Descriptor" }),
                new List<IFilterMetadata>(),
                new Dictionary<string, object>(),
                new MockController());

            actionExecutingContext.ActionDescriptor = new ControllerActionDescriptor()
            {
                ControllerName = ControllerName,
                ActionName = ActionName,
            };

            _filter.OnActionExecuting(actionExecutingContext);

            _auditHelper.Received(1).LogExecuting(ControllerName, ActionName);
        }

        [Fact]
        public void GivenANonFhirResult_WhenExecutedAction_ThenAuditLogShouldBeLogged()
        {
            const HttpStatusCode expectedStatusCode = HttpStatusCode.InternalServerError;

            SetupExecutedAction(expectedStatusCode, new OkResult());

            _auditHelper.Received(1).LogExecuted(ControllerName, ActionName, expectedStatusCode, null);
        }

        [Fact]
        public void GivenAFhirResultWithNullResource_WhenExecutedAction_ThenAuditLogShouldBeLogged()
        {
            const HttpStatusCode expectedStatusCode = HttpStatusCode.OK;

            SetupExecutedAction(expectedStatusCode, new FhirResult());

            _auditHelper.Received(1).LogExecuted(ControllerName, ActionName, expectedStatusCode, null);
        }

        [Fact]
        public void GivenAController_WhenExecutedAction_ThenAuditLogShouldBeLogged()
        {
            const HttpStatusCode expectedStatusCode = HttpStatusCode.Created;

            var fhirResult = new FhirResult(new Patient() { Name = { new HumanName() { Text = "TestPatient" } } });

            SetupExecutedAction(expectedStatusCode, fhirResult);

            _auditHelper.Received(1).LogExecuted(ControllerName, ActionName, expectedStatusCode, "Patient");
        }

        private void SetupExecutedAction(HttpStatusCode expectedStatusCode, IActionResult result)
        {
            var resultExecutedContext = new ResultExecutedContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ControllerActionDescriptor() { DisplayName = "Executed Context Test Descriptor" }),
                new List<IFilterMetadata>(),
                result,
                new MockController());

            resultExecutedContext.HttpContext.Response.StatusCode = (int)expectedStatusCode;

            resultExecutedContext.ActionDescriptor = new ControllerActionDescriptor()
            {
                ControllerName = ControllerName,
                ActionName = ActionName,
            };

            _filter.OnResultExecuted(resultExecutedContext);
        }

        private class MockController : Controller
        {
        }
    }
}
