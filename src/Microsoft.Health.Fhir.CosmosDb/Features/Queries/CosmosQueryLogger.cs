// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Queries
{
    /// <summary>
    /// Logger used for logging <see cref="FhirDocumentQuery{T}"/>.
    /// </summary>
    public class CosmosQueryLogger : ICosmosQueryLogger
    {
        private static readonly string QueryExecutingMessageFormat =
            "Executing Query:" + Environment.NewLine +
            "{Query}" + Environment.NewLine +
            "PartitionKey: {PartitionKey}" + Environment.NewLine +
            "ContinuationToken: {ContinuationToken}" + Environment.NewLine +
            "MaxItemCount: {MaxItemCount}" + Environment.NewLine +
            "MaxConcurrency: {MaxConcurrency}";

        private static readonly Action<ILogger, string, string, string, int?, int?, Exception> LogQueryExecutingDelegate =
            LoggerMessage.Define<string, string, string, int?, int?>(
                LogLevel.Information,
                new EventId(EventIds.ExecutingQuery),
                QueryExecutingMessageFormat);

        private static readonly string QueryExecutionResultMessageFormat =
            "ActivityId: {ActivityId}" + Environment.NewLine +
            "Request Charge: {RequestCharge}" + Environment.NewLine +
            "ContinuationToken: {ContinuationToken}" + Environment.NewLine +
            "Count: {Count}" + Environment.NewLine +
            "DurationMs: {DurationMs}" + Environment.NewLine +
            "PartitionKeyRangeId: {PartitionKeyRangeId}";

        private static readonly string QueryExecutionDiagnosticsMessageFormat =
            "ActivityId: {ActivityId}" + Environment.NewLine +
            "Diagnostics: {Diagnostics}";

        private static readonly string QueryExecutionClientElapsedTimeMessageFormat =
            "ActivityId: {ActivityId}" + Environment.NewLine +
            "ClientElapsedTime: {ClientElapsedTime}";

        private static readonly Action<ILogger, string, double, string, int, double, string, Exception> LogQueryExecutionResultDelegate =
            LoggerMessage.Define<string, double, string, int, double, string>(
                LogLevel.Information,
                new EventId(EventIds.ExecutingQuery),
                QueryExecutionResultMessageFormat);

        private static readonly Action<ILogger, string, string, Exception> LogQueryDiagnosticsDelegate =
    LoggerMessage.Define<string, string>(
        LogLevel.Information,
        new EventId(EventIds.ExecutingQuery),
        QueryExecutionDiagnosticsMessageFormat);

        private static readonly Action<ILogger, string, string, Exception> LogQueryClientElapsedTimeDelegate =
    LoggerMessage.Define<string, string>(
        LogLevel.Information,
        new EventId(EventIds.ExecutingQuery),
        QueryExecutionClientElapsedTimeMessageFormat);

        private readonly ILogger<CosmosQueryLogger> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosQueryLogger"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        public CosmosQueryLogger(
            ILogger<CosmosQueryLogger> logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));

            _logger = logger;
        }

        /// <inheritdoc />
        public void LogQueryExecution(QueryDefinition sqlQuerySpec, string partitionKey, string continuationToken, int? maxItemCount, int? maxConcurrency)
        {
            EnsureArg.IsNotNull(sqlQuerySpec, nameof(sqlQuerySpec));

            LogQueryExecutingDelegate(
                _logger,
                sqlQuerySpec.QueryText,
                partitionKey,
                continuationToken,
                maxItemCount,
                maxConcurrency,
                null);
        }

        /// <inheritdoc />
        public void LogQueryExecutionResult(
            string activityId,
            double requestCharge,
            string continuationToken,
            int count,
            double durationMs,
            string partitionKeyRangeId,
            Exception exception = null)
        {
            LogQueryExecutionResultDelegate(
                _logger,
                activityId,
                requestCharge,
                continuationToken,
                count,
                durationMs,
                partitionKeyRangeId,
                exception);
        }

        /// <inheritdoc />
        public void LogQueryDiagnostics(
            string activityId,
            CosmosDiagnostics diagnostics = null,
            Exception exception = null)
        {
            if (diagnostics != null)
            {
                string diagnosticsString = null;
                try
                {
                    // Attempt to get the string representation. This can potentially throw
                    // if the underlying collection is modified during enumeration.
                    diagnosticsString = diagnostics.ToString();

                    if (!string.IsNullOrEmpty(diagnosticsString))
                    {
                        const int MaxLogChunkSize = 16000;
                        string input = diagnosticsString;

                        for (int i = 0; i < input.Length; i += MaxLogChunkSize)
                        {
                            string chunk = input.Substring(i, Math.Min(MaxLogChunkSize, input.Length - i));
                            LogQueryDiagnosticsDelegate(
                                _logger,
                                activityId,
                                chunk,
                                exception); // Pass the original exception if it exists
                        }
                    }
                    else if (exception != null)
                    {
                        // If diagnosticsString is null/empty but an original exception exists, log the original exception.
                        _logger.LogError(exception, "Original exception associated with ActivityId {ActivityId}. Diagnostics were null or empty.", activityId);
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Collection was modified", StringComparison.InvariantCultureIgnoreCase))
                {
                    // Log that we couldn't capture diagnostics due to the specific concurrency error
                    _logger.LogWarning(ex, "Failed to capture Cosmos DB diagnostics string for ActivityId {ActivityId} due to concurrent modification. The operation itself may have succeeded.", activityId);

                    // Log the original exception if one was provided alongside the diagnostics issue
                    if (exception != null)
                    {
                         _logger.LogError(exception, "Original exception associated with ActivityId {ActivityId} when diagnostics capture failed.", activityId);
                    }
                }
                catch (Exception ex)
                {
                    // Log any other unexpected error during diagnostics processing
                     _logger.LogWarning(ex, "Failed to capture Cosmos DB diagnostics string for ActivityId {ActivityId} due to an unexpected error: {ErrorMessage}", activityId, ex.Message);

                     // Log the original exception if one was provided
                     if (exception != null)
                     {
                          _logger.LogError(exception, "Original exception associated with ActivityId {ActivityId} when diagnostics capture failed.", activityId);
                     }
                }
            }
            else if (exception != null)
            {
                // If diagnostics itself was null, but an exception was provided, log the exception.
                _logger.LogError(exception, "Original exception associated with ActivityId {ActivityId} (no diagnostics available).", activityId);
            }
        }

        /// <inheritdoc />
        public void LogQueryClientElapsedTime(
            string activityId,
            string clientElapsedTime = null,
            Exception exception = null)
        {
            if (!string.IsNullOrEmpty(clientElapsedTime))
            {
                LogQueryClientElapsedTimeDelegate(
                        _logger,
                        activityId,
                        clientElapsedTime,
                        exception);
            }
        }
    }
}
