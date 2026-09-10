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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Rest;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
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
        private async Task<SearchResult> RunSearch(SqlSearchOptions sqlSearchOptions, CancellationToken cancellationToken)
        {
            var fhirContext = _requestContextAccessor.RequestContext;
            if (fhirContext != null
                && fhirContext.Properties.TryGetValue(KnownQueryParameterNames.QueryCaching, out object useQueryCacheObj)
                && useQueryCacheObj != null)
            {
                var useQueryCache = Convert.ToString(useQueryCacheObj);
                if (string.Equals(useQueryCache, QueryCacheSetting.Enabled, StringComparison.OrdinalIgnoreCase))
                {
                    return await SearchImpl(sqlSearchOptions, true, cancellationToken);
                }
                else if (string.Equals(useQueryCache, QueryCacheSetting.Disabled, StringComparison.OrdinalIgnoreCase))
                {
                    return await SearchImpl(sqlSearchOptions, false, cancellationToken);
                }
                else if (string.Equals(useQueryCache, QueryCacheSetting.Both, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Running search with and without query cache.");
                    var stopwatch = Stopwatch.StartNew();

                    using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    var token = tokenSource.Token;

                    var tryWithQueryCache = SearchImpl(sqlSearchOptions, true, token);
                    var tryWithoutQueryCache = SearchImpl(sqlSearchOptions, false, token);

                    var result = await Task.WhenAny(tryWithQueryCache, tryWithoutQueryCache);
                    await tokenSource.CancelAsync();

                    _logger.LogInformation("First search completed in {ElapsedMilliseconds}ms, query cache enabled: {QueryCacheEnabled}.", stopwatch.ElapsedMilliseconds, result == tryWithQueryCache);
                    return await result;
                }
                else // equals default or an invalid value
                {
                    return await SearchImpl(sqlSearchOptions, _fhirSqlServerConfiguration.ReuseQueryPlans, cancellationToken);
                }
            }
            else
            {
                return await SearchImpl(sqlSearchOptions, _fhirSqlServerConfiguration.ReuseQueryPlans, cancellationToken);
            }
        }

        private async Task<SearchResult> SearchImpl(SqlSearchOptions sqlSearchOptions, bool reuseQueryPlans, CancellationToken cancellationToken)
        {
            if (sqlSearchOptions.IsIncludesOperation)
            {
                return await SearchIncludeImpl(sqlSearchOptions, reuseQueryPlans, cancellationToken);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();

            // AND in the continuation token
            ContinuationToken continuationToken = null;
            if (!string.IsNullOrWhiteSpace(sqlSearchOptions.ContinuationToken) && !sqlSearchOptions.CountOnly)
            {
                continuationToken = ContinuationToken.FromString(sqlSearchOptions.ContinuationToken);
                if (continuationToken == null)
                {
                    _logger.LogWarning("Bad Request (InvalidContinuationToken)");
                    throw new BadRequestException(Resources.InvalidContinuationToken);
                }

                if (continuationToken.SortValue != null && continuationToken.SortValue.Equals(SqlSearchConstants.SortSentinelValueForCt, StringComparison.OrdinalIgnoreCase))
                {
                    continuationToken = null;
                    sqlSearchOptions.SortQuerySecondPhase = true;
                }
                else if (continuationToken.SortValue != null
                    && sqlSearchOptions.Sort?.Count > 0
                    && sqlSearchOptions.Sort[0].sortOrder == SortOrder.Ascending
                    && sqlSearchOptions.Sort[0].searchParameterInfo.Code != KnownQueryParameterNames.LastUpdated)
                {
                    // For ascending sort, having a SortValue in the continuation token means we're
                    // paginating within phase 2 (resources WITH the sort parameter). Set SortQuerySecondPhase
                    // so the parser generates a sort CTE rather than a missing query.
                    sqlSearchOptions.SortQuerySecondPhase = true;
                }
            }

            var originalSort = new List<(SearchParameterInfo, SortOrder)>(sqlSearchOptions.Sort);
            var clonedSearchOptions = new SqlSearchOptions(sqlSearchOptions);

            if (clonedSearchOptions.CountOnly && !clonedSearchOptions.QueryParams.Any(kvp => kvp.Key == KnownQueryParameterNames.Summary))
            {
#pragma warning disable IDE0300 // Simplify collection initialization
#pragma warning disable CA1861 // Avoid constant arrays as arguments
                clonedSearchOptions.QueryParams.Add(KnownQueryParameterNames.Summary, new string[] { "count" });
#pragma warning restore CA1861 // Avoid constant arrays as arguments
#pragma warning restore IDE0300 // Simplify collection initialization
            }

            SearchResult searchResult = null;
            await _sqlRetryService.ExecuteSql(
                async (connection, cancellationToken, sqlException) =>
                {
                    using (SqlCommand sqlCommand = connection.CreateCommand()) // WARNING, this code will not set sqlCommand.Transaction. Sql transactions via C#/.NET are not supported in this method.
                    {
                        sqlCommand.CommandTimeout = (int)_sqlServerDataStoreConfiguration.CommandTimeout.TotalSeconds;
                        var isSortValueNeeded = false;

                        var exportTimeTravel = clonedSearchOptions.QueryHints != null && ContainsStartSurrogateId(clonedSearchOptions);
                        if (exportTimeTravel)
                        {
                            PopulateSqlCommandFromQueryHints(clonedSearchOptions, sqlCommand);
                            sqlCommand.CommandTimeout = 1200; // set to 20 minutes, as dataset is usually large
                        }
                        else
                        {
                            bool canReuseQueryPlan = reuseQueryPlans &&
                                _queryPlanReuseChecker.CanReuseQueryPlan(clonedSearchOptions);
                            var parameterManager = new HashingSqlQueryParameterManager(
                                new SqlQueryParameterManager(sqlCommand.Parameters));

                            var queryText = _searchParameterSqlParser.ParseMultiple(
                                clonedSearchOptions.QueryParams,
                                sqlSearchOptions,
                                parameterManager,
                                canReuseQueryPlan,
                                continuationToken);
                            var queryHash = _queryHashCalculator.CalculateHash(queryText);
                            _logger.LogInformation("SQL Search Service query hash: {QueryHash}", queryHash);
                            var customQuery = CustomQueries.CheckQueryHash(connection, queryHash, _logger);
                            isSortValueNeeded = queryText.Contains("SortValue", StringComparison.OrdinalIgnoreCase);

                            if (!string.IsNullOrEmpty(customQuery))
                            {
                                _logger.LogInformation("SQL Search Service, custom Query identified by hash {QueryHash}, {CustomQuery}", queryHash, customQuery);
                                queryText = customQuery;
                                sqlCommand.CommandType = CommandType.StoredProcedure;
                            }

                            // Command text contains no direct user input.
#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
                            sqlCommand.CommandText = queryText;
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities

                            // _logger.LogInformation($"Query.SearchParamIds={string.Join(",", queryGenerator.SearchParamIds)}");
                        }

                        LogSqlCommand(sqlCommand);

                        var st = DateTime.UtcNow;
                        var executionStopwatch = Stopwatch.StartNew();

                        try
                        {
                            using (var reader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                            {
                                if (clonedSearchOptions.CountOnly)
                                {
                                    await reader.ReadAsync(cancellationToken);
                                    long count = reader.GetInt64(0);
                                    if (count > int.MaxValue)
                                    {
                                        _requestContextAccessor.RequestContext.BundleIssues.Add(
                                            new OperationOutcomeIssue(
                                                OperationOutcomeConstants.IssueSeverity.Error,
                                                OperationOutcomeConstants.IssueType.NotSupported,
                                                string.Format(Core.Resources.SearchCountResultsExceedLimit, count, int.MaxValue)));

                                        _logger.LogWarning("Invalid Search Operation (SearchCountResultsExceedLimit)");
                                        throw new InvalidSearchOperationException(string.Format(Core.Resources.SearchCountResultsExceedLimit, count, int.MaxValue));
                                    }

                                    searchResult = new SearchResult((int)count, clonedSearchOptions.UnsupportedSearchParams);

                                    // call NextResultAsync to get the info messages
                                    await reader.NextResultAsync(cancellationToken);

                                    return;
                                }

                                var matchedResources = new List<SearchResultEntry>(sqlSearchOptions.MaxItemCount);
                                var includedResources = new List<SearchResultEntry>(sqlSearchOptions.IncludeCount);
                                short? newContinuationType = null;
                                long? newContinuationId = null;
                                bool moreResults = false;
                                int matchCount = 0;
                                long? matchedResourceSurrogateIdStart = null;

                                string sortValue = null;
                                var isResultPartial = false;

                                while (await reader.ReadAsync(cancellationToken))
                                {
                                    ReadWrapper(
                                        reader,
                                        exportTimeTravel,
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

                                    // If we get to this point, we know there are more results so we need a continuation token
                                    // Additionally, this resource shouldn't be included in the results
                                    if (matchCount >= clonedSearchOptions.MaxItemCount && isMatch)
                                    {
                                        moreResults = true;

                                        continue;
                                    }

                                    Lazy<string> rawResource = new Lazy<string>(() => string.Empty);

                                    if (!clonedSearchOptions.OnlyIds)
                                    {
                                        rawResource = new Lazy<string>(() =>
                                        {
                                            using var rawResourceStream = new MemoryStream(rawResourceBytes);
                                            var decompressedResource = _compressedRawResourceConverter.ReadCompressedRawResource(rawResourceStream);

                                            _logger.LogDebug("{NameOfResourceSurrogateId}: {ResourceSurrogateId}; {NameOfResourceTypeId}: {ResourceTypeId}; Decompressed length: {RawResourceLength}", nameof(resourceSurrogateId), resourceSurrogateId, nameof(resourceTypeId), resourceTypeId, decompressedResource.Length);

                                            if (string.IsNullOrEmpty(decompressedResource))
                                            {
                                                decompressedResource = MissingResourceFactory.CreateJson(resourceId, _model.GetResourceTypeName(resourceTypeId), "warning", "incomplete");
                                                _requestContextAccessor.SetMissingResourceCode(System.Net.HttpStatusCode.PartialContent);
                                            }

                                            return decompressedResource;
                                        });
                                    }

                                    // See if this resource is a continuation token candidate and increase the count
                                    if (isMatch)
                                    {
                                        newContinuationType = resourceTypeId;
                                        newContinuationId = resourceSurrogateId;
                                        if (!matchedResourceSurrogateIdStart.HasValue)
                                        {
                                            matchedResourceSurrogateIdStart = resourceSurrogateId;
                                        }

                                        // If sort value needed, that means we have an extra column tracking sort value.
                                        // Keep track of sort value if this is the last row.
                                        if (matchCount == clonedSearchOptions.MaxItemCount - 1 && isSortValueNeeded)
                                        {
                                            var tempSortValue = reader.GetValue(SortValueColumnName);
                                            sortValue = (tempSortValue as DateTime?) != null ? (tempSortValue as DateTime?).Value.ToString("o") : tempSortValue.ToString();
                                        }

                                        matchCount++;
                                        matchedResources.Add(new SearchResultEntry(
                                            new ResourceWrapper(
                                                resourceId,
                                                version.ToString(CultureInfo.InvariantCulture),
                                                _model.GetResourceTypeName(resourceTypeId),
                                                clonedSearchOptions.OnlyIds ? null : new RawResource(rawResource, FhirResourceFormat.Json, isMetaSet: isRawResourceMetaSet),
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
                                            SearchEntryMode.Match));
                                    }
                                    else
                                    {
                                        includedResources.Add(new SearchResultEntry(
                                            new ResourceWrapper(
                                                resourceId,
                                                version.ToString(CultureInfo.InvariantCulture),
                                                _model.GetResourceTypeName(resourceTypeId),
                                                clonedSearchOptions.OnlyIds ? null : new RawResource(rawResource, FhirResourceFormat.Json, isMetaSet: isRawResourceMetaSet),
                                                new ResourceRequest(requestMethod),
                                                resourceSurrogateId.ToLastUpdated(),
                                                isDeleted,
                                                null,
                                                null,
                                                null,
                                                searchParameterHash,
                                                resourceSurrogateId),
                                            SearchEntryMode.Include));
                                    }

                                    // as long as at least one entry was marked as partial, this resultset
                                    // should be marked as partial
                                    isResultPartial = isResultPartial || isPartialEntry;
                                }

                                if (!clonedSearchOptions.IncludesOperationSupported && includedResources.Count > clonedSearchOptions.IncludeCount)
                                {
                                    includedResources.RemoveRange(
                                        clonedSearchOptions.IncludeCount,
                                        includedResources.Count - clonedSearchOptions.IncludeCount);
                                    isResultPartial = true;
                                }

                                // call NextResultAsync to get the info messages
                                await reader.NextResultAsync(cancellationToken);

                                ContinuationToken continuationToken = moreResults
                                        ? new ContinuationToken(new object[] { sortValue, newContinuationType, newContinuationId })
                                        : null;

                                string includesContinuationTokenString = null;
                                if (clonedSearchOptions.IncludesOperationSupported
                                    && clonedSearchOptions.QueryParams.Any(kvp =>
                                        kvp.Key.StartsWith("_include", StringComparison.OrdinalIgnoreCase)
                                        || kvp.Key.StartsWith("_revinclude", StringComparison.OrdinalIgnoreCase))
                                    && newContinuationType.HasValue
                                    && newContinuationId.HasValue
                                    && matchedResourceSurrogateIdStart.HasValue
                                    && (isResultPartial || includedResources.Count > clonedSearchOptions.IncludeCount)
                                    && !clonedSearchOptions.ContainsIterativeInclude)
                                {
                                    clonedSearchOptions.IncludesContinuationToken = new IncludesContinuationToken(
                                        new object[]
                                        {
                                            newContinuationType.Value,
                                            matchedResourceSurrogateIdStart.Value,
                                            newContinuationId.Value,
                                            null,
                                            null,
                                            sqlSearchOptions.SortQuerySecondPhase,
                                        }).ToJson();

                                    var includesSearchResult = await SearchIncludeImpl(clonedSearchOptions, reuseQueryPlans, cancellationToken);
                                    includedResources.Clear();
                                    includedResources.AddRange(includesSearchResult.Results);
                                    includesContinuationTokenString = includesSearchResult.IncludesContinuationToken;
                                    isResultPartial = !string.IsNullOrEmpty(includesSearchResult.IncludesContinuationToken);
                                }

                                if (isResultPartial)
                                {
                                    _logger.LogWarning("Bundle Partial Result (TruncatedIncludeMessage)");
                                    _requestContextAccessor.RequestContext.BundleIssues.Add(
                                        new OperationOutcomeIssue(
                                            OperationOutcomeConstants.IssueSeverity.Warning,
                                            OperationOutcomeConstants.IssueType.Incomplete,
                                            clonedSearchOptions.IncludesOperationSupported ? (clonedSearchOptions.ContainsIterativeInclude ? Core.Resources.TruncatedIncludeMessageForIterativeInclude : Core.Resources.TruncatedIncludeMessageForIncludes) : Core.Resources.TruncatedIncludeMessage));
                                }

                                // If this is a sort query, lets keep track of whether we actually searched for sort values.
                                if (clonedSearchOptions.Sort != null &&
                                    clonedSearchOptions.Sort.Count > 0 &&
                                    clonedSearchOptions.Sort[0].searchParameterInfo.Code != KnownQueryParameterNames.LastUpdated)
                                {
                                    // If there is an extra column for sort value, we know we have searched for sort values. If no results were returned, we don't know if we have searched for sort values so we need to assume we did so we run the second phase.
                                    sqlSearchOptions.DidWeSearchForSortValue = isSortValueNeeded;
                                }

                                _logger.LogInformation("Continuation token is {ContinuationTokenPresent}returned. {MaxSurrogateId}", continuationToken != null ? string.Empty : "not ", newContinuationId);
                                _logger.LogInformation("Includes continuation token is {ContinuationTokenPresent}returned", includesContinuationTokenString != null ? string.Empty : "not ");

                                searchResult = new SearchResult(matchedResources.Concat(includedResources).ToList(), continuationToken?.ToJson(), originalSort, clonedSearchOptions.UnsupportedSearchParams, null, includesContinuationTokenString);
                            }
                        }
                        catch (SqlException e)
                        {
                            var id = Guid.NewGuid().ToString();
                            await _sqlRetryService.TryLogEvent($"Search-{id}", "Error", sqlCommand.CommandText, st, cancellationToken);
                            await _sqlRetryService.TryLogEvent($"Search-{id}", "Error", e.ToString(), st, cancellationToken);
                            throw;
                        }
                        finally
                        {
                            executionStopwatch.Stop();

                            if (executionStopwatch.ElapsedMilliseconds > _longRunningThreshold.GetValue(_sqlRetryService) && _longRunningQueryDetails.IsEnabled(_sqlRetryService))
                            {
                                // Capture query text and command type BEFORE the connection closes
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
                true); // this enables reads from replicas

            _logger.LogInformation("Search completed in {ElapsedMilliseconds}ms, query cache enabled: {QueryCacheEnabled}.", stopwatch.ElapsedMilliseconds, reuseQueryPlans);
            return searchResult;
        }

        private void ReadWrapper(
            SqlDataReader reader,
            bool readIsHistory,
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
            out bool isHistory)
        {
            resourceTypeId = reader.Read(VLatest.Resource.ResourceTypeId, 0);
            resourceId = reader.Read(VLatest.Resource.ResourceId, 1);
            version = reader.Read(VLatest.Resource.Version, 2);
            isDeleted = reader.Read(VLatest.Resource.IsDeleted, 3);
            resourceSurrogateId = reader.Read(VLatest.Resource.ResourceSurrogateId, 4);
            requestMethod = reader.Read(VLatest.Resource.RequestMethod, 5);
            isMatch = reader.Read(_isMatch, 6);
            isPartialEntry = reader.Read(_isPartial, 7);
            isRawResourceMetaSet = reader.Read(VLatest.Resource.IsRawResourceMetaSet, 8);
            searchParameterHash = reader.Read(VLatest.Resource.SearchParamHash, 9);
            rawResourceBytes = reader.GetSqlBytes(10).Value;
            isInvisible = rawResourceBytes.Length == 1 && rawResourceBytes[0] == 0xF;
            isHistory = readIsHistory && reader.FieldCount > 11 ? reader.Read(VLatest.Resource.IsHistory, 11) : false;
        }

        [Conditional("DEBUG")]
        private void EnableTimeAndIoMessageLogging(IndentedStringBuilder stringBuilder, SqlConnection sqlConnection)
        {
            stringBuilder.AppendLine("SET STATISTICS IO ON;");
            stringBuilder.AppendLine("SET STATISTICS TIME ON;");
            stringBuilder.AppendLine();
            sqlConnection.InfoMessage += (sender, args) => _logger.LogInformation("SQL message: {Message}", args.Message);
        }

        /// <summary>
        /// Logs the parameter declarations and command text of a SQL command
        /// </summary>
        [Conditional("DEBUG")]
        private void LogSqlCommand(SqlCommand sqlCommand)
        {
            // TODO: when SqlCommandWrapper is fully deprecated everywhere, modify LogSqlCommand to accept sqlCommand.
            using SqlCommandWrapper sqlCommandWrapper = new SqlCommandWrapper(sqlCommand);
            var sb = new StringBuilder();
            if (sqlCommandWrapper.CommandType == CommandType.Text)
            {
                foreach (SqlParameter p in sqlCommandWrapper.Parameters)
                {
                    sb.Append("DECLARE ")
                        .Append(p)
                        .Append(' ')
                        .Append(p.SqlDbType.ToString().ToLowerInvariant())
                        .Append(p.Value is string ? (p.Size <= 0 ? "(max)" : $"({p.Size})") : p.Value is decimal ? $"({p.Precision},{p.Scale})" : null)
                        .Append(" = ")
                        .Append(p.SqlDbType == SqlDbType.NChar || p.SqlDbType == SqlDbType.NText || p.SqlDbType == SqlDbType.NVarChar ? "N" : null)
                        .AppendLine(p.Value is string || p.Value is DateTime ? $"'{p.Value:O}'" : (p.Value == null ? "NULL" : p.Value.ToString()));
                }

                sb.AppendLine();
                sb.AppendLine(sqlCommandWrapper.CommandText);

                // this just assures that the call to this fn has occurred after the CommandText is set
                Debug.Assert(sqlCommandWrapper.CommandText.Length > 0);
            }
            else
            {
                sb.Append(sqlCommandWrapper.CommandText + string.Empty);
                foreach (SqlParameter p in sqlCommandWrapper.Parameters)
                {
                    sb.Append(p.Value is string || p.Value is DateTime ? $"'{p.Value:O}'" : (p.Value == null ? "NULL" : $"'{p.Value}'"));
                    if (!(sqlCommandWrapper.Parameters.IndexOf(p) == sqlCommandWrapper.Parameters.Count - 1))
                    {
                        sb.Append(", ");
                    }
                }

                sb.AppendLine();
            }

            sb.AppendLine("OPTION (RECOMPILE)"); // enables query compilation with provided parameter values in debugging
            sb.AppendLine($"-- execution timeout = {sqlCommandWrapper.CommandTimeout} sec.");
            _sqlRetryService.TryLogEvent("Search", "Start", sb.ToString(), null, CancellationToken.None);
            _logger.LogInformation("{SqlQuery}", sb.ToString());
        }
    }
}
