// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Security;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Audit
{
    public class AuditHelperTests
    {
        private const string ControllerName = nameof(MockController);
        private const string AnonymousMethodName = nameof(MockController.Anonymous);
        private const string AudittedMethodName = nameof(MockController.Auditted);
        private const string NoAttributeMethodName = nameof(MockController.NoAttribute);
        private const string AuditEventType = "audit";
        private const string CorrelationId = "correlation";
        private static readonly Uri Uri = new Uri("http://localhost/123");
        private static readonly IReadOnlyCollection<KeyValuePair<string, string>> Claims = new List<KeyValuePair<string, string>>();
        private static readonly IPAddress CallerIpAddress = new IPAddress(new byte[] { 0xA, 0x0, 0x0, 0x0 }); // 10.0.0.0
        private const string CallerIpAddressInString = "10.0.0.0";

        private readonly IActionDescriptorCollectionProvider _actionDescriptorCollectionProvider = Substitute.For<IActionDescriptorCollectionProvider>();
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private readonly IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();

        private readonly IFhirRequestContext _fhirRequestContext = Substitute.For<IFhirRequestContext>();

        private readonly IAuditHelper _auditHelper;

        private readonly HttpContext _httpContext = new DefaultHttpContext();
        private readonly IClaimsExtractor _claimsExtractor = Substitute.For<IClaimsExtractor>();

        public AuditHelperTests()
        {
            Type mockControllerType = typeof(MockController);

            var actionDescriptors = new List<ActionDescriptor>()
            {
                new ControllerActionDescriptor()
                {
                    ControllerName = ControllerName,
                    ActionName = AnonymousMethodName,
                    MethodInfo = mockControllerType.GetMethod(AnonymousMethodName),
                },
                new ControllerActionDescriptor()
                {
                    ControllerName = ControllerName,
                    ActionName = AudittedMethodName,
                    MethodInfo = mockControllerType.GetMethod(AudittedMethodName),
                },
                new ControllerActionDescriptor()
                {
                    ControllerName = ControllerName,
                    ActionName = NoAttributeMethodName,
                    MethodInfo = mockControllerType.GetMethod(NoAttributeMethodName),
                },
                new PageActionDescriptor()
                {
                },
            };

            var actionDescriptorCollection = new ActionDescriptorCollection(actionDescriptors, 1);

            _actionDescriptorCollectionProvider.ActionDescriptors.Returns(actionDescriptorCollection);

            _fhirRequestContext.Uri.Returns(Uri);
            _fhirRequestContext.CorrelationId.Returns(CorrelationId);

            _fhirRequestContextAccessor.FhirRequestContext = _fhirRequestContext;

            _httpContext.Connection.RemoteIpAddress = CallerIpAddress;

            _claimsExtractor.Extract().Returns(Claims);

            _auditHelper = new AuditHelper(_actionDescriptorCollectionProvider, _fhirRequestContextAccessor, _auditLogger, NullLogger<AuditHelper>.Instance);

            ((IStartable)_auditHelper).Start();
        }

        [Theory]
        [InlineData(ControllerName, AnonymousMethodName, null)]
        [InlineData(ControllerName, AudittedMethodName, AuditEventType)]
        public void GivenControllerNameAndActionName_WhenGetAuditEventTypeIsCalled_ThenAuditEventTypeShouldBeReturned(string controllerName, string actionName, string expectedAuditEventType)
        {
            string actualAuditEventType = _auditHelper.GetAuditEventType(controllerName, actionName);

            Assert.Equal(expectedAuditEventType, actualAuditEventType);
        }

        [Fact]
        public void GivenUnknownControllerNameAndActionName_WhenGetAuditEventTypeIsCalled_ThenAuditExceptionShouldBeThrown()
        {
            Assert.Throws<AuditException>(() => _auditHelper.GetAuditEventType("test", "action"));
        }

        [Fact]
        public void GivenAnActionHasAllowAnonymousAttribute_WhenLogExecutingIsCalled_ThenAuditLogShouldNotBeLogged()
        {
            _auditHelper.LogExecuting(ControllerName, AnonymousMethodName, _httpContext, _claimsExtractor);

            _auditLogger.DidNotReceiveWithAnyArgs().LogAudit(
                auditAction: default,
                operation: default,
                resourceType: default,
                requestUri: default,
                statusCode: default,
                correlationId: default,
                callerIpAddress: default,
                callerClaims: default);
        }

        [Fact]
        public void GivenAnActionHasAuditEventTypeAttribute_WhenLogExecutingIsCalled_ThenAuditLogShouldBeLogged()
        {
            _auditHelper.LogExecuting(ControllerName, AudittedMethodName, _httpContext, _claimsExtractor);

            _auditLogger.Received(1).LogAudit(
                AuditAction.Executing,
                AuditEventType,
                resourceType: null,
                requestUri: Uri,
                statusCode: null,
                correlationId: CorrelationId,
                callerIpAddress: CallerIpAddressInString,
                callerClaims: Claims);
        }

        [Fact]
        public void GivenUnknownActon_WhenLogExecutingIsCalled_ThenAuditExceptionShouldBeThrown()
        {
            Assert.Throws<AuditException>(() => _auditHelper.LogExecuting("test", "action", _httpContext, _claimsExtractor));
        }

        [Fact]
        public void GivenAnActionHasAllowAnonymousAttribute_WhenLogExecutedIsCalled_ThenAuditLogShouldNotBeLogged()
        {
            // OK
            _auditHelper.LogExecuted(ControllerName, AnonymousMethodName, "Patient", _httpContext, _claimsExtractor);

            _auditLogger.DidNotReceiveWithAnyArgs().LogAudit(
                auditAction: default,
                operation: default,
                resourceType: default,
                requestUri: default,
                statusCode: default,
                correlationId: default,
                callerIpAddress: default,
                callerClaims: default);
        }

        [Fact]
        public void GivenAnActionHasAuditEventTypeAttribute_WhenLogExecutedIsCalled_ThenAuditLogShouldBeLogged()
        {
            const HttpStatusCode expectedStatusCode = HttpStatusCode.Created;
            const string expectedResourceType = "Patient";

            _httpContext.Response.StatusCode = (int)expectedStatusCode;

            _auditHelper.LogExecuted(ControllerName, AudittedMethodName, expectedResourceType, _httpContext, _claimsExtractor);

            _auditLogger.Received(1).LogAudit(
                AuditAction.Executed,
                AuditEventType,
                expectedResourceType,
                Uri,
                expectedStatusCode,
                CorrelationId,
                CallerIpAddressInString,
                Claims);
        }

        [Fact]
        public void GivenUnknownActon_WhenLogExecutedIsCalled_ThenAuditExceptionShouldBeThrown()
        {
            // Created
            Assert.Throws<AuditException>(() => _auditHelper.LogExecuted("test", "action", "Patient", _httpContext, _claimsExtractor));
        }

        private class MockController : Controller
        {
            [AllowAnonymous]
            public IActionResult Anonymous() => new OkResult();

            [AuditEventType(AuditEventType)]
            public IActionResult Auditted() => new OkResult();

            public IActionResult NoAttribute() => new OkResult();
        }
    }
}
