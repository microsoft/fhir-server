// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Security;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Audit
{
    public class AuditHelperTests
    {
        private const string ControllerName = "controller";
        private const string AnonymousActionName = "anonymous";
        private const string NonAnonymousActionName = "non-anonymous";
        private const string AuditEventType = "audit";
        private const string CorrelationId = "correlation";
        private static readonly Uri Uri = new Uri("http://localhost/123");
        private static readonly IReadOnlyCollection<KeyValuePair<string, string>> Claims = new List<KeyValuePair<string, string>>();
        private static readonly IPAddress CallerIpAddress = new IPAddress(new byte[] { 0xA, 0x0, 0x0, 0x0 }); // 10.0.0.0
        private const string CallerIpAddressInString = "10.0.0.0";

        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private readonly IAuditEventTypeMapping _auditEventTypeMapping = Substitute.For<IAuditEventTypeMapping>();
        private readonly IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();

        private readonly IFhirRequestContext _fhirRequestContext = Substitute.For<IFhirRequestContext>();

        private readonly IAuditHelper _auditHelper;

        private readonly HttpContext _httpContext = new DefaultHttpContext();
        private readonly IClaimsExtractor _claimsExtractor = Substitute.For<IClaimsExtractor>();
        private readonly IAuditHeaderReader _auditHeaderReader = Substitute.For<IAuditHeaderReader>();

        public AuditHelperTests()
        {
            _fhirRequestContext.Uri.Returns(Uri);
            _fhirRequestContext.CorrelationId.Returns(CorrelationId);

            _fhirRequestContextAccessor.FhirRequestContext = _fhirRequestContext;

            _auditEventTypeMapping.GetAuditEventType(ControllerName, AnonymousActionName).Returns((string)null);
            _auditEventTypeMapping.GetAuditEventType(ControllerName, NonAnonymousActionName).Returns(AuditEventType);

            _httpContext.Connection.RemoteIpAddress = CallerIpAddress;

            _claimsExtractor.Extract().Returns(Claims);

            _auditHelper = new AuditHelper(_fhirRequestContextAccessor, _auditEventTypeMapping, _auditLogger, NullLogger<AuditHelper>.Instance, _auditHeaderReader);
        }

        [Fact]
        public void GivenAnActionHasAllowAnonymousAttribute_WhenLogExecutingIsCalled_ThenAuditLogShouldNotBeLogged()
        {
            _auditHelper.LogExecuting(ControllerName, AnonymousActionName, _httpContext, _claimsExtractor);

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
            _auditHelper.LogExecuting(ControllerName, NonAnonymousActionName, _httpContext, _claimsExtractor);

            _auditLogger.Received(1).LogAudit(
                AuditAction.Executing,
                AuditEventType,
                resourceType: null,
                requestUri: Uri,
                statusCode: null,
                correlationId: CorrelationId,
                callerIpAddress: CallerIpAddressInString,
                callerClaims: Claims,
                customHeaders: _auditHeaderReader.Read(_httpContext));
        }

        [Fact]
        public void GivenAnActionHasAllowAnonymousAttribute_WhenLogExecutedIsCalled_ThenAuditLogShouldNotBeLogged()
        {
            // OK
            _auditHelper.LogExecuted(ControllerName, AnonymousActionName, "Patient", _httpContext, _claimsExtractor);

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

            _auditHelper.LogExecuted(ControllerName, NonAnonymousActionName, expectedResourceType, _httpContext, _claimsExtractor);

            _auditLogger.Received(1).LogAudit(
                AuditAction.Executed,
                AuditEventType,
                expectedResourceType,
                Uri,
                expectedStatusCode,
                CorrelationId,
                CallerIpAddressInString,
                Claims,
                customHeaders: _auditHeaderReader.Read(_httpContext));
        }

        [Fact]
        public void GivenAnActionHasAuditEventTypeAttribute_WhenFhirRequestContextHasAuditValue_ThenAuditLogShouldBeLoggedWithFhirRequestValue()
        {
            string otherAuditEventType = "other-audit";

            _fhirRequestContext.AuditEventType.Returns(otherAuditEventType);

            _auditHelper.LogExecuting(ControllerName, NonAnonymousActionName, _httpContext, _claimsExtractor);

            _auditLogger.Received(1).LogAudit(
                AuditAction.Executing,
                otherAuditEventType,
                resourceType: null,
                requestUri: Uri,
                statusCode: null,
                correlationId: CorrelationId,
                callerIpAddress: CallerIpAddressInString,
                callerClaims: Claims,
                customHeaders: _auditHeaderReader.Read(_httpContext));
        }
    }
}
