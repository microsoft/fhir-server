// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Storage;
using SortOrder = Microsoft.Health.Fhir.Core.Features.Search.SortOrder;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    /// <summary>
    /// SQL Server search service implementation.
    /// </summary>
    internal partial class SqlServerSearchService
    {
        private async Task<SearchResult> SearchIncludeImpl(SqlSearchOptions sqlSearchOptions, bool reuseQueryPlans, CancellationToken cancellationToken)
        {
            var includesContinuationToken = IncludesContinuationToken.FromString(sqlSearchOptions.IncludesContinuationToken);
            if (includesContinuationToken == null)
            {
                _logger.LogWarning("Bad Request (InvalidIncludesContinuationToken)");
                throw new BadRequestException(Resources.InvalidIncludesContinuationToken);
            }

            var continuationToken = ContinuationToken.FromString(sqlSearchOptions.ContinuationToken);

            var originalSort = new List<(SearchParameterInfo, SortOrder)>(sqlSearchOptions.Sort);

            // Old expression tree pipeline removed - SQL generation is now handled by SearchParameterSqlParser.ParseMultiple

            SearchResult searchResult = null;

            await _sqlRetryService.ExecuteSql(
                async (connection, cancellationToken, sqlException) =>
                {
                    using (SqlCommand sqlCommand = connection.CreateCommand())
                    {
                        sqlCommand.CommandTimeout = (int)_sqlServerDataStoreConfiguration.CommandTimeout.TotalSeconds;
                        bool canReuseQueryPlan = reuseQueryPlans &&
                            _queryPlanReuseChecker.CanReuseQueryPlan(sqlSearchOptions);
                        var parameterManager = new HashingSqlQueryParameterManager(
                            new SqlQueryParameterManager(sqlCommand.Parameters));

                        var queryText = _searchParameterSqlParser.ParseMultiple(
                            sqlSearchOptions.QueryParams,
                            sqlSearchOptions,
                            parameterManager,
                            canReuseQueryPlan,
                            continuationToken: continuationToken,
                            includesContinuationToken: includesContinuationToken);

                        if (string.IsNullOrEmpty(queryText))
                        {
                            searchResult = new SearchResult(
                                Enumerable.Empty<SearchResultEntry>().ToList(),
                                null,
                                originalSort,
                                sqlSearchOptions.UnsupportedSearchParams);
                            return;
                        }

                        var queryHash = _queryHashCalculator.CalculateHash(queryText);
                        _logger.LogInformation("SQL Search Service includes query hash: {QueryHash}", queryHash);
                        var customQuery = CustomQueries.CheckQueryHash(connection, queryHash, _logger);

                        if (!string.IsNullOrEmpty(customQuery))
                        {
                            queryText = customQuery;
                            sqlCommand.CommandType = CommandType.StoredProcedure;
                        }

#pragma warning disable CA2100
                        sqlCommand.CommandText = queryText;
#pragma warning restore CA2100

                        LogSqlCommand(sqlCommand);

                        var executionStopwatch = Stopwatch.StartNew();

                        try
                        {
                            using (var reader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                            {
                                var moreResults = false;
                                var moreResultsSurrogateIdCutOff = 0L;
                                var moreResultsResourceTypeId = 0;
                                var resources = new List<SearchResultEntry>(sqlSearchOptions.IncludeCount);

                                while (await reader.ReadAsync(cancellationToken))
                                {
                                    ReadWrapper(
                                        reader,
                                        false,
                                        out short resourceTypeId,
                                        out string resourceId,
                                        out int version,
                                        out bool isDeleted,
                                        out long resourceSurrogateId,
                                        out string requestMethod,
                                        out bool isMatch,
                                        out bool isPartialEntry,
                                        out bool isRawResourceMetaSet,
                                        out string searchParameterHash,
                                        out byte[] rawResourceBytes,
                                        out bool isInvisible,
                                        out bool isHistory);

                                    if (isInvisible)
                                    {
                                        continue;
                                    }

                                    if (resources.Count < sqlSearchOptions.IncludeCount)
                                    {
                                        var rawResource = new Lazy<string>(() =>
                                        {
                                            using var rawResourceStream = new MemoryStream(rawResourceBytes);
                                            var decompressedResource = _compressedRawResourceConverter.ReadCompressedRawResource(rawResourceStream);

                                            if (string.IsNullOrEmpty(decompressedResource))
                                            {
                                                decompressedResource = MissingResourceFactory.CreateJson(resourceId, _model.GetResourceTypeName(resourceTypeId), "warning", "incomplete");
                                                _requestContextAccessor.SetMissingResourceCode(System.Net.HttpStatusCode.PartialContent);
                                            }

                                            return decompressedResource;
                                        });

                                        resources.Add(new SearchResultEntry(
                                            new ResourceWrapper(
                                                resourceId,
                                                version.ToString(CultureInfo.InvariantCulture),
                                                _model.GetResourceTypeName(resourceTypeId),
                                                new RawResource(rawResource, FhirResourceFormat.Json, isMetaSet: isRawResourceMetaSet),
                                                new ResourceRequest(requestMethod),
                                                resourceSurrogateId.ToLastUpdated(),
                                                isDeleted,
                                                null,
                                                null,
                                                null,
                                                searchParameterHash,
                                                resourceSurrogateId)
                                            {
                                                IsHistory = isHistory,
                                            },
                                            SearchEntryMode.Include));
                                    }
                                    else
                                    {
                                        moreResultsResourceTypeId = resourceTypeId;
                                        moreResultsSurrogateIdCutOff = resourceSurrogateId - 1;
                                        moreResults = true;
                                        break;
                                    }
                                }

                                await reader.NextResultAsync(cancellationToken);

                                IncludesContinuationToken nextIncludesContinuationToken = null;
                                if (moreResults)
                                {
                                    _logger.LogWarning("Bundle Partial Result (TruncatedIncludeMessage)");
                                    nextIncludesContinuationToken = new IncludesContinuationToken(
                                        new object[]
                                        {
                                            includesContinuationToken.MatchResourceTypeId,
                                            includesContinuationToken.MatchResourceSurrogateIdMin,
                                            includesContinuationToken.MatchResourceSurrogateIdMax,
                                            moreResultsResourceTypeId,
                                            moreResultsSurrogateIdCutOff,
                                            includesContinuationToken.SortQuerySecondPhase,
                                        });
                                }

                                searchResult = new SearchResult(
                                    resources,
                                    null,
                                    originalSort,
                                    sqlSearchOptions.UnsupportedSearchParams,
                                    null,
                                    nextIncludesContinuationToken?.ToJson());
                            }
                        }
                        finally
                        {
                            executionStopwatch.Stop();

                            if (executionStopwatch.ElapsedMilliseconds > _longRunningThreshold.GetValue(_sqlRetryService) && _longRunningQueryDetails.IsEnabled(_sqlRetryService))
                            {
                                string queryTextSnapshot = sqlCommand.CommandText;
                                bool isStoredProcSnapshot = sqlCommand.CommandType == CommandType.StoredProcedure;
                                long executionTimeSnapshot = executionStopwatch.ElapsedMilliseconds;

                                // Always records the long-running warning. Query Store enrichment is
                                // best-effort and appended asynchronously only when a diagnostic slot is free.
                                FireAndForgetQueryStoreLookup(queryTextSnapshot, isStoredProcSnapshot, executionTimeSnapshot);
                            }
                        }
                    }
                },
                _logger,
                cancellationToken,
                true);

            return searchResult;
        }
    }
}
