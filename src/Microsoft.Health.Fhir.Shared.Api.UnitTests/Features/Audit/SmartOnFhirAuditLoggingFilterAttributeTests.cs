// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Context;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Audit
{
    public class SmartOnFhirAuditLoggingFilterAttributeTests
    {
        private const string ControllerName = "controller";
        private const string ActionName = "action";
        private const string Action = "smart-on-fhir-action";

        private readonly IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private readonly SmartOnFhirAuditLoggingFilterAttribute _filter;
        private IReadOnlyCollection<KeyValuePair<string, string>> _loggedClaims;
        private readonly QueryCollection _queryCollection;
        private readonly FormCollection _formCollection;
        private readonly string _correlationId;

        public SmartOnFhirAuditLoggingFilterAttributeTests()
        {
            _correlationId = Guid.NewGuid().ToString();
            _fhirRequestContextAccessor.FhirRequestContext.CorrelationId.Returns(_correlationId);

            _filter = new SmartOnFhirAuditLoggingFilterAttribute(Action, new List<IAuditLogger> { _auditLogger }, _fhirRequestContextAccessor);
            _auditLogger.LogAudit(Arg.Any<AuditAction>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Uri>(), Arg.Any<HttpStatusCode?>(), Arg.Any<string>(), Arg.Do<IReadOnlyCollection<KeyValuePair<string, string>>>(c => _loggedClaims = c));

            var passedValues = new Dictionary<string, StringValues>
            {
                { "client_id", new StringValues("1234") },
                { "secret", new StringValues("secret") },
            };

            _queryCollection = new QueryCollection(
                passedValues);

            _formCollection = new FormCollection(
                passedValues);
        }

        [Fact]
        public void GivenAController_WhenExecutingAction_ThenAuditLogShouldBeLogged()
        {
            SetupExecutingAction(_queryCollection, null);

            VerifyAuditLoggerReceivedLogAudit(AuditAction.Executing, null);
            VerifyClaims();
        }

        [Fact]
        public void GivenAController_WhenExecutingActionWithFormCollection_ThenAuditLogShouldBeLogged()
        {
            SetupExecutingAction(null, _formCollection);

            VerifyAuditLoggerReceivedLogAudit(AuditAction.Executing, null);
            VerifyClaims();
        }

        [Fact]
        public void GivenAController_WhenExecutingActionWithEmptyQueryCollectionAndEmptyFormCollection_ThenAuditLogShouldBeLogged()
        {
            SetupExecutingAction(null, null);

            VerifyAuditLoggerReceivedLogAudit(AuditAction.Executing, null);
            Assert.Empty(_loggedClaims);
        }

        [Fact]
        public void GivenAController_WhenExecutedAction_ThenAuditLogShouldBeLogged()
        {
            const HttpStatusCode expectedStatusCode = HttpStatusCode.InternalServerError;
            SetupExecutedAction(expectedStatusCode, new OkResult(), _queryCollection, null);

            VerifyAuditLoggerReceivedLogAudit(AuditAction.Executed, expectedStatusCode);
            VerifyClaims();
        }

        [Fact]
        public void GivenAController_WhenExecutedActionWithFormCollection_ThenAuditLogShouldBeLogged()
        {
            const HttpStatusCode expectedStatusCode = HttpStatusCode.InternalServerError;
            SetupExecutedAction(expectedStatusCode, new OkResult(), null, _formCollection);

            VerifyAuditLoggerReceivedLogAudit(AuditAction.Executed, expectedStatusCode);
            VerifyClaims();
        }

        [Fact]
        public void GivenAController_WhenExecutedActionWithEmptyQueryCollectionAndEmptyFormCollection_ThenAuditLogShouldBeLogged()
        {
            const HttpStatusCode expectedStatusCode = HttpStatusCode.InternalServerError;
            SetupExecutedAction(expectedStatusCode, new OkResult(), null, null);

            VerifyAuditLoggerReceivedLogAudit(AuditAction.Executed, expectedStatusCode);
            Assert.Empty(_loggedClaims);
        }

        private void SetupExecutingAction(IQueryCollection queryCollection, IFormCollection formCollection)
        {
            var actionExecutingContext = new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ControllerActionDescriptor() { DisplayName = "Executing Context Test Descriptor" }),
                new List<IFilterMetadata>(),
                new Dictionary<string, object>(),
                new MockController());

            actionExecutingContext.HttpContext.Request.Query = queryCollection;
            actionExecutingContext.HttpContext.Request.Form = formCollection;

            actionExecutingContext.ActionDescriptor = new ControllerActionDescriptor()
            {
                ControllerName = ControllerName,
                ActionName = ActionName,
            };

            _filter.OnActionExecuting(actionExecutingContext);
        }

        private void SetupExecutedAction(HttpStatusCode expectedStatusCode, IActionResult result, IQueryCollection queryCollection, IFormCollection formCollection)
        {
            var resultExecutedContext = new ResultExecutedContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ControllerActionDescriptor() { DisplayName = "Executed Context Test Descriptor" }),
                new List<IFilterMetadata>(),
                result,
                new MockController());

            resultExecutedContext.HttpContext.Request.Query = queryCollection;
            resultExecutedContext.HttpContext.Request.Form = formCollection;

            resultExecutedContext.HttpContext.Response.StatusCode = (int)expectedStatusCode;

            resultExecutedContext.ActionDescriptor = new ControllerActionDescriptor()
            {
                ControllerName = ControllerName,
                ActionName = ActionName,
            };

            _filter.OnResultExecuted(resultExecutedContext);
        }

        private void VerifyAuditLoggerReceivedLogAudit(AuditAction auditAction, HttpStatusCode? httpStatusCode)
        {
            _auditLogger.Received(1).LogAudit(
                Arg.Is(auditAction),
                Arg.Is(Action),
                Arg.Is<string>(x => x == null),
                Arg.Any<Uri>(),
                Arg.Is(httpStatusCode),
                Arg.Is(_correlationId),
                Arg.Any<IReadOnlyCollection<KeyValuePair<string, string>>>());
        }

        private void VerifyClaims()
        {
            Assert.Equal(1, _loggedClaims.Count);
            (string key, string value) = _loggedClaims.First();
            Assert.Equal("client_id", key);
            Assert.Equal("1234", value);
        }

        private class MockController : Controller
        {
        }
    }
}
