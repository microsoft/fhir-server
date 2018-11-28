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
        private readonly ResultExecutedContext _executedContext;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private readonly IFhirRequestContext _fhirRequestContext = Substitute.For<IFhirRequestContext>();
        private readonly IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();
        private readonly FhirResult _fhirResult;
        private readonly IOptions<SecurityConfiguration> _securityOptions = Substitute.For<IOptions<SecurityConfiguration>>();
        private readonly SecurityConfiguration _securityConfiguration = Substitute.For<SecurityConfiguration>();
        private readonly ClaimsPrincipal _claimsPrincipal = Substitute.For<ClaimsPrincipal>();

        private readonly AuditLoggingFilterAttribute _filter;

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
            _fhirRequestContext.RequestType.Returns(new Coding("System", "TestRequestType"));
            _fhirRequestContext.RequestSubType = new Coding("System", "TestRequestSubType");
            _fhirRequestContext.Uri.Returns(new Uri("https://fhirtest/fhir?count=100"));
            _fhirRequestContextAccessor.FhirRequestContext.Returns(_fhirRequestContext);
            _fhirRequestContextAccessor.FhirRequestContext.Principal.Returns(_claimsPrincipal);

            _securityConfiguration.LastModifiedClaims.Returns(new HashSet<string> { "claim1" });
            _securityOptions.Value.Returns(_securityConfiguration);
            _claimsPrincipal.Claims.Returns(new List<System.Security.Claims.Claim> { Claim1 });

            _claims = new KeyValuePair<string, string>[]
            {
                KeyValuePair.Create("claim", "value"),
            };

            _claimsIndexer = Substitute.For<IClaimsIndexer>();

            _claimsIndexer.Extract().Returns(_claims);

            _filter = new AuditLoggingFilterAttribute(
                _auditLogger,
                _fhirRequestContextAccessor,
                _claimsIndexer);
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

            var descriptor = executingContext.ActionDescriptor as ControllerActionDescriptor;

            descriptor.MethodInfo = typeof(FilterTestsHelper).GetMethod("MethodWithAnonymousAttribute");
            _filter.OnActionExecuting(executingContext);
            _auditLogger.DidNotReceiveWithAnyArgs().LogAudit(Arg.Any<AuditAction>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Uri>(), Arg.Any<HttpStatusCode?>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<KeyValuePair<string, string>>>());
            _executedContext.ActionDescriptor = executingContext.ActionDescriptor;
            _filter.OnResultExecuted(_executedContext);
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

            var descriptor = executingContext.ActionDescriptor as ControllerActionDescriptor;

            var claims = _claimsIndexer.Extract();

            descriptor.MethodInfo = typeof(FilterTestsHelper).GetMethod("MethodWithAuditEventAttribute");
            _filter.OnActionExecuting(executingContext);
            _auditLogger.Received(1).LogAudit(AuditAction.Executing, _fhirRequestContext.RequestSubType.Code, null, _fhirRequestContext.Uri, null, _fhirRequestContext.CorrelationId, _claims);
            _executedContext.ActionDescriptor = executingContext.ActionDescriptor;
            _filter.OnResultExecuted(_executedContext);
            _auditLogger.Received(2).LogAudit(Arg.Any<AuditAction>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Uri>(), Arg.Any<HttpStatusCode?>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<KeyValuePair<string, string>>>());
            _auditLogger.Received(1).LogAudit(AuditAction.Executed, _fhirRequestContext.RequestSubType.Code, _fhirResult.Resource.TypeName, _fhirRequestContext.Uri, _fhirResult.StatusCode, _fhirRequestContext.CorrelationId, _claims);
        }

        [Theory]
        [InlineData(nameof(FhirController.Create), AuditEventSubType.Create)]
        [InlineData(nameof(FhirController.Update), AuditEventSubType.Update)]
        [InlineData(nameof(FhirController.Read), AuditEventSubType.Read)]
        [InlineData(nameof(FhirController.SystemHistory), AuditEventSubType.HistorySystem)]
        [InlineData(nameof(FhirController.TypeHistory), AuditEventSubType.HistoryType)]
        [InlineData(nameof(FhirController.History), AuditEventSubType.HistoryInstance)]
        [InlineData(nameof(FhirController.VRead), AuditEventSubType.VRead)]
        [InlineData(nameof(FhirController.Delete), AuditEventSubType.Delete)]
        [InlineData(nameof(FhirController.SearchByResourceType), AuditEventSubType.SearchType)]
        [InlineData(nameof(FhirController.SearchByResourceTypePost), AuditEventSubType.SearchType)]
        [InlineData(nameof(FhirController.Search), AuditEventSubType.SearchSystem)]
        [InlineData(nameof(FhirController.SearchPost), AuditEventSubType.SearchSystem)]
        [InlineData(nameof(FhirController.SearchCompartmentByResourceType), AuditEventSubType.Search)]
        public void GivenAFhirRequest_WhenExecutingAnValidAction_ThenCorrectRequestSubTypeMustBeSet(string methodName, string auditEventSubType)
        {
            var executingContext = new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ControllerActionDescriptor() { DisplayName = "Executing Context Test Descriptor" }),
                new List<IFilterMetadata>(),
                _actionArguments,
                FilterTestsHelper.CreateMockFhirController());

            var fhirController = executingContext.Controller as FhirController;

            AssertProperRequestSubTypeSet(executingContext, methodName, auditEventSubType, _filter);
        }

        private void AssertProperRequestSubTypeSet(ActionExecutingContext executingContext, string methodName, string requestSubType, AuditLoggingFilterAttribute filter)
        {
            executingContext.ActionDescriptor = new ControllerActionDescriptor();
            var descriptor = executingContext.ActionDescriptor as ControllerActionDescriptor;

            descriptor.MethodInfo = typeof(FhirController).GetMethod(methodName);
            filter.OnActionExecuting(executingContext);
            Assert.Equal(_fhirRequestContextAccessor.FhirRequestContext.RequestSubType.Code, requestSubType);
            Assert.Equal(_fhirRequestContextAccessor.FhirRequestContext.RequestSubType.System, AuditEventSubType.System);
        }

        [Fact]
        public void GivenAFhirRequest_WhenExecutingAnActionWithoutAttributes_ThenException()
        {
            var executingContext = new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ControllerActionDescriptor() { DisplayName = "Executing Context Test Descriptor" }),
                new List<IFilterMetadata>(),
                _actionArguments,
                FilterTestsHelper.CreateMockFhirController());

            var descriptor = executingContext.ActionDescriptor as ControllerActionDescriptor;

            descriptor.MethodInfo = typeof(FilterTestsHelper).GetMethod("MethodWithNoAttribute");

            var excp = Assert.Throws<NotSupportedException>(() => _filter.OnActionExecuting(executingContext));
            Assert.Contains(excp.Message, "Audit Event Sub Type is not set for method MethodWithNoAttribute.");
        }
    }
}
