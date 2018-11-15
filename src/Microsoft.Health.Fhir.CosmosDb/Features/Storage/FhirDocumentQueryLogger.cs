// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// Logger used for logging <see cref="FhirDocumentQuery{T}"/>.
    /// </summary>
    public class FhirDocumentQueryLogger : IFhirDocumentQueryLogger
    {
        private static readonly string QueryExecutingMessageFormat =
            "QueryId: {QueryId}" + Environment.NewLine +
            "Executing Query:" + Environment.NewLine +
            "{Query}" + Environment.NewLine +
            "ContinuationToken: {ContinuationToken}" + Environment.NewLine +
            "MaxItemCount: {MaxItemCount}";

        private static readonly Action<ILogger, Guid, string, string, int?, Exception> LogQueryExecutingDelegate =
            LoggerMessage.Define<Guid, string, string, int?>(
                LogLevel.Information,
                new EventId(EventIds.ExecutingQuery),
                QueryExecutingMessageFormat);

        private static readonly string QueryExecutionResultMessageFormat =
            "QueryId: {QueryId}" + Environment.NewLine +
            "ActivityId: {ActivityId}" + Environment.NewLine +
            "Request Charge: {RequestCharge}" + Environment.NewLine +
            "ContinuationToken: {ContinuationToken}" + Environment.NewLine +
            "ETag: {ETag}" + Environment.NewLine +
            "Count: {Count}";

        private static readonly Action<ILogger, Guid, string, double, string, string, int, Exception> LogQueryExecutionResultDelegate =
            LoggerMessage.Define<Guid, string, double, string, string, int>(
                LogLevel.Information,
                new EventId(EventIds.ExecutingQuery),
                QueryExecutionResultMessageFormat);

        private readonly ILogger<IDocumentQuery> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirDocumentQueryLogger"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        public FhirDocumentQueryLogger(
            ILogger<IDocumentQuery> logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));

            _logger = logger;
        }

        /// <inheritdoc />
        public void LogQueryExecution(Guid queryId, SqlQuerySpec sqlQuerySpec, string continuationToken, int? maxItemCount)
        {
            EnsureArg.IsNotNull(sqlQuerySpec, nameof(sqlQuerySpec));

            LogQueryExecutingDelegate(
                _logger,
                queryId,
                sqlQuerySpec.QueryText,
                continuationToken,
                maxItemCount,
                null);
        }

        /// <inheritdoc />
        public void LogQueryExecutionResult(
            Guid queryId,
            string activityId,
            double requestCharge,
            string continuationToken,
            string eTag,
            int count,
            Exception exception = null)
        {
            LogQueryExecutionResultDelegate(
                _logger,
                queryId,
                activityId,
                requestCharge,
                continuationToken,
                eTag,
                count,
                exception);
        }
    }
}
