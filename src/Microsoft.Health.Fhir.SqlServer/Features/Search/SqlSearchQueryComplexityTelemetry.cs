// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal static class SqlSearchQueryComplexityTelemetry
    {
        public static void Record(SearchOptions searchOptions, IFhirRequestContext requestContext, ILogger logger)
        {
            EnsureArg.IsNotNull(searchOptions, nameof(searchOptions));
            EnsureArg.IsNotNull(logger, nameof(logger));

            SqlSearchQueryComplexityResult complexity = SqlSearchQueryComplexityCalculator.Calculate(searchOptions);
            logger.LogInformation(
                "SQL search query complexity: {QueryComplexityTier} ({QueryComplexityScore}) for request {CorrelationId} {FhirOperation} {RequestMethod} {RouteName} {ResourceType}. Includes operation: {IsIncludesOperation}. Count only: {IsCountOnly}. Background task: {IsBackgroundTask}.",
                complexity.Tier,
                complexity.Score,
                requestContext?.CorrelationId,
                requestContext?.AuditEventType,
                requestContext?.Method,
                requestContext?.RouteName,
                requestContext?.ResourceType,
                searchOptions.IsIncludesOperation,
                searchOptions.CountOnly,
                requestContext?.IsBackgroundTask ?? false);

            if (!ShouldWriteResponseHeaders(requestContext))
            {
                return;
            }

            if (!requestContext.ResponseHeaders.ContainsKey(KnownHeaders.QueryComplexityScore))
            {
                requestContext.ResponseHeaders[KnownHeaders.QueryComplexityScore] = complexity.Score.ToString(CultureInfo.InvariantCulture);
            }

            if (!requestContext.ResponseHeaders.ContainsKey(KnownHeaders.QueryComplexityTier))
            {
                requestContext.ResponseHeaders[KnownHeaders.QueryComplexityTier] = complexity.Tier.ToString();
            }
        }

        private static bool ShouldWriteResponseHeaders(IFhirRequestContext requestContext)
        {
            return requestContext?.ResponseHeaders != null &&
                !requestContext.IsBackgroundTask &&
                !requestContext.ExecutingBatchOrTransaction &&
                IsSearchOperation(requestContext.AuditEventType);
        }

        private static bool IsSearchOperation(string auditEventType)
        {
            return auditEventType == AuditEventSubType.Search ||
                auditEventType == AuditEventSubType.SearchType ||
                auditEventType == AuditEventSubType.SearchSystem;
        }
    }
}
