// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Parameters;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.Merge;
using Microsoft.Health.Fhir.SqlServer.Registration;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Storage;
using SortOrder = Microsoft.Health.Fhir.Core.Features.Search.SortOrder;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    /// <summary>
    /// SQL Server search service implementation.
    /// </summary>
    internal partial class SqlServerSearchService : SearchService
    {
        internal const string ReuseQueryPlansParameterId = "Search.ReuseQueryPlans.IsEnabled";

        /// <summary>
        /// Feature flag that gates creation of the 3-column reference-type filtered statistic on the
        /// ReferenceSearchParam table (the stat that additionally filters on ReferenceResourceTypeId,
        /// i.e. <c>WHERE ResourceTypeId = .. AND SearchParamId = .. AND ReferenceResourceTypeId = ..</c>).
        /// Off by default: unless a row exists in the Parameters table with this Id set to an enabled
        /// value, these stats are not created and reference searches fall back to the broader 2-column
        /// filtered statistic (ResourceTypeId + SearchParamId).
        /// </summary>
        internal const string ReferenceResourceTypeFilteredStatsParameterId = "Search.ReferenceResourceTypeFilteredStats.IsEnabled";
        private const string SortValueColumnName = "SortValue";

        private readonly ISqlServerFhirModel _model;
        private readonly CompartmentSearchRewriter _compartmentSearchRewriter;
        private readonly SmartCompartmentSearchRewriter _smartCompartmentSearchRewriter;
        private readonly ILogger<SqlServerSearchService> _logger;
        private readonly BitColumn _isMatch = new BitColumn("IsMatch");
        private readonly BitColumn _isPartial = new BitColumn("IsPartial");
        private readonly ISqlRetryService _sqlRetryService;
        private readonly SqlServerDataStoreConfiguration _sqlServerDataStoreConfiguration;
        private readonly FhirSqlServerConfiguration _fhirSqlServerConfiguration;
        private readonly SchemaInformation _schemaInformation;
        private readonly ICompressedRawResourceConverter _compressedRawResourceConverter;
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly ISqlQueryHashCalculator _queryHashCalculator;
        private readonly IFhirDataStore _fhirDataStore;
        private readonly IQueryPlanReuseChecker _queryPlanReuseChecker;
        private readonly SearchParameterSqlParser _searchParameterSqlParser;

        private static object _locker = new object();

        private static CachedParameter<SqlServerSearchService> _referenceResourceTypeFilteredStats;

        public SqlServerSearchService(
            ISearchOptionsFactory searchOptionsFactory,
            IFhirDataStore fhirDataStore,
            ISqlServerFhirModel model,
            CompartmentSearchRewriter compartmentSearchRewriter,
            SmartCompartmentSearchRewriter smartCompartmentSearchRewriter,
            ISqlRetryService sqlRetryService,
            IOptions<SqlServerDataStoreConfiguration> sqlServerDataStoreConfiguration,
            FhirSqlServerConfiguration fhirSqlServerConfiguration,
            SchemaInformation schemaInformation,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            ICompressedRawResourceConverter compressedRawResourceConverter,
            ISqlQueryHashCalculator queryHashCalculator,
            IQueryPlanReuseChecker queryPlanReuseChecker,
            SearchParameterSqlParser searchParameterSqlParser,
            ILogger<SqlServerSearchService> logger)
            : base(searchOptionsFactory, fhirDataStore, logger)
        {
            EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(compartmentSearchRewriter, nameof(compartmentSearchRewriter));
            EnsureArg.IsNotNull(smartCompartmentSearchRewriter, nameof(smartCompartmentSearchRewriter));
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            EnsureArg.IsNotNull(queryPlanReuseChecker, nameof(queryPlanReuseChecker));
            EnsureArg.IsNotNull(searchParameterSqlParser, nameof(searchParameterSqlParser));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlServerDataStoreConfiguration = EnsureArg.IsNotNull(sqlServerDataStoreConfiguration?.Value, nameof(sqlServerDataStoreConfiguration));
            _fhirSqlServerConfiguration = EnsureArg.IsNotNull(fhirSqlServerConfiguration, nameof(fhirSqlServerConfiguration));
            _fhirDataStore = fhirDataStore;
            _model = model;
            _compartmentSearchRewriter = compartmentSearchRewriter;
            _smartCompartmentSearchRewriter = smartCompartmentSearchRewriter;
            _sqlRetryService = sqlRetryService;
            _queryHashCalculator = queryHashCalculator;
            _queryPlanReuseChecker = queryPlanReuseChecker;
            _searchParameterSqlParser = searchParameterSqlParser;
            _logger = logger;

            _schemaInformation = schemaInformation;
            _requestContextAccessor = requestContextAccessor;
            _compressedRawResourceConverter = compressedRawResourceConverter;

            InitializeProcessingFlags(logger);
        }

        internal bool StoredProcedureLayerIsEnabled { get; set; } = true;

        internal ISqlServerFhirModel Model => _model;

        private static void InitializeProcessingFlags(ILogger<SqlServerSearchService> logger)
        {
            lock (_locker)
            {
                if (_longRunningQueryDetails == null)
                {
                    _longRunningQueryDetails = new CachedParameter<SqlServerSearchService>(LongRunningQueryDetailsParameterId, 1, logger);
                }

                if (_longRunningThreshold == null)
                {
                    _longRunningThreshold = new CachedParameter<SqlServerSearchService>(LongRunningQueryDetailsThresholdId, LongRunningThresholdMillisecondsDefault, logger);
                }

                if (_referenceResourceTypeFilteredStats == null)
                {
                    // Default 0 (disabled): the 3-column reference-type filtered stat is only created
                    // when an operator adds a row to the Parameters table with this Id enabled.
                    _referenceResourceTypeFilteredStats = new CachedParameter<SqlServerSearchService>(ReferenceResourceTypeFilteredStatsParameterId, 0, logger);
                }
            }
        }

        public override async Task<SearchResult> SearchAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            SqlSearchOptions sqlSearchOptions = new SqlSearchOptions(searchOptions);

            if (sqlSearchOptions.IsIncludesOperation)
            {
                var includesContinuationToken = IncludesContinuationToken.FromString(sqlSearchOptions.IncludesContinuationToken);
                if (includesContinuationToken == null)
                {
                    _logger.LogWarning("Bad Request (InvalidIncludesContinuationToken)");
                    throw new BadRequestException(Resources.InvalidIncludesContinuationToken);
                }

                sqlSearchOptions.SortQuerySecondPhase = includesContinuationToken.SortQuerySecondPhase ?? false;

                SearchResult includesSearchResult = await RunSearch(sqlSearchOptions, cancellationToken);

                if (includesSearchResult.Results.Count() < sqlSearchOptions.IncludeCount
                    && includesSearchResult.IncludesContinuationToken == null
                    && includesContinuationToken.SecondPhaseContinuationToken != null)
                {
                    // Continue to second phase of includes
                    sqlSearchOptions.IncludesContinuationToken = includesContinuationToken.SecondPhaseContinuationToken.ToJson();
                    sqlSearchOptions.IncludeCount = sqlSearchOptions.IncludeCount - includesSearchResult.Results.Count();
                    sqlSearchOptions.SortQuerySecondPhase = true;

                    var secondPhaseIncludesSearchResult = await RunSearch(sqlSearchOptions, cancellationToken);

                    var finalResults = new List<SearchResultEntry>();
                    finalResults.AddRange(includesSearchResult.Results);
                    finalResults.AddRange(secondPhaseIncludesSearchResult.Results);
                    includesSearchResult = new SearchResult(
                        finalResults,
                        secondPhaseIncludesSearchResult.ContinuationToken,
                        secondPhaseIncludesSearchResult.SortOrder,
                        secondPhaseIncludesSearchResult.UnsupportedSearchParameters,
                        includesContinuationToken: secondPhaseIncludesSearchResult.IncludesContinuationToken);
                }
                else if (includesSearchResult.Results.Count() >= sqlSearchOptions.IncludeCount
                    && includesSearchResult.IncludesContinuationToken != null
                    && includesContinuationToken.SecondPhaseContinuationToken != null)
                {
                    // We have reached the requested include count but there are more includes to be fetched.
                    // We need to preserve the second phase continuation token.
                    var newIncludesContinuationToken = IncludesContinuationToken.FromString(includesSearchResult.IncludesContinuationToken);

                    var combinedIncludesContinuationToken = new IncludesContinuationToken(
                        new object[]
                        {
                            newIncludesContinuationToken.MatchResourceTypeId,
                            newIncludesContinuationToken.MatchResourceSurrogateIdMin,
                            newIncludesContinuationToken.MatchResourceSurrogateIdMax,
                            newIncludesContinuationToken.IncludeResourceTypeId,
                            newIncludesContinuationToken.IncludeResourceSurrogateId,
                            includesContinuationToken.SortQuerySecondPhase,
                            includesContinuationToken.SecondPhaseContinuationToken,
                        }).ToJson();
                    includesSearchResult = new SearchResult(
                        includesSearchResult.Results,
                        includesSearchResult.ContinuationToken,
                        includesSearchResult.SortOrder,
                        includesSearchResult.UnsupportedSearchParameters,
                        includesContinuationToken: combinedIncludesContinuationToken);
                }
                else if (includesSearchResult.Results.Count() >= sqlSearchOptions.IncludeCount
                    && includesSearchResult.IncludesContinuationToken == null
                    && includesContinuationToken.SecondPhaseContinuationToken != null)
                {
                    // We have reached the requested include count and there are no more includes to be fetched.
                    // So the next page of results is from the second phase continuation token.

                    includesSearchResult = new SearchResult(
                        includesSearchResult.Results,
                        includesSearchResult.ContinuationToken,
                        includesSearchResult.SortOrder,
                        includesSearchResult.UnsupportedSearchParameters,
                        includesContinuationToken: includesContinuationToken.SecondPhaseContinuationToken.ToJson());
                }

                return includesSearchResult;
            }

            SearchResult searchResult = await RunSearch(sqlSearchOptions, cancellationToken);
            int resultCount = searchResult.Results.Count(r => r.SearchEntryMode == SearchEntryMode.Match);

            if (!sqlSearchOptions.IsSortWithFilter &&
                !sqlSearchOptions.SortHasMissingModifier &&
                !sqlSearchOptions.SortQuerySecondPhase &&
                searchResult.ContinuationToken == null &&
                resultCount <= sqlSearchOptions.MaxItemCount &&
                sqlSearchOptions.Sort != null &&
                sqlSearchOptions.Sort.Count > 0 &&
                sqlSearchOptions.Sort[0].searchParameterInfo.Code != KnownQueryParameterNames.LastUpdated)
            {
                // We seem to have run a sort which has returned less results than what max we can return.
                // Let's determine whether we need to execute another query or not.
                if ((sqlSearchOptions.Sort[0].sortOrder == SortOrder.Ascending)
                    || (sqlSearchOptions.Sort[0].sortOrder == SortOrder.Descending)
                    || (sqlSearchOptions.Sort[0].sortOrder == SortOrder.Descending && resultCount == 0 && !sqlSearchOptions.CountOnly))
                {
                    if (sqlSearchOptions.MaxItemCount - resultCount == 0)
                    {
                        // Check if more resources to be retrieved.
                        sqlSearchOptions.SortQuerySecondPhase = true;
                        sqlSearchOptions.MaxItemCount = 1;
                        var secondSearchResult = await RunSearch(sqlSearchOptions, cancellationToken);

                        // Since we are already returning MaxItemCount number of resources we don't want
                        // to execute another search right now just to drop all the resources. We will return
                        // a "special" ct so that we the subsequent request will be handled correctly.
                        var ct = (secondSearchResult.Results?.Any() ?? false) ? new ContinuationToken(new object[]
                            {
                                SqlSearchConstants.SortSentinelValueForCt,
                                0,
                            })
                            : null;

                        searchResult = new SearchResult(
                            searchResult.Results,
                            ct?.ToJson(),
                            searchResult.SortOrder,
                            searchResult.UnsupportedSearchParameters,
                            includesContinuationToken: searchResult.IncludesContinuationToken);
                    }
                    else
                    {
                        var finalResultsInOrder = new List<SearchResultEntry>();
                        finalResultsInOrder.AddRange(searchResult.Results);
                        sqlSearchOptions.SortQuerySecondPhase = true;
                        sqlSearchOptions.MaxItemCount -= resultCount;

                        var includesCount = searchResult.Results.Count(r => r.SearchEntryMode == SearchEntryMode.Include);
                        if (includesCount < sqlSearchOptions.IncludeCount)
                        {
                            sqlSearchOptions.IncludeCount -= includesCount;
                        }
                        else
                        {
                            sqlSearchOptions.IncludeContinuationTokenSearch = true;
                            sqlSearchOptions.IncludeCount = 0;
                        }

                        var secondSearchResult = await RunSearch(sqlSearchOptions, cancellationToken);

                        finalResultsInOrder.AddRange(secondSearchResult.Results);

                        var includesContinuationToken = searchResult.IncludesContinuationToken;

                        // If phase 2 didn't produce an includes continuation token but we know
                        // there are includes to fetch (IncludeContinuationTokenSearch was set because
                        // phase 1 exhausted the include budget), create one from phase 2's matched results.
                        var secondPhaseIncludesCt = secondSearchResult.IncludesContinuationToken;
                        if (secondPhaseIncludesCt == null
                            && sqlSearchOptions.IncludeContinuationTokenSearch
                            && sqlSearchOptions.IncludesOperationSupported
                            && secondSearchResult.Results.Any(r => r.SearchEntryMode == SearchEntryMode.Match))
                        {
                            var phase2Matches = secondSearchResult.Results
                                .Where(r => r.SearchEntryMode == SearchEntryMode.Match)
                                .ToList();
                            var firstMatch = phase2Matches.First().Resource;
                            var lastMatch = phase2Matches.Last().Resource;
                            var resourceTypeId = _model.GetResourceTypeId(firstMatch.ResourceTypeName);

                            secondPhaseIncludesCt = new IncludesContinuationToken(new object[]
                            {
                                resourceTypeId,
                                firstMatch.ResourceSurrogateId,
                                lastMatch.ResourceSurrogateId,
                                null,
                                null,
                                true, // SortQuerySecondPhase
                            }).ToJson();
                        }

                        if (secondPhaseIncludesCt != null)
                        {
                            if (includesContinuationToken == null)
                            {
                                includesContinuationToken = secondPhaseIncludesCt;
                            }
                            else
                            {
                                var firstToken = IncludesContinuationToken.FromString(includesContinuationToken);
                                var secondToken = IncludesContinuationToken.FromString(secondPhaseIncludesCt);
                                includesContinuationToken = new IncludesContinuationToken(new object[]
                                {
                                    firstToken.MatchResourceTypeId,
                                    firstToken.MatchResourceSurrogateIdMin,
                                    firstToken.MatchResourceSurrogateIdMax,
                                    firstToken.IncludeResourceTypeId,
                                    firstToken.IncludeResourceSurrogateId,
                                    false,
                                    secondToken,
                                }).ToJson();
                            }
                        }

                        searchResult = new SearchResult(
                            finalResultsInOrder,
                            secondSearchResult.ContinuationToken,
                            secondSearchResult.SortOrder,
                            secondSearchResult.UnsupportedSearchParameters,
                            includesContinuationToken: includesContinuationToken);
                    }
                }
            }

            // If we should include the total count of matching search results
            if (sqlSearchOptions.IncludeTotal == TotalType.Accurate && !sqlSearchOptions.CountOnly)
            {
                // If this is the first page and there aren't any more pages
                if (sqlSearchOptions.ContinuationToken == null && searchResult.ContinuationToken == null)
                {
                    // Count the match results on the page.
                    searchResult.TotalCount = searchResult.Results.Count(r => r.SearchEntryMode == SearchEntryMode.Match);
                }
                else
                {
                    try
                    {
                        // Otherwise, indicate that we'd like to get the count
                        sqlSearchOptions.CountOnly = true;

                        // And perform a second read.
                        var countOnlySearchResult = await RunSearch(sqlSearchOptions, cancellationToken);

                        searchResult.TotalCount = countOnlySearchResult.TotalCount;
                    }
                    finally
                    {
                        // Ensure search options is set to its original state.
                        sqlSearchOptions.CountOnly = false;
                    }
                }
            }

            return searchResult;
        }

        public override bool IsValidResourceType(string resourceType)
        {
            return _model.TryGetResourceTypeId(resourceType, out _);
        }

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
                        }/*
                        else if (TryExtractGetResourcesByTokensParams(expression, clonedSearchOptions, (SqlServerFhirModel)_model, out var resourceTypeId, out var searchParamId, out var tokens, out var top))
                        {
                            PopulateGetResourcesByTokensCommand(sqlCommand, resourceTypeId, searchParamId, tokens, top);
                        }*/
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

        private static bool ContainsStartSurrogateId(SqlSearchOptions options)
        {
            IReadOnlyList<(string Param, string Value)> hints = options.QueryHints;
            return hints.Any(x => string.Equals(KnownQueryParameterNames.StartSurrogateId, x.Param, StringComparison.OrdinalIgnoreCase));
        }

        private void PopulateSqlCommandFromQueryHints(SqlSearchOptions options, SqlCommand command)
        {
            IReadOnlyList<(string Param, string Value)> hints = options.QueryHints;

            var resourceTypeId = _model.GetResourceTypeId(hints.First(x => x.Param == KnownQueryParameterNames.Type).Value);
            var startId = long.Parse(hints.First(x => x.Param == KnownQueryParameterNames.StartSurrogateId).Value);
            var endId = long.Parse(hints.First(x => x.Param == KnownQueryParameterNames.EndSurrogateId).Value);
            var globalStr = hints.FirstOrDefault(x => x.Param == KnownQueryParameterNames.GlobalEndSurrogateId).Value;
            var globalEndId = string.IsNullOrEmpty(globalStr) ? null : (long?)long.Parse(globalStr);

            PopulateSqlCommandFromQueryHints(command, resourceTypeId, startId, endId, globalEndId, options.ResourceVersionTypes.HasFlag(ResourceVersionType.History), options.ResourceVersionTypes.HasFlag(ResourceVersionType.SoftDeleted));
        }

        private static void PopulateSqlCommandFromQueryHints(SqlCommand command, short resourceTypeId, long startId, long endId, long? globalEndId, bool? includeHistory, bool? includeDeleted)
        {
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "dbo.GetResourcesByTypeAndSurrogateIdRange";
            command.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            command.Parameters.AddWithValue("@StartId", startId);
            command.Parameters.AddWithValue("@EndId", endId);
            if (globalEndId.HasValue)
            {
                command.Parameters.AddWithValue("@GlobalEndId", globalEndId.Value);
            }

            command.Parameters.AddWithValue("@IncludeHistory", includeHistory);
            command.Parameters.AddWithValue("@IncludeDeleted", includeDeleted);
        }

        public override Task<SearchResult> SearchBySurrogateIdRange(string resourceType, long startId, long endId, CancellationToken cancellationToken)
        {
            return SearchBySurrogateIdRange(resourceType, startId, endId, null, null, cancellationToken, false, false);
        }

        /// <summary>
        /// Searches for resources by their type and surrogate id and optionally a searchParamHash and will return resources
        /// </summary>
        /// <param name="resourceType">The resource type to search</param>
        /// <param name="startId">The lower bound for surrogate ids to find</param>
        /// <param name="endId">The upper bound for surrogate ids to find</param>
        /// <param name="windowStartId">The lower bound for the window of time to consider for historical records</param>
        /// <param name="windowEndId">The upper bound for the window of time to consider for historical records</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="includeHistory">Return historical records that match the other parameters.</param>
        /// <param name="includeDeleted">Return deleted records that match the other parameters.</param>
        /// <returns>All resources with surrogate ids greater than or equal to startId and less than or equal to endId. If windowEndId is set it will return the most recent version of a resource that was created before windowEndId that is within the range of startId to endId.</returns>
        public async Task<SearchResult> SearchBySurrogateIdRange(string resourceType, long startId, long endId, long? windowStartId, long? windowEndId, CancellationToken cancellationToken, bool includeHistory = false, bool includeDeleted = false)
        {
            var resourceTypeId = _model.GetResourceTypeId(resourceType);
            using var sqlCommand = new SqlCommand();
            sqlCommand.CommandTimeout = GetSurrogateIdRangeCommandTimeout();
            PopulateSqlCommandFromQueryHints(sqlCommand, resourceTypeId, startId, endId, windowEndId, includeHistory, includeDeleted);
            LogSqlCommand(sqlCommand);
            List<SearchResultEntry> resources = null;
            await _sqlRetryService.ExecuteSql(
                sqlCommand,
                async (cmd, cancel) =>
                {
                    using SqlDataReader reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancel);
                    resources = new List<SearchResultEntry>();
                    while (await reader.ReadAsync(cancel))
                    {
                        ReadWrapper(
                            reader,
                            true,
                            out short _,
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

                        using var rawResourceStream = new MemoryStream(rawResourceBytes);
                        var rawResource = _compressedRawResourceConverter.ReadCompressedRawResource(rawResourceStream);

                        if (string.IsNullOrEmpty(rawResource))
                        {
                            rawResource = MissingResourceFactory.CreateJson(resourceId, _model.GetResourceTypeName(resourceTypeId), "warning", "incomplete");
                            _requestContextAccessor.SetMissingResourceCode(System.Net.HttpStatusCode.PartialContent);
                        }

                        resources.Add(new SearchResultEntry(
                            new ResourceWrapper(
                                resourceId,
                                version.ToString(CultureInfo.InvariantCulture),
                                resourceType,
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
                            isMatch ? SearchEntryMode.Match : SearchEntryMode.Include));
                    }

                    return;
                },
                _logger,
                null,
                cancellationToken);
            return new SearchResult(resources, null, null, new List<Tuple<string, string>>()) { TotalCount = resources.Count };
        }

        private static (long StartId, long EndId, int Count) ReaderToSurrogateIdRange(SqlDataReader sqlDataReader)
        {
            return (sqlDataReader.GetInt64(1), sqlDataReader.GetInt64(2), sqlDataReader.GetInt32(3));
        }

        public override async Task<IReadOnlyList<(long StartId, long EndId, int Count)>> GetSurrogateIdRanges(string resourceType, long startId, long endId, int rangeSize, int numberOfRanges, bool up, CancellationToken cancellationToken, bool activeOnly = false)
        {
            var resourceTypeId = _model.GetResourceTypeId(resourceType);
            using var sqlCommand = new SqlCommand();
            PopulateGetResourceSurrogateIdRangesCommand(sqlCommand, resourceTypeId, startId, endId, rangeSize, numberOfRanges, up, activeOnly);
            sqlCommand.CommandTimeout = GetSurrogateIdRangeCommandTimeout();
            LogSqlCommand(sqlCommand);
            return await sqlCommand.ExecuteReaderAsync(_sqlRetryService, ReaderToSurrogateIdRange, _logger, cancellationToken);
        }

        private static string ReaderGetUsedResourceTypes(SqlDataReader sqlDataReader)
        {
            return sqlDataReader.GetString(1);
        }

        public override async Task<IReadOnlyList<string>> GetUsedResourceTypes(CancellationToken cancellationToken)
        {
            using var sqlCommand = new SqlCommand("dbo.GetUsedResourceTypes") { CommandType = CommandType.StoredProcedure };
            LogSqlCommand(sqlCommand);
            return await sqlCommand.ExecuteReaderAsync(_sqlRetryService, ReaderGetUsedResourceTypes, _logger, cancellationToken);
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

        /// <summary>
        /// Returns the column names used for filtered statistics on the given search parameter table.
        /// Only tables that benefit from per-resource-type filtered statistics are included.
        /// </summary>
        /// <param name="table">The fully-qualified table name (e.g. <c>dbo.TokenSearchParam</c>).</param>
        /// <returns>The set of column names, or an empty set when the table does not support filtered stats.</returns>
        internal static HashSet<string> GetKeyColumns(string table)
        {
            var results = new HashSet<string>();
            if (table == VLatest.StringSearchParam.TableName)
            {
                results.Add(VLatest.StringSearchParam.Text.Metadata.Name);
            }
            else if (table == VLatest.TokenSearchParam.TableName)
            {
                results.Add(VLatest.TokenSearchParam.Code.Metadata.Name);
            }
            else if (table == VLatest.DateTimeSearchParam.TableName)
            {
                results.Add(VLatest.DateTimeSearchParam.StartDateTime.Metadata.Name);
                results.Add(VLatest.DateTimeSearchParam.EndDateTime.Metadata.Name);
            }
            else if (table == VLatest.NumberSearchParam.TableName)
            {
                results.Add(VLatest.NumberSearchParam.LowValue.Metadata.Name);
                results.Add(VLatest.NumberSearchParam.HighValue.Metadata.Name);
            }
            else if (table == VLatest.QuantitySearchParam.TableName)
            {
                results.Add(VLatest.QuantitySearchParam.LowValue.Metadata.Name);
                results.Add(VLatest.QuantitySearchParam.HighValue.Metadata.Name);
            }
            else if (table == VLatest.ReferenceSearchParam.TableName)
            {
                results.Add(VLatest.ReferenceSearchParam.ReferenceResourceId.Metadata.Name);
            }

            return results;
        }

        private int GetSurrogateIdRangeCommandTimeout()
        {
            return Math.Max((int)_sqlServerDataStoreConfiguration.CommandTimeout.TotalSeconds, 1200);
        }

        /*
        private async Task CreateStats(SqlRootExpression expression, CancellationToken cancel)
        {
            if (_resourceSearchParamStats == null)
            {
                lock (_locker)
                {
                    _resourceSearchParamStats ??= new ResourceSearchParamStats(_sqlRetryService, _logger, cancel);
                }
            }

            await _resourceSearchParamStats.Create(expression, _sqlRetryService, _logger, (SqlServerFhirModel)_model, cancel);
        }
        */

        internal static ICollection<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId)> GetStatsFromCache()
        {
            return Array.Empty<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId)>();
        }

        /// <summary>
        /// Forces the next read of the reference-resource-type filtered statistics feature flag to bypass the
        /// in-memory cache and re-query the Parameters table. Intended for integration tests that toggle the flag.
        /// </summary>
        internal static void ResetReferenceResourceTypeFilteredStatsCache()
        {
            _referenceResourceTypeFilteredStats?.Reset();
        }

        internal async Task<IReadOnlyList<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId)>> GetStatsFromDatabase(CancellationToken cancel)
        {
            return await GetStatsFromDatabase(_sqlRetryService, _logger, cancel);
        }

        private static async Task<IReadOnlyList<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId)>> GetStatsFromDatabase(ISqlRetryService sqlRetryService, ILogger<SqlServerSearchService> logger, CancellationToken cancel)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.GetResourceSearchParamStats", CommandType = CommandType.StoredProcedure };
            return await cmd.ExecuteReaderAsync(
                            sqlRetryService,
                            (reader) =>
                            {
                                // ST_Code_WHERE_ResourceTypeId_28_SearchParamId_202
                                // ST_ReferenceResourceId_WHERE_ResourceTypeId_40_SearchParamId_1219_ReferenceResourceTypeId_2
                                var table = reader.GetString(0);
                                var stats = reader.GetString(1);
                                var split = stats.Split("_");
                                var column = split[1];
                                var resourceTypeId = short.Parse(split[4]);
                                var searchParamId = short.Parse(split[6]);
                                short? referenceResourceTypeId = split.Length > 8 ? short.Parse(split[8]) : null;
                                return ("dbo." + table, column, resourceTypeId, searchParamId, referenceResourceTypeId);
                            },
                            logger,
                            cancel);
        }

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

        private static void PopulateGetResourceSurrogateIdRangesCommand(SqlCommand cmd, short resourceTypeId, long startId, long endId, int rangeSize, int? numberOfRanges, bool up, bool activeOnly)
        {
            cmd.CommandText = "dbo.GetResourceSurrogateIdRanges";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            cmd.Parameters.AddWithValue("@StartId", startId);
            cmd.Parameters.AddWithValue("@EndId", endId);
            cmd.Parameters.AddWithValue("@RangeSize", rangeSize);
            cmd.Parameters.AddWithValue("@NumberOfRanges", numberOfRanges);
            cmd.Parameters.AddWithValue("@Up", up);
            cmd.Parameters.AddWithValue("@ActiveOnly", activeOnly);
        }

        internal class ResourceSearchParamStats
        {
            private readonly ConcurrentDictionary<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId), bool> _stats;

            public ResourceSearchParamStats(
                ISqlRetryService sqlRetryService,
                ILogger<SqlServerSearchService> logger,
                CancellationToken cancel)
            {
                _stats = new ConcurrentDictionary<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId), bool>();
                Init(sqlRetryService, logger, cancel).Wait(cancel);
            }

            public ICollection<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId)> GetStatsFromCache()
            {
                return _stats.Keys;
            }

            /*
            public async Task Create(
                SqlRootExpression expression,
                ISqlRetryService sqlRetryService,
                ILogger<SqlServerSearchService> logger,
                SqlServerFhirModel model,
                CancellationToken cancel)
            {
                // Iterate over top-level table expressions
                for (int tableIndex = 0; tableIndex < expression.SearchParamTableExpressions.Count; tableIndex++)
                {
                    var tableExpression = expression.SearchParamTableExpressions[tableIndex];

                    // We support Normal, Union, and NotExists. Skip include/sort/etc.
                    if (tableExpression.Kind != SearchParamTableExpressionKind.Normal &&
                        tableExpression.Kind != SearchParamTableExpressionKind.Union &&
                        tableExpression.Kind != SearchParamTableExpressionKind.NotExists)
                    {
                        continue;
                    }

                    // Collected raw tuples (table, resourceTypeId, searchParamId, referenceResourceTypeId)
                    var collected = new List<(string Table, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId)>();

                    // Track whether we also need a ResourceSurrogateId filtered stat
                    bool hasResourceSurrogateId = false;

                    if (tableExpression.Kind == SearchParamTableExpressionKind.Normal)
                    {
                        ProcessPredicateForStats(tableExpression.Predicate, tableExpression.QueryGenerator, model, tableExpression.ChainLevel, expression, tableIndex, collected, logger, parentMultiaryContext: null, isUnionBranch: false);
                    }
                    else if (tableExpression.Kind == SearchParamTableExpressionKind.Union &&
                             tableExpression.Predicate is UnionExpression unionPredicate)
                    {
                        // Each union branch is its own logical context; do not cross-associate resource type constraints
                        foreach (var branch in unionPredicate.Expressions)
                        {
                            ProcessUnionBranch(branch, tableExpression.QueryGenerator, model, tableExpression.ChainLevel, expression, tableIndex, collected, logger);
                        }
                    }
                    else if (tableExpression.Kind == SearchParamTableExpressionKind.NotExists)
                    {
                        ProcessNotExistsForStats(tableExpression.Predicate, tableExpression.QueryGenerator, model, collected, out hasResourceSurrogateId);
                    }

                    // Emit stats rows
                    foreach (var (table, resourceTypeId, searchParamId, referenceResourceTypeId) in collected)
                    {
                        // For a NotExists (:missing=true) predicate that carries a ResourceSurrogateId range
                        // constraint, create an additional ResourceSurrogateId filtered stat. This is emitted
                        // independently of the value-key columns so it is still created for tables that have no
                        // value columns (e.g. ReferenceSearchParam, UriSearchParam), which is exactly where the
                        // anti-join on ResourceSurrogateId benefits most. hasResourceSurrogateId is only ever set
                        // in the NotExists branch, so this never adds stats for Normal/Union searches.
                        if (hasResourceSurrogateId)
                        {
                            await Create(table, "ResourceSurrogateId", resourceTypeId, searchParamId, null, sqlRetryService, logger, cancel);
                        }

                        var columns = GetKeyColumns(table);
                        if (columns.Count == 0)
                        {
                            continue;
                        }

                        foreach (var column in columns)
                        {
                            await Create(table, column, resourceTypeId, searchParamId, referenceResourceTypeId, sqlRetryService, logger, cancel);
                        }
                    }
                }
            }

            private void ProcessUnionBranch(
                Expression unionInner,
                SearchParamTableExpressionQueryGenerator defaultGenerator,
                SqlServerFhirModel model,
                int chainLevel,
                SqlRootExpression root,
                int tableIndex,
                List<(string Table, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId)> collected,
                ILogger logger)
            {
                // A union branch may itself be a MultiaryExpression (AND group) or a single expression
                if (unionInner is MultiaryExpression multi)
                {
                    // Treat this AND group as a distinct resource-type/search-param context
                    foreach (var child in multi.Expressions)
                    {
                        ProcessPredicateForStats(child, defaultGenerator, model, chainLevel, root, tableIndex, collected, logger, parentMultiaryContext: multi, isUnionBranch: true);
                    }
                }
                else
                {
                    ProcessPredicateForStats(unionInner, defaultGenerator, model, chainLevel, root, tableIndex, collected, logger, parentMultiaryContext: null, isUnionBranch: true);
                }
            }

            /// <summary>
            /// Processes a NotExists predicate (produced by MissingSearchParamVisitor for :missing=true queries)
            /// to extract the owning search parameter and optionally detect ResourceSurrogateId range constraints.
            /// </summary>
            private void ProcessNotExistsForStats(
                Expression predicate,
                SearchParamTableExpressionQueryGenerator defaultGenerator,
                SqlServerFhirModel model,
                List<(string Table, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId)> collected,
                out bool hasResourceSurrogateId)
            {
                hasResourceSurrogateId = false;

                var missingParams = new List<MissingSearchParameterExpression>();
                var resourceTypeIds = new HashSet<short>();
                bool foundSurrogateId = false;

                CollectNotExistsLeaves(predicate, missingParams, resourceTypeIds, model, ref foundSurrogateId);

                // Conservative: skip if predicate resolves to anything other than exactly one owning search parameter
                if (missingParams.Count != 1)
                {
                    return;
                }

                var missingParam = missingParams[0];

                // Skip synthetic parameters
                if (missingParam.Parameter.Name == SqlSearchParameters.PrimaryKeyParameterName ||
                    missingParam.Parameter.Name == SqlSearchParameters.ResourceSurrogateIdParameterName)
                {
                    return;
                }

                // Resolve the table for this parameter
                var specificGenerator = missingParam.AcceptVisitor(_queryGeneratorFactory, _queryGeneratorFactory.InitialContext) ?? defaultGenerator;
                var tableName = specificGenerator.Table.TableName;

                // Resolve search param ID
                if (!model.TryGetSearchParamId(missingParam.Parameter.Url, out var searchParamId) || searchParamId == 0)
                {
                    return;
                }

                // Require a concrete resource-type constraint that came from the predicate itself. We
                // intentionally do NOT fall back to the search parameter's BaseResourceTypes here: for a typed
                // :missing search (e.g. Observation?_tag:missing=true) the resource type is present in the
                // NotExists predicate as a sibling, so it is already captured above. An untyped cross-type
                // :missing search (e.g. ?_tag:missing=true) has no concrete type and a BaseResourceTypes
                // fallback would fan out a stat for every base resource type, so we skip it instead.
                if (resourceTypeIds.Count == 0)
                {
                    return;
                }

                hasResourceSurrogateId = foundSurrogateId;

                foreach (var rtId in resourceTypeIds)
                {
                    collected.Add((tableName, rtId, searchParamId, null));
                }
            }

            /// <summary>
            /// Recursively collects MissingSearchParameterExpression leaves, resource type constraints,
            /// and detects ResourceSurrogateId range constraints from a NotExists predicate.
            /// </summary>
            internal static void CollectNotExistsLeaves(
                Expression expression,
                List<MissingSearchParameterExpression> missingParams,
                HashSet<short> resourceTypeIds,
                SqlServerFhirModel model,
                ref bool foundSurrogateId)
            {
                switch (expression)
                {
                    case MissingSearchParameterExpression msp:
                        missingParams.Add(msp);
                        break;

                    case SearchParameterExpression spe:
                        if (spe.Parameter.Name == SearchParameterNames.ResourceType)
                        {
                            CollectResourceTypesFromExpression(spe.Expression, model, resourceTypeIds);
                        }
                        else if (spe.Parameter.Name == SqlSearchParameters.ResourceSurrogateIdParameterName)
                        {
                            foundSurrogateId = true;
                        }

                        break;

                    case MultiaryExpression multi:
                        foreach (var inner in multi.Expressions)
                        {
                            CollectNotExistsLeaves(inner, missingParams, resourceTypeIds, model, ref foundSurrogateId);
                        }

                        break;
                }
            }

            private void ProcessPredicateForStats(
                Expression predicate,
                SearchParamTableExpressionQueryGenerator defaultGenerator,
                SqlServerFhirModel model,
                int chainLevel,
                SqlRootExpression root,
                int tableIndex,
                List<(string Table, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId)> collected,
                ILogger logger,
                MultiaryExpression parentMultiaryContext,
                bool isUnionBranch)
            {
                switch (predicate)
                {
                    case SearchParameterExpression spe:
                        HandleSearchParameterExpression(spe, defaultGenerator, model, chainLevel, root, tableIndex, collected, parentMultiaryContext, isUnionBranch);
                        break;

                    case MultiaryExpression multi:
                        foreach (var inner in multi.Expressions)
                        {
                            ProcessPredicateForStats(inner, defaultGenerator, model, chainLevel, root, tableIndex, collected, logger, parentMultiaryContext: multi, isUnionBranch: isUnionBranch);
                        }

                        break;

                    case UnionExpression union:
                        foreach (var branch in union.Expressions)
                        {
                            ProcessUnionBranch(branch, defaultGenerator, model, chainLevel, root, tableIndex, collected, logger);
                        }

                        break;

                    default:
                        // Non-search-parameter leaf (e.g. compartment) � ignore for stats
                        break;
                }
            }

            private void HandleSearchParameterExpression(
                SearchParameterExpression spe,
                SearchParamTableExpressionQueryGenerator defaultGenerator,
                SqlServerFhirModel model,
                int chainLevel,
                SqlRootExpression root,
                int tableIndex,
                List<(string Table, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId)> collected,
                MultiaryExpression parentMultiaryContext,
                bool isUnionBranch)
            {
                // Ignore synthetic parameters
                if (spe.Parameter.Name == SqlSearchParameters.PrimaryKeyParameterName ||
                    spe.Parameter.Name == SqlSearchParameters.ResourceSurrogateIdParameterName)
                {
                    return;
                }

                // Determine query generator for this specific expression
                var specificGenerator = spe.AcceptVisitor(_queryGeneratorFactory, _queryGeneratorFactory.InitialContext) ?? defaultGenerator;
                var tableName = specificGenerator.Table.TableName;

                // Extract searchParamId (skip if not resolvable)
                if (!model.TryGetSearchParamId(spe.Parameter.Url, out var searchParamId) || searchParamId == 0)
                {
                    // If this is the _type search parameter, we don't create stats entries directly for it
                    return;
                }

                // Collect applicable resource types
                var resourceTypeIds = new HashSet<short>();

                // 1. If inside an AND group (Multiary) gather resource type constraints from siblings
                if (parentMultiaryContext != null)
                {
                    foreach (var siblingSpe in parentMultiaryContext.Expressions
                        .OfType<SearchParameterExpression>()
                        .Where(spe => spe.Parameter.Name == SearchParameterNames.ResourceType))
                    {
                        CollectResourceTypesFromExpression(siblingSpe.Expression, model, resourceTypeIds);
                    }
                }

                // 2. For chain level 1, derive types from predecessor chain expression
                if (resourceTypeIds.Count == 0 &&
                    chainLevel == 1 &&
                    tableIndex > 0)
                {
                    var prev = root.SearchParamTableExpressions[tableIndex - 1];
                    if (prev.Kind == SearchParamTableExpressionKind.Chain &&
                        prev.Predicate is SqlChainLinkExpression chainLink)
                    {
                        foreach (var rt in chainLink.ResourceTypes)
                        {
                            if (model.TryGetResourceTypeId(rt, out var rtId))
                            {
                                resourceTypeIds.Add(rtId);
                            }
                        }
                    }
                }

                // 3. Fall back to base resource types from SearchParameter definition
                if (resourceTypeIds.Count == 0 && spe.Parameter.BaseResourceTypes?.Count > 0)
                {
                    foreach (var baseType in spe.Parameter.BaseResourceTypes)
                    {
                        if (model.TryGetResourceTypeId(baseType, out var rtId))
                        {
                            resourceTypeIds.Add(rtId);
                        }
                    }
                }

                // Skip if still none (cannot reliably pair)
                if (resourceTypeIds.Count == 0)
                {
                    return;
                }

                // For ReferenceSearchParam, extract target reference resource types from the expression
                // to create per-target-type filtered statistics (Approach B).
                var referenceResourceTypeIds = new HashSet<short>();
                if (tableName == VLatest.ReferenceSearchParam.TableName)
                {
                    CollectReferenceResourceTypes(spe.Expression, model, referenceResourceTypeIds);
                }

                foreach (var rtId in resourceTypeIds)
                {
                    if (referenceResourceTypeIds.Count > 0)
                    {
                        // Create one entry per target type for Approach B stats
                        foreach (var refRtId in referenceResourceTypeIds)
                        {
                            collected.Add((tableName, rtId, searchParamId, refRtId));
                        }
                    }
                    else
                    {
                        collected.Add((tableName, rtId, searchParamId, null));
                    }
                }
            }

            /// <summary>
            /// Walks the inner expression tree of a reference search parameter to extract
            /// target resource type IDs from <see cref="StringExpression"/> nodes with
            /// <see cref="FieldName.ReferenceResourceType"/>.
            /// </summary>
            private static void CollectReferenceResourceTypes(Expression expression, SqlServerFhirModel model, HashSet<short> referenceResourceTypeIds)
            {
                switch (expression)
                {
                    case StringExpression se when se.FieldName == FieldName.ReferenceResourceType:
                        if (model.TryGetResourceTypeId(se.Value, out var rtId))
                        {
                            referenceResourceTypeIds.Add(rtId);
                        }

                        break;
                    case MultiaryExpression me:
                        foreach (var inner in me.Expressions)
                        {
                            CollectReferenceResourceTypes(inner, model, referenceResourceTypeIds);
                        }

                        break;
                    case SearchParameterExpression innerSpe:
                        CollectReferenceResourceTypes(innerSpe.Expression, model, referenceResourceTypeIds);
                        break;
                }
            }
            */

            private static void CollectResourceTypesFromExpression(Expression expression, SqlServerFhirModel model, HashSet<short> resourceTypeIds)
            {
                switch (expression)
                {
                    case StringExpression se:
                        if (model.TryGetResourceTypeId(se.Value, out var rtId))
                        {
                            resourceTypeIds.Add(rtId);
                        }

                        break;
                    case MultiaryExpression me:
                        foreach (var inner in me.Expressions)
                        {
                            CollectResourceTypesFromExpression(inner, model, resourceTypeIds);
                        }

                        break;
                    case SearchParameterExpression innerSpe:
                        CollectResourceTypesFromExpression(innerSpe.Expression, model, resourceTypeIds);
                        break;
                }
            }

            private static HashSet<string> GetKeyColumns(string table) => SqlServerSearchService.GetKeyColumns(table);

            private async Task Create(string tableName, string columnName, short resourceTypeId, short searchParamId, short? referenceResourceTypeId, ISqlRetryService sqlRetryService, ILogger<SqlServerSearchService> logger, CancellationToken cancel)
            {
                // The 3-column reference-type filtered stat (which additionally filters on ReferenceResourceTypeId)
                // is gated behind a feature flag that is OFF by default. When disabled, fall back to the broader
                // 2-column filtered stat (ResourceTypeId + SearchParamId) so reference searches still get a stat.
                if (referenceResourceTypeId.HasValue && !_referenceResourceTypeFilteredStats.IsEnabled(sqlRetryService))
                {
                    referenceResourceTypeId = null;
                }

                if (_stats.ContainsKey((tableName, columnName, resourceTypeId, searchParamId, referenceResourceTypeId)))
                {
                    logger.LogInformation("ResourceSearchParamStats.FoundInCache Table={Table} Column={Column} Type={ResourceType} Param={SearchParam} RefType={ReferenceResourceType}", tableName, columnName, resourceTypeId, searchParamId, referenceResourceTypeId);
                    return;
                }

                try
                {
                    using var cmd = new SqlCommand() { CommandText = "dbo.CreateResourceSearchParamStats", CommandType = CommandType.StoredProcedure };
                    cmd.Parameters.AddWithValue("@Table", tableName[4..]); // remove dbo.
                    cmd.Parameters.AddWithValue("@Column", columnName);
                    cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
                    cmd.Parameters.AddWithValue("@SearchParamId", searchParamId);
                    cmd.Parameters.AddWithValue("@ReferenceResourceTypeId", (object)referenceResourceTypeId ?? DBNull.Value);
                    await cmd.ExecuteNonQueryAsync(sqlRetryService, logger, cancel);

                    _stats.TryAdd((tableName, columnName, resourceTypeId, searchParamId, referenceResourceTypeId), true);

                    logger.LogInformation("ResourceSearchParamStats.CreateStats.Completed Table={Table} Column={Column} Type={ResourceType} Param={SearchParam} RefType={ReferenceResourceType}", tableName, columnName, resourceTypeId, searchParamId, referenceResourceTypeId);
                }
                catch (SqlException ex)
                {
                    logger.LogWarning(ex, "ResourceSearchParamStats.CreateStats: Exception={Exception}", ex.Message);
                }
            }

            private async Task Init(ISqlRetryService sqlRetryService, ILogger<SqlServerSearchService> logger, CancellationToken cancel)
            {
                try
                {
                    var stats = await GetStatsFromDatabase(sqlRetryService, logger, cancel);

                    foreach (var stat in stats)
                    {
                        _stats.TryAdd(stat, true);
                    }

                    logger.LogInformation("ResourceSearchParamStats.Init: Stats={Stats}", stats.Count);
                }
                catch (SqlException ex)
                {
                    logger.LogWarning(ex, "ResourceSearchParamStats.Init: Exception={Exception}", ex.Message);
                }
            }
        }

        /*
        private class Token
        {
            internal Token(string code, int? systemId, string systemValue)
            {
                Code = code;
                SystemId = systemId;
                SystemValue = systemId.HasValue ? null : systemValue;
            }

            internal string Code { get; set; }

            internal int? SystemId { get; set; }

            internal string SystemValue { get; set; }
        }

        private class TokenListRowGenerator : ITableValuedParameterRowGenerator<IList<Token>, TokenListRow>
        {
            private readonly int _codeMaxLength = (int)VLatest.TokenSearchParam.Code.Metadata.MaxLength;

            public IEnumerable<TokenListRow> GenerateRows(IList<Token> tokens)
            {
                foreach (var token in tokens)
                {
                    string code;
                    string codeOverflow;
                    if (token.Code.Length > _codeMaxLength)
                    {
                        code = token.Code[.._codeMaxLength];
                        codeOverflow = token.Code[_codeMaxLength..];
                    }
                    else
                    {
                        code = token.Code;
                        codeOverflow = null;
                    }

                    yield return new TokenListRow(code, codeOverflow, token.SystemId, token.SystemValue);
                }
            }
        }
        */
    }
}
