// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Logging;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.ValueSets;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    public class AuditLoggingFilterTests
    {
        private ResultExecutedContext _executedContext;
        private IFhirContextAccessor _fhirContextAccessor = Substitute.For<IFhirContextAccessor>();
        private IFhirContext _fhirContext = Substitute.For<IFhirContext>();
        private IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();
        private FhirResult _fhirResult;
        private readonly IOptions<SecurityConfiguration> _securityOptions = Substitute.For<IOptions<SecurityConfiguration>>();
        private readonly SecurityConfiguration _securityConfiguration = Substitute.For<SecurityConfiguration>();
        private readonly ClaimsPrincipal _claimsPrincipal = Substitute.For<ClaimsPrincipal>();

        private IReadOnlyCollection<KeyValuePair<string, string>> _claims;
        private IClaimsIndexer _claimsIndexer;

        private readonly IDictionary<string, object> _actionArguments = new Dictionary<string, object>();

        public AuditLoggingFilterTests()
        {
            _fhirResult = new FhirResult(new Patient() { Name = { new HumanName() { Text = "TestPatient" } } });

            _executedContext = new ResultExecutedContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ControllerActionDescriptor() { DisplayName = "Executed Context Test Descriptor" }),
                new List<IFilterMetadata>(),
                _fhirResult,
                FilterTestsHelper.CreateMockFhirController());

            _executedContext.HttpContext.Response.StatusCode = (int)HttpStatusCode.Created;
            _fhirResult.StatusCode = HttpStatusCode.Created;
            _fhirContext.RequestType = new Coding("System", "TestRequestType");
            _fhirContext.RequestSubType = new Coding("System", "TestRequestSubType");
            _fhirContext.RequestUri = new Uri("https://fhirtest/fhir?count=100");
            _fhirContextAccessor.FhirContext.Returns(_fhirContext);
            _fhirContextAccessor.FhirContext.Principal.Returns(_claimsPrincipal);
            _securityConfiguration.LastModifiedClaims.Returns(new HashSet<string> { "claim1" });
            _securityOptions.Value.Returns(_securityConfiguration);
            _claimsPrincipal.Claims.Returns(new List<System.Security.Claims.Claim> { Claim1 });

            _claims = new KeyValuePair<string, string>[]
            {
                KeyValuePair.Create("claim", "value"),
            };

            _claimsIndexer = Substitute.For<IClaimsIndexer>();

            _claimsIndexer.Extract().Returns(_claims);
        }

        private static System.Security.Claims.Claim Claim1 => new System.Security.Claims.Claim("claim1", "value1");

        [Fact]
        public void GivenAFhirRequest_WhenExecutingAnAnonymousAction_ThenLogAuditMustNotBeCalled()
        {
            var executingContext = new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ControllerActionDescriptor() { DisplayName = "Executing Context Test Descriptor" }),
                new List<IFilterMetadata>(),
                _actionArguments,
                FilterTestsHelper.CreateMockFhirController());

            var filter = new AuditLoggingFilterAttribute(_auditLogger, _fhirContextAccessor, _claimsIndexer);

            var descriptor = executingContext.ActionDescriptor as ControllerActionDescriptor;

            descriptor.MethodInfo = typeof(FilterTestsHelper).GetMethod("MethodWithAnonymousAttribute");
            filter.OnActionExecuting(executingContext);
            _auditLogger.DidNotReceiveWithAnyArgs().LogAudit(Arg.Any<AuditAction>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Uri>(), Arg.Any<HttpStatusCode?>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<KeyValuePair<string, string>>>());
            _executedContext.ActionDescriptor = executingContext.ActionDescriptor;
            filter.OnResultExecuted(_executedContext);
            _auditLogger.DidNotReceiveWithAnyArgs().LogAudit(Arg.Any<AuditAction>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Uri>(), Arg.Any<HttpStatusCode?>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<KeyValuePair<string, string>>>());
        }

        [Fact]
        public void GivenAFhirRequest_WhenExecutingAnValidAction_ThenLogAuditMustBeCalled()
        {
            var executingContext = new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ControllerActionDescriptor() { DisplayName = "Executing Context Test Descriptor" }),
                new List<IFilterMetadata>(),
                _actionArguments,
                FilterTestsHelper.CreateMockFhirController());

            var filter = new AuditLoggingFilterAttribute(_auditLogger, _fhirContextAccessor, _claimsIndexer);

            var descriptor = executingContext.ActionDescriptor as ControllerActionDescriptor;

            var claims = _claimsIndexer.Extract();

            descriptor.MethodInfo = typeof(FilterTestsHelper).GetMethod("MethodWithAuditEventAttribute");
            filter.OnActionExecuting(executingContext);
            _auditLogger.Received(1).LogAudit(AuditAction.Executing, _fhirContext.RequestSubType.Code, null, _fhirContext.RequestUri, null, _fhirContext.CorrelationId, _claims);
            _executedContext.ActionDescriptor = executingContext.ActionDescriptor;
            filter.OnResultExecuted(_executedContext);
            _auditLogger.Received(2).LogAudit(Arg.Any<AuditAction>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Uri>(), Arg.Any<HttpStatusCode?>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<KeyValuePair<string, string>>>());
            _auditLogger.Received(1).LogAudit(AuditAction.Executed, _fhirContext.RequestSubType.Code, _fhirResult.Resource.TypeName, _fhirContext.RequestUri, _fhirResult.StatusCode, _fhirContext.CorrelationId, _claims);
        }

        [Fact]
        public void GivenAFhirRequest_WhenExecutingAnValidAction_ThenCorrectRequestSubTypeMustBeSet()
        {
            var executingContext = new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ControllerActionDescriptor() { DisplayName = "Executing Context Test Descriptor" }),
                new List<IFilterMetadata>(),
                _actionArguments,
                FilterTestsHelper.CreateMockFhirController());

            var filter = new AuditLoggingFilterAttribute(_auditLogger, _fhirContextAccessor, _claimsIndexer);

            var fhirController = executingContext.Controller as FhirController;

            AssertProperRequestSubTypeSet(executingContext, "Create", AuditEventSubType.Create, filter);
            AssertProperRequestSubTypeSet(executingContext, "Update", AuditEventSubType.Update, filter);
            AssertProperRequestSubTypeSet(executingContext, "Read", AuditEventSubType.Read, filter);
            AssertProperRequestSubTypeSet(executingContext, "SystemHistory", AuditEventSubType.HistorySystem, filter);
            AssertProperRequestSubTypeSet(executingContext, "TypeHistory", AuditEventSubType.HistoryType, filter);
            AssertProperRequestSubTypeSet(executingContext, "History", AuditEventSubType.HistoryInstance, filter);
            AssertProperRequestSubTypeSet(executingContext, "VRead", AuditEventSubType.VRead, filter);
            AssertProperRequestSubTypeSet(executingContext, "Delete", AuditEventSubType.Delete, filter);
            AssertProperRequestSubTypeSet(executingContext, "Search", AuditEventSubType.SearchType, filter);
            AssertProperRequestSubTypeSet(executingContext, "SearchPost", AuditEventSubType.SearchType, filter);
        }

        private void AssertProperRequestSubTypeSet(ActionExecutingContext executingContext, string methodName, string requestSubType, AuditLoggingFilterAttribute filter)
        {
            executingContext.ActionDescriptor = new ControllerActionDescriptor();
            var descriptor = executingContext.ActionDescriptor as ControllerActionDescriptor;

            descriptor.MethodInfo = typeof(FhirController).GetMethod(methodName);
            filter.OnActionExecuting(executingContext);
            Assert.Equal(_fhirContextAccessor.FhirContext.RequestSubType.Code, requestSubType);
            Assert.Equal(_fhirContextAccessor.FhirContext.RequestSubType.System, AuditEventSubType.System);
        }

        [Fact]
        public void GivenAFhirRequest_WhenExecutingAnActionWithoutAttributes_ThenException()
        {
            var executingContext = new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ControllerActionDescriptor() { DisplayName = "Executing Context Test Descriptor" }),
                new List<IFilterMetadata>(),
                _actionArguments,
                FilterTestsHelper.CreateMockFhirController());

            var filter = new AuditLoggingFilterAttribute(_auditLogger, _fhirContextAccessor, _claimsIndexer);
            var descriptor = executingContext.ActionDescriptor as ControllerActionDescriptor;

            descriptor.MethodInfo = typeof(FilterTestsHelper).GetMethod("MethodWithNoAttribute");

            var excp = Assert.Throws<NotSupportedException>(() => filter.OnActionExecuting(executingContext));
            Assert.Contains(excp.Message, "Audit Event Sub Type is not set for method MethodWithNoAttribute");
        }
    }
}
