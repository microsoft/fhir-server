// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using AngleSharp.Common;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Audit;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Audit;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Audit
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Audit)]
    public class AuditLoggerTests
    {
        private static readonly string ActionTypeKeyName = "ActionType";
        private static readonly string EventTypeKeyName = "EventType";
        private static readonly string AudienceKeyName = "Audience";
        private static readonly string AuthorityKeyName = "Authority";
        private static readonly string ResourceTypeKeyName = "ResourceType";
        private static readonly string RequestUriKeyName = "RequestUri";
        private static readonly string ActionKeyName = "Action";
        private static readonly string StatusCodeKeyName = "StatusCode";
        private static readonly string CorrelationIdKeyName = "CorrelationId";
        private static readonly string ClaimsKeyName = "Claims";
        private static readonly string CustomHeadersKeyName = "CustomHeaders";
        private static readonly string OperationTypeKeyName = "OperationType";
        private static readonly string CallerAgentKeyName = "CallerAgent";
        private static readonly string NullValue = "(null)";

        private static readonly AuditAction DefaultAuditAction = AuditAction.Executed;
        private static readonly string DefaultOperation = "search-system";
        private static readonly string DefaultResourceType = "Patient";
        private static readonly Uri DefaultRequestUri = new Uri("http://localhost/search");
        private static readonly HttpStatusCode DefaultHttpStatusCode = HttpStatusCode.OK;
        private static readonly string DefaultCorrelationId = Guid.NewGuid().ToString();
        private static readonly string DefaultCallerIpAddress = "10.0.0.0";
        private static readonly string DefaultCallerAgent = "callerAgent";

        private readonly IAuditLogger _auditLogger;
        private readonly IMockLogger<IAuditLogger> _logger;
        private readonly SecurityConfiguration _securityConfiguration;

        public AuditLoggerTests()
        {
            _securityConfiguration = new SecurityConfiguration();
            _logger = new MockLogger<IAuditLogger>();
            _auditLogger = new AuditLogger(Options.Create(_securityConfiguration), _logger);
        }

        [Theory]
        [InlineData("DELETE")]
        [InlineData("GET")]
        [InlineData("PATCH")]
        [InlineData("POST")]
        [InlineData("PUT")]
        public void GivenHttpMethod_WhenLogExecutingIsCalled_ThenAuditLogShouldNotHaveRightOperationType(string httpMethod)
        {
            _auditLogger.LogAudit(
                auditAction: DefaultAuditAction,
                operation: DefaultOperation,
                resourceType: DefaultResourceType,
                requestUri: DefaultRequestUri,
                statusCode: DefaultHttpStatusCode,
                correlationId: DefaultCorrelationId,
                callerIpAddress: DefaultCallerIpAddress,
                callerClaims: null,
                customHeaders: null,
                operationType: httpMethod,
                callerAgent: DefaultCallerAgent);

            var logs = _logger.GetLogs();
            Assert.Equal(1, logs.Count);

            var logElements = logs[0].State
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => new string[] { x.Substring(0, x.IndexOf(':')).Trim(), x.Substring(x.IndexOf(':') + 1).Trim() })
                .ToDictionary(x => x[0], x => x[1].Trim());
            Assert.Equal(DefaultAuditAction.ToString(), logElements[ActionTypeKeyName]);
            Assert.Equal(AuditLogger.AuditEventType, logElements[EventTypeKeyName]);
            Assert.Equal(DefaultOperation, logElements[ActionKeyName]);
            Assert.Equal(DefaultResourceType, logElements[ResourceTypeKeyName]);
            Assert.Equal(DefaultRequestUri.OriginalString, logElements[RequestUriKeyName]);
            Assert.Equal(DefaultHttpStatusCode.ToString(), logElements[StatusCodeKeyName]);
            Assert.Equal(DefaultCorrelationId, logElements[CorrelationIdKeyName]);
            Assert.Equal(NullValue, logElements[AudienceKeyName]);
            Assert.Equal(NullValue, logElements[AuthorityKeyName]);
            Assert.Equal(NullValue, logElements[ClaimsKeyName]);
            Assert.Equal(NullValue, logElements[CustomHeadersKeyName]);
            Assert.Equal(httpMethod, logElements[OperationTypeKeyName]);
            Assert.Equal(DefaultCallerAgent, logElements[CallerAgentKeyName]);
        }

        [Theory]
        [InlineData(" DELETE   ")]
        [InlineData("INVALID")]
        [InlineData("1234")]
        [InlineData("\nPOST\n")]
        [InlineData("\r\nPUT\r\n")]
        public void GivenInvalidHttpMethod_WhenLogExecutingIsCalled_ThenAuditLogShouldNotHaveUnknownOperationType(string httpMethod)
        {
            _auditLogger.LogAudit(
                auditAction: DefaultAuditAction,
                operation: DefaultOperation,
                resourceType: DefaultResourceType,
                requestUri: DefaultRequestUri,
                statusCode: DefaultHttpStatusCode,
                correlationId: DefaultCorrelationId,
                callerIpAddress: DefaultCallerIpAddress,
                callerClaims: null,
                customHeaders: null,
                operationType: httpMethod,
                callerAgent: DefaultCallerAgent);

            var logs = _logger.GetLogs();
            Assert.Equal(1, logs.Count);

            var logElements = logs[0].State
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => new string[] { x.Substring(0, x.IndexOf(':')).Trim(), x.Substring(x.IndexOf(':') + 1).Trim() })
                .ToDictionary(x => x[0], x => x[1].Trim());
            Assert.Equal(DefaultAuditAction.ToString(), logElements[ActionTypeKeyName]);
            Assert.Equal(AuditLogger.AuditEventType, logElements[EventTypeKeyName]);
            Assert.Equal(DefaultOperation, logElements[ActionKeyName]);
            Assert.Equal(DefaultResourceType, logElements[ResourceTypeKeyName]);
            Assert.Equal(DefaultRequestUri.OriginalString, logElements[RequestUriKeyName]);
            Assert.Equal(DefaultHttpStatusCode.ToString(), logElements[StatusCodeKeyName]);
            Assert.Equal(DefaultCorrelationId, logElements[CorrelationIdKeyName]);
            Assert.Equal(NullValue, logElements[AudienceKeyName]);
            Assert.Equal(NullValue, logElements[AuthorityKeyName]);
            Assert.Equal(NullValue, logElements[ClaimsKeyName]);
            Assert.Equal(NullValue, logElements[CustomHeadersKeyName]);
            Assert.Equal(AuditLogger.UnknownOperationType, logElements[OperationTypeKeyName]);
            Assert.Equal(DefaultCallerAgent, logElements[CallerAgentKeyName]);
        }
    }
}
