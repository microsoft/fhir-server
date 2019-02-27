// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    public class AuditLogger : IAuditLogger
    {
        internal const string AuditEventType = "AuditEvent";

        private static readonly string AuditMessageFormat =
            "ActionType: {ActionType}" + Environment.NewLine +
            "EventType: {EventType}" + Environment.NewLine +
            "Audience: {Audience}" + Environment.NewLine +
            "Authority: {Authority}" + Environment.NewLine +
            "ResourceType: {ResourceType}" + Environment.NewLine +
            "RequestUri: {ResourceUri}" + Environment.NewLine +
            "Action: {Action}" + Environment.NewLine +
            "StatusCode: {StatusCode}" + Environment.NewLine +
            "CorrelationId: {CorrelationId}" + Environment.NewLine +
            "Claims: {Claims}";

        private readonly ILogger<IAuditLogger> _logger;
        private readonly SecurityConfiguration _securityConfiguration;

        public AuditLogger(ILogger<IAuditLogger> logger, IOptions<SecurityConfiguration> securityConfiguration)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(securityConfiguration?.Value, nameof(securityConfiguration));

            _logger = logger;
            _securityConfiguration = securityConfiguration.Value;
        }

        public void LogAudit(
            AuditAction auditAction,
            string action,
            string resourceType,
            Uri requestUri,
            HttpStatusCode? statusCode,
            string correlationId,
            IReadOnlyCollection<KeyValuePair<string, string>> claims)
        {
            string claimsInString = null;

            if (claims != null)
            {
                claimsInString = string.Join(";", claims.Select(claim => $"{claim.Key}={claim.Value}"));
            }

            _logger.LogInformation(
                AuditMessageFormat,
                auditAction,
                AuditEventType,
                _securityConfiguration.Authentication?.Audience,
                _securityConfiguration.Authentication?.Authority,
                resourceType,
                requestUri,
                action,
                statusCode,
                correlationId,
                claimsInString);
        }
    }
}
