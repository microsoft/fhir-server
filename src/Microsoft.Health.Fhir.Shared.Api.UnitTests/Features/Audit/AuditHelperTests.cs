// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Api.Features.AnonymousOperations;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Audit
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Audit)]
    public class AuditHelperTests
    {
        private const string AuditEventType = AuditEventSubType.Create;
        private const string CorrelationId = "correlation";
        private static readonly Uri Uri = new Uri("http://localhost/123");
        private static readonly IReadOnlyCollection<KeyValuePair<string, string>> Claims = new List<KeyValuePair<string, string>>();
        private static readonly IPAddress CallerIpAddress = new IPAddress(new byte[] { 0xA, 0x0, 0x0, 0x0 }); // 10.0.0.0
        private const string CallerIpAddressInString = "10.0.0.0";

        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        private readonly IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();
        private readonly IAuditHeaderReader _auditHeaderReader = Substitute.For<IAuditHeaderReader>();

        private readonly IFhirRequestContext _fhirRequestContext = Substitute.For<IFhirRequestContext>();

        private readonly IAuditHelper _auditHelper;

        private readonly HttpContext _httpContext = new DefaultHttpContext();
        private readonly IClaimsExtractor _claimsExtractor = Substitute.For<IClaimsExtractor>();

        public AuditHelperTests()
        {
            _fhirRequestContext.Uri.Returns(Uri);
            _fhirRequestContext.CorrelationId.Returns(CorrelationId);
            _fhirRequestContext.ResourceType.Returns("Patient");

            _fhirRequestContextAccessor.RequestContext = _fhirRequestContext;

            _httpContext.Connection.RemoteIpAddress = CallerIpAddress;

            _claimsExtractor.Extract().Returns(Claims);

            _auditHelper = new AuditHelper(_fhirRequestContextAccessor, _auditLogger, _auditHeaderReader);
        }

        [Fact]
        public void GivenNoAuditEventType_WhenLogExecutingIsCalled_ThenAuditLogShouldNotBeLogged()
        {
            _auditHelper.LogExecuting(_httpContext, _claimsExtractor);

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

        [Theory]
        [InlineData(FhirAnonymousOperationType.Metadata)]
        [InlineData(FhirAnonymousOperationType.Versions)]
        public void GivenInvalidAuditEventType_WhenLogExecutingIsCalled_ThenAuditLogShouldNotBeLogged(string auditEventType)
        {
            _fhirRequestContext.AuditEventType.Returns(auditEventType);

            _auditHelper.LogExecuting(_httpContext, _claimsExtractor);

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
        public void GivenAuditEventType_WhenLogExecutingIsCalled_ThenAuditLogShouldBeLogged()
        {
            _fhirRequestContext.AuditEventType.Returns(AuditEventType);

            _auditHelper.LogExecuting(_httpContext, _claimsExtractor);

            _auditLogger.Received(1).LogAudit(
                AuditAction.Executing,
                AuditEventType,
                resourceType: "Patient",
                requestUri: Uri,
                statusCode: null,
                correlationId: CorrelationId,
                callerIpAddress: CallerIpAddressInString,
                callerClaims: Claims,
                customHeaders: _auditHeaderReader.Read(_httpContext),
                operationType: Arg.Any<string>(),
                callerAgent: Arg.Any<string>());
        }

        [Fact]
        public void GivenNoAuditEventType_WhenLogExecutedIsCalled_ThenAuditLogShouldNotBeLogged()
        {
            _auditHelper.LogExecuted(_httpContext, _claimsExtractor);

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

        [Theory]
        [InlineData(FhirAnonymousOperationType.Metadata)]
        [InlineData(FhirAnonymousOperationType.Versions)]
        public void GivenInvalidAuditEventType_WhenLogExecutedIsCalled_ThenAuditLogShouldNotBeLogged(string auditEventType)
        {
            _fhirRequestContext.AuditEventType.Returns(auditEventType);

            _auditHelper.LogExecuted(_httpContext, _claimsExtractor);

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
        public void GivenAuditEventType_WhenLogExecutedIsCalled_ThenAuditLogShouldBeLogged()
        {
            const HttpStatusCode expectedStatusCode = HttpStatusCode.Created;
            const string expectedResourceType = "Patient";

            _fhirRequestContext.AuditEventType.Returns(AuditEventType);
            _fhirRequestContext.ResourceType.Returns(expectedResourceType);

            _httpContext.Response.StatusCode = (int)expectedStatusCode;

            _auditHelper.LogExecuted(_httpContext, _claimsExtractor);

            _auditLogger.Received(1).LogAudit(
                AuditAction.Executed,
                AuditEventType,
                expectedResourceType,
                Uri,
                expectedStatusCode,
                CorrelationId,
                CallerIpAddressInString,
                Claims,
                customHeaders: _auditHeaderReader.Read(_httpContext),
                operationType: Arg.Any<string>(),
                callerAgent: Arg.Any<string>());
        }

        [Fact]
        public void GivenDuration_WhenLogExecutedIsCalled_ThenAdditionalPropertiesIsNotNullInAuditLog()
        {
            long durationMs = 1123;
            const HttpStatusCode expectedStatusCode = HttpStatusCode.Created;
            const string expectedResourceType = "Patient";

            _fhirRequestContext.AuditEventType.Returns(AuditEventType);
            _fhirRequestContext.ResourceType.Returns(expectedResourceType);
            _httpContext.Response.StatusCode = (int)expectedStatusCode;

            _auditHelper.LogExecuted(_httpContext, _claimsExtractor, durationMs: durationMs);

            _auditLogger.Received(1).LogAudit(
                AuditAction.Executed,
                AuditEventType,
                expectedResourceType,
                Uri,
                expectedStatusCode,
                CorrelationId,
                CallerIpAddressInString,
                Claims,
                customHeaders: _auditHeaderReader.Read(_httpContext),
                operationType: Arg.Any<string>(),
                callerAgent: Arg.Any<string>(),
                additionalProperties: Arg.Is<Dictionary<string, string>>(d => d.ContainsKey(AuditHelper.ProcessingDurationMs) && d.ContainsValue(durationMs.ToString())));
        }

        [Fact]
        public void GivenAuditHelper_WhenLogExecutingIsCalled_ThenCallerAgentShouldAlwaysBeDefaultCallerAgent()
        {
            _fhirRequestContext.AuditEventType.Returns(AuditEventType);

            _auditHelper.LogExecuting(_httpContext, _claimsExtractor);

            _auditLogger.Received().LogAudit(
                auditAction: Arg.Any<AuditAction>(),
                operation: Arg.Any<string>(),
                resourceType: Arg.Any<string>(),
                requestUri: Arg.Any<Uri>(),
                statusCode: Arg.Any<HttpStatusCode?>(),
                correlationId: Arg.Any<string>(),
                callerIpAddress: Arg.Any<string>(),
                callerClaims: Arg.Any<IReadOnlyCollection<KeyValuePair<string, string>>>(),
                customHeaders: Arg.Any<IReadOnlyDictionary<string, string>>(),
                operationType: Arg.Any<string>(),
                callerAgent: AuditHelper.DefaultCallerAgent);
        }

        [Theory]
        [InlineData("DELETE", "DELETE")]
        [InlineData("GET", "GET")]
        [InlineData("PATCH", "PATCH")]
        [InlineData("POST", "POST")]
        [InlineData("PUT", "PUT")]
        [InlineData(" DELETE   ", "DELETE")]
        [InlineData("INVALID", AuditHelper.UnknownOperationType)]
        [InlineData("1234", AuditHelper.UnknownOperationType)]
        [InlineData("\nPOSTT\n", AuditHelper.UnknownOperationType)]
        [InlineData("\r\n  PUT   \r\n", "PUT")]
        [InlineData("   ", AuditHelper.UnknownOperationType)]
        [InlineData("PAT\r\nCH", AuditHelper.UnknownOperationType)]
        [InlineData(null, AuditHelper.UnknownOperationType)]
        public void GivenOperationType_WhenLogAuditIsCalled_ThenRightOperationTypeShouldBeLogged(
            string actualOperationType,
            string expectedOperationType)
        {
            _fhirRequestContext.AuditEventType.Returns(AuditEventType);
            _httpContext.Request.Method = actualOperationType;

            _auditHelper.LogExecuting(_httpContext, _claimsExtractor);

            _auditLogger.Received().LogAudit(
                auditAction: Arg.Any<AuditAction>(),
                operation: Arg.Any<string>(),
                resourceType: Arg.Any<string>(),
                requestUri: Arg.Any<Uri>(),
                statusCode: Arg.Any<HttpStatusCode?>(),
                correlationId: Arg.Any<string>(),
                callerIpAddress: Arg.Any<string>(),
                callerClaims: Arg.Any<IReadOnlyCollection<KeyValuePair<string, string>>>(),
                customHeaders: Arg.Any<IReadOnlyDictionary<string, string>>(),
                operationType: expectedOperationType,
                callerAgent: AuditHelper.DefaultCallerAgent);
        }

        [Fact]
        public void GivenAuditEventWithLogInjectionAttack_WhenLogExecutedIsCalled_ThenAuditLogHasSanitzedInput()
        {
            const HttpStatusCode expectedStatusCode = HttpStatusCode.Created;
            const string expectedResourceType = "Patient";
            var headers = new Dictionary<string, string>
            {
                { "CustomHeader", "<div>injection attack </div>" },
            };
            ReadOnlyDictionary<string, string> customHeaders = new ReadOnlyDictionary<string, string>(headers);
            var securityConfig = new SecurityConfiguration();
            IOptions<SecurityConfiguration> optionsConfig = Options.Create(securityConfig);
            var logger = new TestLogger();
            var auditLogger = new AuditLogger(optionsConfig, logger);

            auditLogger.LogAudit(
                AuditAction.Executed,
                AuditEventType,
                expectedResourceType,
                Uri,
                expectedStatusCode,
                CorrelationId,
                CallerIpAddressInString,
                Claims,
                customHeaders: customHeaders);

            var expectedHeaders = HttpUtility.HtmlEncode(string.Join(";", customHeaders.Select(header => $"{header.Key}={header.Value}")));
            Assert.Contains(expectedHeaders, logger.LogRecords.First().State);
        }

        private class TestLogger : ILogger<IAuditLogger>
        {
            internal List<LogRecord> LogRecords { get; } = new List<LogRecord>();

            public IDisposable BeginScope<TState>(TState state)
            {
                return null;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                LogRecords.Add(new LogRecord() { LogLevel = logLevel, State = state.ToString() });
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return false;
            }

            internal class LogRecord
            {
                internal LogLevel LogLevel { get; init; }

                internal string State { get; init; }
            }
        }
    }
}
