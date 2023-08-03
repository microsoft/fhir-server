// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Audit;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Features.Audit
{
    /// <summary>
    /// Provides mechanism to log the audit event using default logger.
    /// </summary>
    public class AuditLogger : IAuditLogger
    {
        private const string AuditEventType = "AuditEvent";

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
            "Claims: {Claims}" + Environment.NewLine +
            "CustomHeaders: {CustomHeaders}" + Environment.NewLine +
            "OperationType: {OperationType}" + Environment.NewLine +
            "CallerAgent: {CallerAgent}" + Environment.NewLine +
            "AdditionalProperties: {AdditionalProperties}" + Environment.NewLine;

        private readonly SecurityConfiguration _securityConfiguration;
        private readonly ILogger<IAuditLogger> _logger;

        public AuditLogger(
            IOptions<SecurityConfiguration> securityConfiguration,
            ILogger<IAuditLogger> logger)
        {
            EnsureArg.IsNotNull(securityConfiguration?.Value, nameof(securityConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _securityConfiguration = securityConfiguration.Value;
            _logger = logger;
        }

        /// <inheritdoc />
        public void LogAudit(
            AuditAction auditAction,
            string operation,
            string resourceType,
            Uri requestUri,
            HttpStatusCode? statusCode,
            string correlationId,
            string callerIpAddress,
            IReadOnlyCollection<KeyValuePair<string, string>> callerClaims,
            IReadOnlyDictionary<string, string> customHeaders = null,
            string operationType = null,
            string callerAgent = null,
            IReadOnlyDictionary<string, string> additionalProperties = null)
        {
            string claimsInString = null;
            string customerHeadersInString = null;
            string additionalPropertiesInString = null;

            if (callerClaims != null)
            {
                claimsInString = HttpUtility.HtmlEncode(string.Join(";", callerClaims.Select(claim => $"{claim.Key}={claim.Value}")));
            }

            if (customHeaders != null)
            {
                customerHeadersInString = HttpUtility.HtmlEncode(string.Join(";", customHeaders.Select(header => $"{header.Key}={header.Value}")));
            }

            if (additionalProperties != null)
            {
                additionalPropertiesInString = string.Join(";", additionalProperties.Select(props => $"{props.Key}={props.Value}"));
            }

            _logger.LogInformation(
#pragma warning disable CA2254 // Template should be a static expression
                AuditMessageFormat,
#pragma warning restore CA2254 // Template should be a static expression
                auditAction,
                AuditEventType,
                _securityConfiguration.Authentication?.Audience,
                _securityConfiguration.Authentication?.Authority,
                resourceType,
                requestUri,
                operation,
                statusCode,
                correlationId,
                claimsInString,
                customerHeadersInString,
                operationType,
                callerAgent,
                additionalPropertiesInString);
        }
    }
}
