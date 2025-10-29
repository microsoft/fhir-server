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
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
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
    internal class SqlServerSearchService : SearchService
    {
        private static readonly GetResourceSurrogateIdRangesProcedure GetResourceSurrogateIdRanges = new GetResourceSurrogateIdRangesProcedure();

        private readonly ISqlServerFhirModel _model;
        private readonly SqlRootExpressionRewriter _sqlRootExpressionRewriter;
        private readonly SearchParamTableExpressionQueryGeneratorFactory _queryGeneratorFactory;

        private readonly SortRewriter _sortRewriter;
        private readonly PartitionEliminationRewriter _partitionEliminationRewriter;
        private readonly CompartmentSearchRewriter _compartmentSearchRewriter;
        private readonly SmartCompartmentSearchRewriter _smartCompartmentSearchRewriter;
        private readonly ChainFlatteningRewriter _chainFlatteningRewriter;
        private readonly ILogger<SqlServerSearchService> _logger;
        private readonly BitColumn _isMatch = new BitColumn("IsMatch");
        private readonly BitColumn _isPartial = new BitColumn("IsPartial");
        private readonly ISqlRetryService _sqlRetryService;
        private readonly SqlServerDataStoreConfiguration _sqlServerDataStoreConfiguration;
        private const string SortValueColumnName = "SortValue";
        private readonly SchemaInformation _schemaInformation;
        private readonly ICompressedRawResourceConverter _compressedRawResourceConverter;
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly SearchParameterInfo _fakeLastUpdate = new SearchParameterInfo(SearchParameterNames.LastUpdated, SearchParameterNames.LastUpdated);
        private readonly ISqlQueryHashCalculator _queryHashCalculator;
        private readonly IFhirDataStore _fhirDataStore;
        private static ResourceSearchParamStats _resourceSearchParamStats;
        private static object _locker = new object();
        private static ProcessingFlag<SqlServerSearchService> _reuseQueryPlans;
        internal const string ReuseQueryPlansParameterId = "Search.ReuseQueryPlans.IsEnabled";

        public SqlServerSearchService(
            ISearchOptionsFactory searchOptionsFactory,
            IFhirDataStore fhirDataStore,
            ISqlServerFhirModel model,
            SqlRootExpressionRewriter sqlRootExpressionRewriter,
            ChainFlatteningRewriter chainFlatteningRewriter,
            SortRewriter sortRewriter,
            PartitionEliminationRewriter partitionEliminationRewriter,
            CompartmentSearchRewriter compartmentSearchRewriter,
            SmartCompartmentSearchRewriter smartCompartmentSearchRewriter,
            SearchParamTableExpressionQueryGeneratorFactory queryGeneratorFactory,
            ISqlRetryService sqlRetryService,
            IOptions<SqlServerDataStoreConfiguration> sqlServerDataStoreConfiguration,
            SchemaInformation schemaInformation,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            ICompressedRawResourceConverter compressedRawResourceConverter,
            ISqlQueryHashCalculator queryHashCalculator,
            ILogger<SqlServerSearchService> logger)
            : base(searchOptionsFactory, fhirDataStore, logger)
        {
            EnsureArg.IsNotNull(sqlRootExpressionRewriter, nameof(sqlRootExpressionRewriter));
            EnsureArg.IsNotNull(chainFlatteningRewriter, nameof(chainFlatteningRewriter));
            EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(partitionEliminationRewriter, nameof(partitionEliminationRewriter));
            EnsureArg.IsNotNull(compartmentSearchRewriter, nameof(compartmentSearchRewriter));
            EnsureArg.IsNotNull(smartCompartmentSearchRewriter, nameof(smartCompartmentSearchRewriter));
            EnsureArg.IsNotNull(queryGeneratorFactory, nameof(queryGeneratorFactory));
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlServerDataStoreConfiguration = EnsureArg.IsNotNull(sqlServerDataStoreConfiguration?.Value, nameof(sqlServerDataStoreConfiguration));
            _fhirDataStore = fhirDataStore;
            _model = model;
            _sqlRootExpressionRewriter = sqlRootExpressionRewriter;
            _sortRewriter = sortRewriter;
            _queryGeneratorFactory = queryGeneratorFactory;
            _partitionEliminationRewriter = partitionEliminationRewriter;
            _compartmentSearchRewriter = compartmentSearchRewriter;
            _smartCompartmentSearchRewriter = smartCompartmentSearchRewriter;
            _chainFlatteningRewriter = chainFlatteningRewriter;
            _sqlRetryService = sqlRetryService;
            _queryHashCalculator = queryHashCalculator;
            _logger = logger;

            _schemaInformation = schemaInformation;
            _requestContextAccessor = requestContextAccessor;
            _compressedRawResourceConverter = compressedRawResourceConverter;

            if (_reuseQueryPlans == null)
            {
                lock (_locker)
                {
                    _reuseQueryPlans ??= new ProcessingFlag<SqlServerSearchService>(ReuseQueryPlansParameterId, false, _logger);
                }
            }
        }

        internal ISqlServerFhirModel Model => _model;

        internal static void ResetReuseQueryPlans()
        {
            _reuseQueryPlans.Reset();
        }

        public override async Task<SearchResult> SearchAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            SqlSearchOptions sqlSearchOptions = new SqlSearchOptions(searchOptions);

            SearchResult searchResult = await RunSearch(sqlSearchOptions, cancellationToken);
            int resultCount = searchResult.Results.Count(r => r.SearchEntryMode == SearchEntryMode.Match);

            if (!sqlSearchOptions.IsSortWithFilter &&
                searchResult.ContinuationToken == null &&
                resultCount <= sqlSearchOptions.MaxItemCount &&
                sqlSearchOptions.Sort != null &&
                sqlSearchOptions.Sort.Count > 0 &&
                sqlSearchOptions.Sort[0].searchParameterInfo.Code != KnownQueryParameterNames.LastUpdated)
            {
                // We seem to have run a sort which has returned less results than what max we can return.
                // Let's determine whether we need to execute another query or not.
                if ((sqlSearchOptions.Sort[0].sortOrder == SortOrder.Ascending && sqlSearchOptions.DidWeSearchForSortValue.HasValue && !sqlSearchOptions.DidWeSearchForSortValue.Value) ||
                    (sqlSearchOptions.Sort[0].sortOrder == SortOrder.Descending && sqlSearchOptions.DidWeSearchForSortValue.HasValue && sqlSearchOptions.DidWeSearchForSortValue.Value && !sqlSearchOptions.SortHasMissingModifier) || (sqlSearchOptions.Sort[0].sortOrder == SortOrder.Descending && resultCount == 0 && !sqlSearchOptions.CountOnly))
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

                        searchResult = new SearchResult(searchResult.Results, ct?.ToJson(), searchResult.SortOrder, searchResult.UnsupportedSearchParameters);
                    }
                    else
                    {
                        var finalResultsInOrder = new List<SearchResultEntry>();
                        finalResultsInOrder.AddRange(searchResult.Results);
                        sqlSearchOptions.SortQuerySecondPhase = true;
                        sqlSearchOptions.MaxItemCount -= resultCount;

                        searchResult = await RunSearch(sqlSearchOptions, cancellationToken);

                        finalResultsInOrder.AddRange(searchResult.Results);
                        searchResult = new SearchResult(
                            finalResultsInOrder,
                            searchResult.ContinuationToken,
                            searchResult.SortOrder,
                            searchResult.UnsupportedSearchParameters);
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
                    return await SearchImpl(sqlSearchOptions, _reuseQueryPlans.IsEnabled(_sqlRetryService), cancellationToken);
                }
            }
            else
            {
                return await SearchImpl(sqlSearchOptions, _reuseQueryPlans.IsEnabled(_sqlRetryService), cancellationToken);
            }
        }

        private async Task<SearchResult> SearchImpl(SqlSearchOptions sqlSearchOptions, bool reuseQueryPlans, CancellationToken cancellationToken)
        {
            if (sqlSearchOptions.IsIncludesOperation)
            {
                return await SearchIncludeImpl(sqlSearchOptions, cancellationToken);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            Expression searchExpression = sqlSearchOptions.Expression;

            // AND in the continuation token
            if (!string.IsNullOrWhiteSpace(sqlSearchOptions.ContinuationToken) && !sqlSearchOptions.CountOnly)
            {
                var continuationToken = ContinuationToken.FromString(sqlSearchOptions.ContinuationToken);
                if (continuationToken != null)
                {
                    if (string.IsNullOrEmpty(continuationToken.SortValue))
                    {
                        // Check whether it's a _lastUpdated or (_type,_lastUpdated) sort optimization
                        bool optimize = true;
                        (SearchParameterInfo searchParamInfo, SortOrder sortOrder) = sqlSearchOptions.Sort.Count == 0 ? default : sqlSearchOptions.Sort[0];
                        if (sqlSearchOptions.Sort.Count > 0)
                        {
                            if (!(searchParamInfo.Name == SearchParameterNames.LastUpdated || searchParamInfo.Name == SearchParameterNames.ResourceType))
                            {
                                optimize = false;
                            }
                        }

                        FieldName fieldName;
                        object keyValue;
                        SearchParameterInfo parameter;
                        if (continuationToken.ResourceTypeId == null || _schemaInformation.Current < SchemaVersionConstants.PartitionedTables)
                        {
                            // backwards compat
                            parameter = SqlSearchParameters.ResourceSurrogateIdParameter;
                            fieldName = SqlFieldName.ResourceSurrogateId;
                            keyValue = continuationToken.ResourceSurrogateId;
                        }
                        else
                        {
                            parameter = SqlSearchParameters.PrimaryKeyParameter;
                            fieldName = SqlFieldName.PrimaryKey;
                            keyValue = new PrimaryKeyValue(continuationToken.ResourceTypeId.Value, continuationToken.ResourceSurrogateId);
                        }

                        Expression lastUpdatedExpression = !optimize
                            ? Expression.GreaterThan(fieldName, null, keyValue)
                            : sortOrder == SortOrder.Ascending
                                ? Expression.GreaterThan(fieldName, null, keyValue)
                                : Expression.LessThan(fieldName, null, keyValue);

                        var tokenExpression = Expression.SearchParameter(parameter, lastUpdatedExpression);
                        searchExpression = searchExpression == null ? tokenExpression : Expression.And(tokenExpression, searchExpression);
                    }
                }
                else
                {
                    _logger.LogWarning("Bad Request (InvalidContinuationToken)");
                    throw new BadRequestException(Resources.InvalidContinuationToken);
                }
            }

            var originalSort = new List<(SearchParameterInfo, SortOrder)>(sqlSearchOptions.Sort);
            var clonedSearchOptions = UpdateSort(sqlSearchOptions, searchExpression);

            if (clonedSearchOptions.CountOnly)
            {
                // if we're only returning a count, discard any _include parameters since included resources are not counted.
                searchExpression = searchExpression?.AcceptVisitor(RemoveIncludesRewriter.Instance);
            }

            // ! - Trace
            SqlRootExpression expression = (SqlRootExpression)CreateDefaultSearchExpression(searchExpression, clonedSearchOptions)
                ?.AcceptVisitor(IncludeRewriter.Instance)
                ?? SqlRootExpression.WithResourceTableExpressions();

            await CreateStats(expression, cancellationToken);

            SearchResult searchResult = null;

            await _sqlRetryService.ExecuteSql(
                async (connection, cancellationToken, sqlException) =>
                {
                    using (SqlCommand sqlCommand = connection.CreateCommand()) // WARNING, this code will not set sqlCommand.Transaction. Sql transactions via C#/.NET are not supported in this method.
                    {
                        sqlCommand.CommandTimeout = (int)_sqlServerDataStoreConfiguration.CommandTimeout.TotalSeconds;
                        var isSortValueNeeded = false;

                        var exportTimeTravel = clonedSearchOptions.QueryHints != null && ContainsGlobalEndSurrogateId(clonedSearchOptions);
                        if (exportTimeTravel)
                        {
                            PopulateSqlCommandFromQueryHints(clonedSearchOptions, sqlCommand);
                            sqlCommand.CommandTimeout = 1200; // set to 20 minutes, as dataset is usually large
                        }
                        else if (await TryHandleSimpleResourceReadAsync(expression, clonedSearchOptions, _fhirDataStore, cancellationToken) is SearchResult simpleReadResult)
                        {
                            // Simple resource read was handled directly via FhirDataStore.GetAsync
                            _logger.LogInformation("Handled simple resource read directly via FhirDataStore.GetAsync");
                            searchResult = simpleReadResult;
                            return;
                        }
                        else
                        {
                            var stringBuilder = new IndentedStringBuilder(new StringBuilder());

                            EnableTimeAndIoMessageLogging(stringBuilder, connection);

                            var queryGenerator = new SqlQueryGenerator(
                                stringBuilder,
                                new HashingSqlQueryParameterManager(new SqlQueryParameterManager(sqlCommand.Parameters)),
                                _model,
                                _schemaInformation,
                                _queryGeneratorFactory,
                                reuseQueryPlans,
                                sqlSearchOptions.IsAsyncOperation,
                                sqlException);

                            expression.AcceptVisitor(queryGenerator, clonedSearchOptions);
                            isSortValueNeeded = queryGenerator.IsSortValueNeeded(clonedSearchOptions);

                            SqlCommandSimplifier.RemoveRedundantParameters(stringBuilder, sqlCommand.Parameters, _logger);

                            var queryText = stringBuilder.ToString();
                            var queryHash = _queryHashCalculator.CalculateHash(queryText);
                            _logger.LogInformation("SQL Search Service query hash: {QueryHash}", queryHash);
                            var customQuery = CustomQueries.CheckQueryHash(connection, queryHash, _logger);

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

                            _logger.LogInformation($"Query.SearchParamIds={string.Join(",", queryGenerator.SearchParamIds)}");
                        }

                        LogSqlCommand(sqlCommand);

                        var st = DateTime.UtcNow;
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
                                        ? new ContinuationToken(
                                            clonedSearchOptions.Sort.Select(s =>
                                                s.searchParameterInfo.Name switch
                                                {
                                                    SearchParameterNames.ResourceType => (object)newContinuationType,
                                                    SearchParameterNames.LastUpdated => newContinuationId,
                                                    _ => sortValue,
                                                }).ToArray())
                                        : null;
                                string includesContinuationTokenString = null;
                                if (clonedSearchOptions.IncludesOperationSupported
                                    && clonedSearchOptions.Expression is MultiaryExpression
                                    && ((MultiaryExpression)clonedSearchOptions.Expression).Expressions.Any(x => x is IncludeExpression)
                                    && newContinuationType.HasValue
                                    && newContinuationId.HasValue
                                    && matchedResourceSurrogateIdStart.HasValue
                                    && (isResultPartial || includedResources.Count > clonedSearchOptions.IncludeCount))
                                {
                                    clonedSearchOptions.IncludesContinuationToken = new IncludesContinuationToken(
                                        new object[]
                                        {
                                            newContinuationType.Value,
                                            matchedResourceSurrogateIdStart.Value,
                                            newContinuationId.Value,
                                        }).ToJson();

                                    var includesSearchResult = await SearchIncludeImpl(clonedSearchOptions, cancellationToken);
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
                                            clonedSearchOptions.IncludesOperationSupported ? Core.Resources.TruncatedIncludeMessageForIncludes : Core.Resources.TruncatedIncludeMessage));
                                }

                                // If this is a sort query, lets keep track of whether we actually searched for sort values.
                                if (clonedSearchOptions.Sort != null &&
                                    clonedSearchOptions.Sort.Count > 0 &&
                                    clonedSearchOptions.Sort[0].searchParameterInfo.Code != KnownQueryParameterNames.LastUpdated)
                                {
                                    // If there is an extra column for sort value, we know we have searched for sort values. If no results were returned, we don't know if we have searched for sort values so we need to assume we did so we run the second phase.
                                    sqlSearchOptions.DidWeSearchForSortValue = isSortValueNeeded;
                                }

                                // This value is set inside the SortRewriter. If it is set, we need to pass
                                // this value back to the caller.
                                if (clonedSearchOptions.IsSortWithFilter)
                                {
                                    sqlSearchOptions.IsSortWithFilter = true;
                                }

                                if (clonedSearchOptions.SortHasMissingModifier)
                                {
                                    sqlSearchOptions.SortHasMissingModifier = true;
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
                    }
                },
                _logger,
                cancellationToken,
                true); // this enables reads from replicas

            _logger.LogInformation("Search completed in {ElapsedMilliseconds}ms, query cache enabled: {QueryCacheEnabled}.", stopwatch.ElapsedMilliseconds, reuseQueryPlans);
            return searchResult;
        }

        private static bool ContainsGlobalEndSurrogateId(SqlSearchOptions options)
        {
            IReadOnlyList<(string Param, string Value)> hints = options.QueryHints;
            return hints.Any(x => string.Equals(KnownQueryParameterNames.GlobalEndSurrogateId, x.Param, StringComparison.OrdinalIgnoreCase));
        }

        private void PopulateSqlCommandFromQueryHints(SqlSearchOptions options, SqlCommand command)
        {
            IReadOnlyList<(string Param, string Value)> hints = options.QueryHints;

            var resourceTypeId = _model.GetResourceTypeId(hints.First(x => x.Param == KnownQueryParameterNames.Type).Value);
            var startId = long.Parse(hints.First(x => x.Param == KnownQueryParameterNames.StartSurrogateId).Value);
            var endId = long.Parse(hints.First(x => x.Param == KnownQueryParameterNames.EndSurrogateId).Value);
            var globalStartId = long.Parse(hints.First(x => x.Param == KnownQueryParameterNames.GlobalStartSurrogateId).Value);
            var globalEndId = long.Parse(hints.First(x => x.Param == KnownQueryParameterNames.GlobalEndSurrogateId).Value);

            PopulateSqlCommandFromQueryHints(command, resourceTypeId, startId, endId, globalEndId, options.ResourceVersionTypes.HasFlag(ResourceVersionType.History), options.ResourceVersionTypes.HasFlag(ResourceVersionType.SoftDeleted));
        }

        private static void PopulateSqlCommandFromQueryHints(SqlCommand command, short resourceTypeId, long startId, long endId, long? globalEndId, bool? includeHistory, bool? includeDeleted)
        {
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "dbo.GetResourcesByTypeAndSurrogateIdRange";
            command.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            command.Parameters.AddWithValue("@StartId", startId);
            command.Parameters.AddWithValue("@EndId", endId);
            command.Parameters.AddWithValue("@GlobalEndId", globalEndId);
            command.Parameters.AddWithValue("@IncludeHistory", includeHistory);
            command.Parameters.AddWithValue("@IncludeDeleted", includeDeleted);
        }

        /// <summary>
        /// Attempts to populate a SqlCommand to use the ReadResource stored procedure for simple resource reads by ID.
        /// This provides better performance than the generated SQL for simple resource lookups.
        /// </summary>
        /// <param name="expression">The SQL root expression to analyze</param>
        /// <param name="searchOptions">The search options</param>
        /// <param name="command">The SQL command to populate</param>
        /// <returns>True if the command was populated for a simple resource read, false otherwise</returns>
        private bool TryPopulateReadResourceCommand(SqlRootExpression expression, SqlSearchOptions searchOptions, SqlCommand command)
        {
            // Only use ReadResource stored proc for simple cases
            if (searchOptions.CountOnly ||
                searchOptions.IncludeCount != 0 ||
                searchOptions.Sort?.Count > 0 ||
                expression.SearchParamTableExpressions.Count > 0 ||
                expression.ResourceTableExpressions.Count != 2) // Should have exactly ResourceType and Id
            {
                return false;
            }

            // For versioned reads, we need ResourceVersionTypes to include History, but we still want the optimization
            // when searching for a single resource by ID. Note: The ReadResource stored procedure supports a @version
            // parameter, but specific version numbers are not currently passed through the search parameter system.
            // For now, we allow the optimization for history searches but always pass NULL for version (latest version).
            // TODO: Future enhancement could extract specific version information if available.
            bool isHistorySearch = searchOptions.ResourceVersionTypes.HasFlag(ResourceVersionType.History);
            bool isLatestOnly = searchOptions.ResourceVersionTypes == ResourceVersionType.Latest;

            if (!isLatestOnly && !isHistorySearch)
            {
                // Only allow Latest or searches that include History (for versioned reads)
                return false;
            }

            // Find ResourceType and Id expressions
            SearchParameterExpression resourceTypeExpression = null;
            SearchParameterExpression resourceIdExpression = null;

            foreach (var searchParamExpression in expression.ResourceTableExpressions.OfType<SearchParameterExpression>())
            {
                if (searchParamExpression.Parameter.Code == SearchParameterNames.ResourceType)
                {
                    resourceTypeExpression = searchParamExpression;
                }
                else if (searchParamExpression.Parameter.Code == SearchParameterNames.Id)
                {
                    resourceIdExpression = searchParamExpression;
                }
            }

            // Must have both ResourceType and Id expressions
            if (resourceTypeExpression == null || resourceIdExpression == null)
            {
                return false;
            }

            // Extract resource type and id values
            if (!TryExtractSimpleStringValue(resourceTypeExpression.Expression, out string resourceTypeName) ||
                !TryExtractSimpleStringValue(resourceIdExpression.Expression, out string resourceId))
            {
                return false;
            }

            // Get resource type ID
            if (!_model.TryGetResourceTypeId(resourceTypeName, out short resourceTypeId))
            {
                return false;
            }

            // Populate command to use ReadResource stored procedure
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "dbo.ReadResource";
            command.Parameters.AddWithValue("@resourceTypeId", resourceTypeId);
            command.Parameters.AddWithValue("@resourceId", resourceId);
            command.Parameters.AddWithValue("@version", DBNull.Value); // null for latest version

            return true;
        }

        /// <summary>
        /// Attempts to extract a simple string value from an expression (e.g., StringExpression).
        /// </summary>
        /// <param name="expression">The expression to analyze</param>
        /// <param name="value">The extracted string value</param>
        /// <returns>True if a simple string value was extracted, false otherwise</returns>
        private static bool TryExtractSimpleStringValue(Expression expression, out string value)
        {
            value = null;

            if (expression is StringExpression stringExpression)
            {
                value = stringExpression.Value;
                return !string.IsNullOrEmpty(value);
            }

            return false;
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
        /// <param name="searchParamHashFilter">When not null then we filter using the searchParameterHash</param>
        /// <param name="includeHistory">Return historical records that match the other parameters.</param>
        /// <param name="includeDeleted">Return deleted records that match the other parameters.</param>
        /// <returns>All resources with surrogate ids greater than or equal to startId and less than or equal to endId. If windowEndId is set it will return the most recent version of a resource that was created before windowEndId that is within the range of startId to endId.</returns>
        public async Task<SearchResult> SearchBySurrogateIdRange(string resourceType, long startId, long endId, long? windowStartId, long? windowEndId, CancellationToken cancellationToken, string searchParamHashFilter = null, bool includeHistory = false, bool includeDeleted = false)
        {
            var resourceTypeId = _model.GetResourceTypeId(resourceType);
            using var sqlCommand = new SqlCommand();
            sqlCommand.CommandTimeout = GetReindexCommandTimeout();
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

                        // original sql was: AND (SearchParamHash != @p0 OR SearchParamHash IS NULL)
                        if (!(searchParameterHash == null || searchParameterHash != searchParamHashFilter))
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

        private static (long StartId, long EndId) ReaderToSurrogateIdRange(SqlDataReader sqlDataReader)
        {
            return (sqlDataReader.GetInt64(1), sqlDataReader.GetInt64(2));
        }

        public override async Task<IReadOnlyList<(long StartId, long EndId)>> GetSurrogateIdRanges(string resourceType, long startId, long endId, int rangeSize, int numberOfRanges, bool up, CancellationToken cancellationToken, bool? activeOnly = false)
        {
            var resourceTypeId = _model.GetResourceTypeId(resourceType);
            using var sqlCommand = new SqlCommand();
            GetResourceSurrogateIdRanges.PopulateCommand(sqlCommand, resourceTypeId, startId, endId, rangeSize, numberOfRanges, up, activeOnly, _schemaInformation);
            sqlCommand.CommandTimeout = GetReindexCommandTimeout();
            LogSqlCommand(sqlCommand);
            return await sqlCommand.ExecuteReaderAsync(_sqlRetryService, ReaderToSurrogateIdRange, _logger, cancellationToken);
        }

        private static string ReaderGetUsedResourceTypes(SqlDataReader sqlDataReader)
        {
            return sqlDataReader.GetString(1);
        }

        private static (long StartResourceSurrogateId, long EndResourceSurrogateId, int Count) ReaderGetSurrogateIdsAndCountForResourceType(SqlDataReader sqlDataReader)
        {
            return (sqlDataReader.GetInt64(0), sqlDataReader.GetInt64(1), sqlDataReader.GetInt32(2));
        }

        public override async Task<IReadOnlyList<string>> GetUsedResourceTypes(CancellationToken cancellationToken)
        {
            using var sqlCommand = new SqlCommand("dbo.GetUsedResourceTypes") { CommandType = CommandType.StoredProcedure };
            LogSqlCommand(sqlCommand);
            return await sqlCommand.ExecuteReaderAsync(_sqlRetryService, ReaderGetUsedResourceTypes, _logger, cancellationToken);
        }

        /// <summary>
        /// If no sorting fields are specified, sets the sorting fields to the primary key. (This is either ResourceSurrogateId or ResourceTypeId, ResourceSurrogateId).
        /// If sorting only by ResourceTypeId, adds in ResourceSurrogateId as the second sort column.
        /// If sorting by ResourceSurrogateId and using partitioned tables and searching over a single type, sets the sort to ResourceTypeId, ResourceSurrogateId
        /// </summary>
        /// <param name="searchOptions">The input SearchOptions</param>
        /// <param name="searchExpression">The searchExpression</param>
        /// <returns>If the sort needs to be updated, a new <see cref="SearchOptions"/> instance, otherwise, the same instance as <paramref name="searchOptions"/></returns>
        private SqlSearchOptions UpdateSort(SqlSearchOptions searchOptions, Expression searchExpression)
        {
            SqlSearchOptions newSearchOptions = searchOptions;
            if (searchOptions.ResourceVersionTypes.HasFlag(ResourceVersionType.History) && searchOptions.Sort.Any())
            {
                // history is always sorted by _lastUpdated (except for export).
                newSearchOptions = searchOptions.CloneSqlSearchOptions();

                return newSearchOptions;
            }

            if (searchOptions.Sort.Count == 0)
            {
                newSearchOptions = searchOptions.CloneSqlSearchOptions();

                if (_schemaInformation.Current < SchemaVersionConstants.PartitionedTables)
                {
                    newSearchOptions.Sort = new (SearchParameterInfo searchParameterInfo, SortOrder sortOrder)[]
                    {
                        (_fakeLastUpdate, SortOrder.Ascending),
                    };
                }
                else
                {
                    newSearchOptions.Sort = new (SearchParameterInfo searchParameterInfo, SortOrder sortOrder)[]
                    {
                        (SearchParameterInfo.ResourceTypeSearchParameter, SortOrder.Ascending),
                        (_fakeLastUpdate, SortOrder.Ascending),
                    };
                }

                return newSearchOptions;
            }

            if (searchOptions.Sort.Count == 1 && searchOptions.Sort[0].searchParameterInfo.Name == SearchParameterNames.ResourceType)
            {
                // We will not get here unless the schema version is at least SchemaVersionConstants.PartitionedTables.

                // Add _lastUpdated to the sort list so that there is a deterministic key to sort on

                newSearchOptions = searchOptions.CloneSqlSearchOptions();

                newSearchOptions.Sort = new (SearchParameterInfo searchParameterInfo, SortOrder sortOrder)[]
                {
                    (SearchParameterInfo.ResourceTypeSearchParameter, searchOptions.Sort[0].sortOrder),
                    (_fakeLastUpdate, searchOptions.Sort[0].sortOrder),
                };

                return newSearchOptions;
            }

            if (searchOptions.Sort.Count == 1 && searchOptions.Sort[0].searchParameterInfo.Name == SearchParameterNames.LastUpdated && _schemaInformation.Current >= SchemaVersionConstants.PartitionedTables)
            {
                (short? singleAllowedTypeId, BitArray allowedTypes) = TypeConstraintVisitor.Instance.Visit(searchExpression, _model);

                if (singleAllowedTypeId != null && allowedTypes != null)
                {
                    // this means that this search is over a single type.
                    newSearchOptions = searchOptions.CloneSqlSearchOptions();

                    newSearchOptions.Sort = new (SearchParameterInfo searchParameterInfo, SortOrder sortOrder)[]
                    {
                        (SearchParameterInfo.ResourceTypeSearchParameter, searchOptions.Sort[0].sortOrder),
                        (_fakeLastUpdate, searchOptions.Sort[0].sortOrder),
                    };
                }

                return newSearchOptions;
            }

            if (searchOptions.Sort[^1].searchParameterInfo.Name != SearchParameterNames.LastUpdated)
            {
                // Make sure custom sort has _lastUpdated as the last sort parameter.

                newSearchOptions = searchOptions.CloneSqlSearchOptions();

                newSearchOptions.Sort = new List<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)>(searchOptions.Sort)
                {
                    (_fakeLastUpdate, SortOrder.Ascending),
                };

                return newSearchOptions;
            }

            return newSearchOptions;
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
        /// Searches for resources by their type and surrogate id and optionally a searchParamHash. This can also just return a count of resources.
        /// </summary>
        /// <param name="searchOptions">The searchOptions</param>
        /// <param name="searchParameterHash">A searchParamHash to filter results</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>SearchResult</returns>
        protected async override Task<SearchResult> SearchForReindexInternalAsync(SearchOptions searchOptions, string searchParameterHash, CancellationToken cancellationToken)
        {
            string resourceType = GetForceReindexResourceType(searchOptions);
            if (searchOptions.CountOnly)
            {
                _model.TryGetResourceTypeId(resourceType, out short resourceTypeId);

                // Check if we have surrogate ID range hints - if so, use the optimized range count
                if (searchOptions.QueryHints != null &&
                    searchOptions.QueryHints.Any(h => h.Param == KnownQueryParameterNames.StartSurrogateId) &&
                    searchOptions.QueryHints.Any(h => h.Param == KnownQueryParameterNames.EndSurrogateId))
                {
                    long startId = long.Parse(searchOptions.QueryHints.First(h => h.Param == KnownQueryParameterNames.StartSurrogateId).Value);
                    long endId = long.Parse(searchOptions.QueryHints.First(h => h.Param == KnownQueryParameterNames.EndSurrogateId).Value);

                    int count = await GetResourceCountBySurrogateIdRangeAsync(
                        resourceTypeId,
                        startId,
                        endId,
                        searchOptions.IgnoreSearchParamHash ? null : searchParameterHash,
                        cancellationToken);

                    var searchResult = new SearchResult(count, Array.Empty<Tuple<string, string>>());
                    searchResult.ReindexResult = new SearchResultReindex()
                    {
                        Count = count,
                        StartResourceSurrogateId = startId,
                        EndResourceSurrogateId = endId,
                    };

                    _logger.LogInformation("Count for reindex by range: Resource Type={ResourceType} StartId={StartId} EndId={EndId} Count={Count}", resourceType, startId, endId, count);

                    return searchResult;
                }

                // Fall back to the original method if no range hints are provided
                return await SearchForReindexSurrogateIdsBySearchParamHashAsync(resourceTypeId, searchOptions.MaxItemCount, cancellationToken, searchParameterHash);
            }

            var queryHints = searchOptions.QueryHints;
            long globalStartId = long.Parse(queryHints.First(h => h.Param == KnownQueryParameterNames.StartSurrogateId).Value);
            long globalEndId = long.Parse(queryHints.First(h => h.Param == KnownQueryParameterNames.EndSurrogateId).Value);
            long queryStartId = globalStartId;

            SearchResult results = null;

            // Search within the surrogate ID range
            results = await SearchBySurrogateIdRange(
                resourceType,
                globalStartId,
                globalEndId,
                null,
                null,
                cancellationToken,
                searchOptions.IgnoreSearchParamHash ? null : searchParameterHash);

            if (results.Results.Any())
            {
                results.MaxResourceSurrogateId = results.Results.Max(e => e.Resource.ResourceSurrogateId);
                _logger.LogInformation("For Reindex, Resource Type={ResourceType} Count={Count} MaxResourceSurrogateId={MaxResourceSurrogateId}", resourceType, results.TotalCount, results.MaxResourceSurrogateId);
                return results;
            }

            // Return empty result when no resources are found in the given range provided by queryHints.
            _logger.LogInformation("No surrogate ID ranges found containing data. Resource Type={ResourceType} StartId={StartId} EndId={EndId}", resourceType, globalStartId, globalEndId);
            return new SearchResult(0, []);
        }

        /// <summary>
        /// Searches for the count of resources in n number of sql calls because it uses searchParamHash and because
        /// Resource.SearchParamHash doesn't have an index on it, we need to use maxItemCount to limit the total
        /// number of resources per query
        /// </summary>
        /// <param name="resourceTypeId">The id for the resource type</param>
        /// <param name="maxItemCount">The max items to query at a time</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <param name="searchParamHash">SearchParamHash if we need to filter out the results</param>
        /// <returns>SearchResult</returns>
        private async Task<SearchResult> SearchForReindexSurrogateIdsBySearchParamHashAsync(short resourceTypeId, int maxItemCount, CancellationToken cancellationToken, string searchParamHash = null)
        {
            if (string.IsNullOrWhiteSpace(searchParamHash))
            {
                return await SearchForReindexSurrogateIdsWithoutSearchParamHashAsync(resourceTypeId, cancellationToken);
            }

            // can't use totalCount for reindex on extremely large dbs because we don't have an
            // index on Resource.SearchParamHash which would be necessary to calculate an accurate count
            int totalCount = 0;
            long startResourceSurrogateId = 0;
            long tmpStartResourceSurrogateId = 0;
            long endResourceSurrogateId = 0;
            int rowCount = maxItemCount;
            SearchResult searchResult = null;

            while (true)
            {
                long tmpEndResourceSurrogateId;
                int tmpCount;

                using var sqlCommand = new SqlCommand();
                sqlCommand.CommandTimeout = Math.Max((int)_sqlServerDataStoreConfiguration.CommandTimeout.TotalSeconds, 180);
                sqlCommand.Parameters.AddWithValue("@p0", searchParamHash);
                sqlCommand.Parameters.AddWithValue("@p1", resourceTypeId);
                sqlCommand.Parameters.AddWithValue("@p2", tmpStartResourceSurrogateId);
                sqlCommand.Parameters.AddWithValue("@p3", rowCount);
                sqlCommand.CommandText = @"
SELECT isnull(min(ResourceSurrogateId), 0), isnull(max(ResourceSurrogateId), 0), count(*)
  FROM (SELECT TOP (@p3) ResourceSurrogateId
          FROM dbo.Resource
          WHERE ResourceTypeId = @p1
            AND IsHistory = 0
            AND IsDeleted = 0
            AND ResourceSurrogateId > @p2
            AND (SearchParamHash != @p0 OR SearchParamHash IS NULL)
          ORDER BY
               ResourceSurrogateId
       ) A";
                LogSqlCommand(sqlCommand);

                IReadOnlyList<(long StartResourceSurrogateId, long EndResourceSurrogateId, int Count)> results = await sqlCommand.ExecuteReaderAsync(_sqlRetryService, ReaderGetSurrogateIdsAndCountForResourceType, _logger, cancellationToken);
                if (results.Count == 0)
                {
                    break;
                }

                (long StartResourceSurrogateId, long EndResourceSurrogateId, int Count) singleResult = results.Single();

                tmpStartResourceSurrogateId = singleResult.StartResourceSurrogateId;
                tmpEndResourceSurrogateId = singleResult.EndResourceSurrogateId;
                tmpCount = singleResult.Count;

                totalCount += tmpCount;
                if (startResourceSurrogateId == 0)
                {
                    startResourceSurrogateId = tmpStartResourceSurrogateId;
                }

                if (tmpEndResourceSurrogateId > 0)
                {
                    endResourceSurrogateId = tmpEndResourceSurrogateId;
                    tmpStartResourceSurrogateId = tmpEndResourceSurrogateId;
                }

                if (tmpCount <= 1)
                {
                    break;
                }
            }

            searchResult = new SearchResult(totalCount, Array.Empty<Tuple<string, string>>());
            searchResult.ReindexResult = new SearchResultReindex()
            {
                Count = totalCount,
                StartResourceSurrogateId = startResourceSurrogateId,
                EndResourceSurrogateId = endResourceSurrogateId,
            };

            return searchResult;
        }

        /// <summary>
        /// Searches for the count of resources in one sql call because it doesn't use searchParamHash
        /// </summary>
        /// <param name="resourceTypeId">The id for the resource type</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>SearchResult</returns>
        private async Task<SearchResult> SearchForReindexSurrogateIdsWithoutSearchParamHashAsync(short resourceTypeId, CancellationToken cancellationToken)
        {
            int totalCount = 0;
            long startResourceSurrogateId = 0;
            long endResourceSurrogateId = 0;

            SearchResult searchResult = null;

            using var sqlCommand = new SqlCommand();
            sqlCommand.CommandTimeout = Math.Max((int)_sqlServerDataStoreConfiguration.CommandTimeout.TotalSeconds, 180);
            sqlCommand.Parameters.AddWithValue("@p0", resourceTypeId);
            sqlCommand.CommandText = "SELECT isnull(min(ResourceSurrogateId), 0), isnull(max(ResourceSurrogateId), 0), count(*) FROM dbo.Resource WHERE ResourceTypeId = @p0 AND IsHistory = 0 AND IsDeleted = 0";
            LogSqlCommand(sqlCommand);

            IReadOnlyList<(long StartResourceSurrogateId, long EndResourceSurrogateId, int Count)> results = await sqlCommand.ExecuteReaderAsync(_sqlRetryService, ReaderGetSurrogateIdsAndCountForResourceType, _logger, cancellationToken);
            if (results.Count > 0)
            {
                (long StartResourceSurrogateId, long EndResourceSurrogateId, int Count) singleResult = results.Single();

                startResourceSurrogateId = singleResult.StartResourceSurrogateId;
                endResourceSurrogateId = singleResult.EndResourceSurrogateId;
                totalCount = singleResult.Count;
            }

            searchResult = new SearchResult(totalCount, Array.Empty<Tuple<string, string>>());
            searchResult.ReindexResult = new SearchResultReindex()
            {
                Count = totalCount,
                StartResourceSurrogateId = startResourceSurrogateId,
                EndResourceSurrogateId = endResourceSurrogateId,
            };

            return searchResult;
        }

        /// <summary>
        /// Gets the count of resources within a specific surrogate ID range.
        /// </summary>
        /// <param name="resourceTypeId">The resource type ID</param>
        /// <param name="startId">The lower bound surrogate ID (inclusive)</param>
        /// <param name="endId">The upper bound surrogate ID (inclusive)</param>
        /// <param name="searchParamHash">Optional search parameter hash filter</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The count of resources within the specified range</returns>
        private async Task<int> GetResourceCountBySurrogateIdRangeAsync(
            short resourceTypeId,
            long startId,
            long endId,
            string searchParamHash,
            CancellationToken cancellationToken)
        {
            using var sqlCommand = new SqlCommand();
            sqlCommand.CommandTimeout = GetReindexCommandTimeout();

            if (!string.IsNullOrWhiteSpace(searchParamHash))
            {
                sqlCommand.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
                sqlCommand.Parameters.AddWithValue("@StartId", startId);
                sqlCommand.Parameters.AddWithValue("@EndId", endId);
                sqlCommand.Parameters.AddWithValue("@SearchParamHash", searchParamHash);

                sqlCommand.CommandText = @"
            SELECT COUNT(*) 
            FROM dbo.Resource 
            WHERE ResourceTypeId = @ResourceTypeId 
              AND ResourceSurrogateId >= @StartId 
              AND ResourceSurrogateId <= @EndId
              AND IsHistory = 0 
              AND IsDeleted = 0
              AND (SearchParamHash != @SearchParamHash OR SearchParamHash IS NULL)";
            }
            else
            {
                sqlCommand.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
                sqlCommand.Parameters.AddWithValue("@StartId", startId);
                sqlCommand.Parameters.AddWithValue("@EndId", endId);

                sqlCommand.CommandText = @"
            SELECT COUNT(*) 
            FROM dbo.Resource 
            WHERE ResourceTypeId = @ResourceTypeId 
              AND ResourceSurrogateId >= @StartId 
              AND ResourceSurrogateId <= @EndId
              AND IsHistory = 0 
              AND IsDeleted = 0";
            }

            LogSqlCommand(sqlCommand);

            int count = 0;
            await _sqlRetryService.ExecuteSql(
                sqlCommand,
                async (cmd, cancel) =>
                {
                    var result = await cmd.ExecuteScalarAsync(cancel);
                    count = Convert.ToInt32(result);
                    return;
                },
                _logger,
                null,
                cancellationToken);

            return count;
        }

        private int GetReindexCommandTimeout()
        {
            return Math.Max((int)_sqlServerDataStoreConfiguration.CommandTimeout.TotalSeconds, 1200);
        }

        private static string GetForceReindexResourceType(SearchOptions searchOptions)
        {
            string resourceType = string.Empty;
            var spe = searchOptions.Expression as SearchParameterExpression;
            if (spe != null && spe.Parameter.Name == KnownQueryParameterNames.Type)
            {
                resourceType = (spe.Expression as StringExpression)?.Value;
            }

            return resourceType;
        }

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

        internal static ICollection<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId)> GetStatsFromCache()
        {
            return _resourceSearchParamStats.GetStatsFromCache();
        }

        internal async Task<IReadOnlyList<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId)>> GetStatsFromDatabase(CancellationToken cancel)
        {
            return await GetStatsFromDatabase(_sqlRetryService, _logger, cancel);
        }

        private static async Task<IReadOnlyList<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId)>> GetStatsFromDatabase(ISqlRetryService sqlRetryService, ILogger<SqlServerSearchService> logger, CancellationToken cancel)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.GetResourceSearchParamStats", CommandType = CommandType.StoredProcedure };
            return await cmd.ExecuteReaderAsync(
                            sqlRetryService,
                            (reader) =>
                            {
                                // ST_Code_WHERE_ResourceTypeId_28_SearchParamId_202
                                var table = reader.GetString(0);
                                var stats = reader.GetString(1);
                                var split = stats.Split("_");
                                var column = split[1];
                                var resorceTypeId = short.Parse(split[4]);
                                var searchParamId = short.Parse(split[6]);
                                return ("dbo." + table, column, resorceTypeId, searchParamId);
                            },
                            logger,
                            cancel);
        }

        private async Task<SearchResult> SearchIncludeImpl(SqlSearchOptions sqlSearchOptions, CancellationToken cancellationToken)
        {
            var includesContinuationToken = IncludesContinuationToken.FromString(sqlSearchOptions.IncludesContinuationToken);
            if (includesContinuationToken == null)
            {
                _logger.LogWarning("Bad Request (InvalidIncludesContinuationToken)");
                throw new BadRequestException(Resources.InvalidIncludesContinuationToken);
            }

            var gteExpression = Expression.GreaterThanOrEqual(
                SqlFieldName.ResourceSurrogateId,
                null,
                includesContinuationToken.MatchResourceSurrogateIdMin);
            var lteExpression = Expression.LessThanOrEqual(
                SqlFieldName.ResourceSurrogateId,
                null,
                includesContinuationToken.MatchResourceSurrogateIdMax);
            var tokenExpression = Expression.And(
                Expression.SearchParameter(SqlSearchParameters.ResourceSurrogateIdParameter, gteExpression),
                Expression.SearchParameter(SqlSearchParameters.ResourceSurrogateIdParameter, lteExpression));
            Expression searchExpression = sqlSearchOptions.Expression == null ? tokenExpression : Expression.And(tokenExpression, sqlSearchOptions.Expression);

            var originalSort = new List<(SearchParameterInfo, SortOrder)>(sqlSearchOptions.Sort);
            var clonedSearchOptions = UpdateSort(sqlSearchOptions, searchExpression);

            if (clonedSearchOptions.CountOnly)
            {
                // if we're only returning a count, discard any _include parameters since included resources are not counted.
                searchExpression = searchExpression?.AcceptVisitor(RemoveIncludesRewriter.Instance);
            }

            // ! - Trace
            SqlRootExpression expression = (SqlRootExpression)CreateDefaultSearchExpression(searchExpression, clonedSearchOptions)
                ?.AcceptVisitor(IncludesOperationRewriter.Instance)
                ?? SqlRootExpression.WithResourceTableExpressions();

            await CreateStats(expression, cancellationToken);

            SearchResult searchResult = null;

            await _sqlRetryService.ExecuteSql(
                async (connection, cancellationToken, sqlException) =>
                {
                    using (SqlCommand sqlCommand = connection.CreateCommand()) // WARNING, this code will not set sqlCommand.Transaction. Sql transactions via C#/.NET are not supported in this method.
                    {
                        sqlCommand.CommandTimeout = (int)_sqlServerDataStoreConfiguration.CommandTimeout.TotalSeconds;

                        var exportTimeTravel = clonedSearchOptions.QueryHints != null && ContainsGlobalEndSurrogateId(clonedSearchOptions);
                        if (exportTimeTravel)
                        {
                            PopulateSqlCommandFromQueryHints(clonedSearchOptions, sqlCommand);
                            sqlCommand.CommandTimeout = 1200; // set to 20 minutes, as dataset is usually large
                        }
                        else
                        {
                            var stringBuilder = new IndentedStringBuilder(new StringBuilder());

                            EnableTimeAndIoMessageLogging(stringBuilder, connection);

                            var queryGenerator = new SqlQueryGenerator(
                                stringBuilder,
                                new HashingSqlQueryParameterManager(new SqlQueryParameterManager(sqlCommand.Parameters)),
                                _model,
                                _schemaInformation,
                                _queryGeneratorFactory,
                                _reuseQueryPlans.IsEnabled(_sqlRetryService),
                                sqlSearchOptions.IsAsyncOperation,
                                sqlException);

                            expression.AcceptVisitor(queryGenerator, clonedSearchOptions);

                            SqlCommandSimplifier.RemoveRedundantParameters(stringBuilder, sqlCommand.Parameters, _logger);

                            var queryText = stringBuilder.ToString();
                            var queryHash = _queryHashCalculator.CalculateHash(queryText);
                            _logger.LogInformation("SQL Search Service query hash: {QueryHash}", queryHash);
                            var customQuery = CustomQueries.CheckQueryHash(connection, queryHash, _logger);

                            if (!string.IsNullOrEmpty(customQuery))
                            {
                                _logger.LogInformation("SQl Search Service, custom Query identified by hash {QueryHash}, {CustomQuery}", queryHash, customQuery);
                                queryText = customQuery;
                                sqlCommand.CommandType = CommandType.StoredProcedure;
                            }

                            // Command text contains no direct user input.
#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
                            sqlCommand.CommandText = queryText;
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities
                        }

                        LogSqlCommand(sqlCommand);

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

                            var moreResults = false;
                            var resources = new List<SearchResultEntry>(sqlSearchOptions.IncludeCount);

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

                                if (resources.Count < clonedSearchOptions.IncludeCount)
                                {
                                    var rawResource = new Lazy<string>(() =>
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

                                    resources.Add(new SearchResultEntry(
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
                                        SearchEntryMode.Include));
                                }
                                else
                                {
                                    moreResults = true;
                                }
                            }

                            // call NextResultAsync to get the info messages
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
                                        _model.GetResourceTypeId(resources[^1].Resource.ResourceTypeName),
                                        resources[^1].Resource.ResourceSurrogateId,
                                    });
                            }

                            searchResult = new SearchResult(
                                resources,
                                null,
                                originalSort,
                                clonedSearchOptions.UnsupportedSearchParams,
                                null,
                                nextIncludesContinuationToken?.ToJson());
                        }
                    }
                },
                _logger,
                cancellationToken,
                true); // this enables reads from replicas

            return searchResult;
        }

        private SqlRootExpression CreateDefaultSearchExpression(Expression rootExpression, SqlSearchOptions searchOptions)
        {
            return (SqlRootExpression)rootExpression
                ?.AcceptVisitor(LastUpdatedToResourceSurrogateIdRewriter.Instance)
                .AcceptVisitor(_compartmentSearchRewriter)
                .AcceptVisitor(_smartCompartmentSearchRewriter)
                .AcceptVisitor(DateTimeEqualityRewriter.Instance)
                .AcceptVisitor(FlatteningRewriter.Instance)
                .AcceptVisitor(UntypedReferenceRewriter.Instance)
                .AcceptVisitor(_sqlRootExpressionRewriter)
                .AcceptVisitor(DateTimeTableExpressionCombiner.Instance)
                .AcceptVisitor(_partitionEliminationRewriter)
                .AcceptVisitor(_sortRewriter, searchOptions)
                .AcceptVisitor(SearchParamTableExpressionReorderer.Instance)
                .AcceptVisitor(MissingSearchParamVisitor.Instance)
                .AcceptVisitor(NotExpressionRewriter.Instance)
                .AcceptVisitor(_chainFlatteningRewriter)
                .AcceptVisitor(ResourceColumnPredicatePushdownRewriter.Instance)
                .AcceptVisitor(DateTimeBoundedRangeRewriter.Instance)
                .AcceptVisitor(
                    (SqlExpressionRewriterWithInitialContext<object>)(_schemaInformation.Current >= SchemaVersionConstants.PartitionedTables
                        ? StringOverflowRewriter.Instance
                        : LegacyStringOverflowRewriter.Instance))
                .AcceptVisitor(NumericRangeRewriter.Instance)
                .AcceptVisitor(IncludeMatchSeedRewriter.Instance)
                .AcceptVisitor(TopRewriter.Instance, searchOptions);
        }

        /// <summary>
        /// Attempts to handle simple resource read requests directly via FhirDataStore.GetAsync
        /// instead of generating SQL queries for better performance.
        /// </summary>
        /// <param name="expression">The SQL root expression to analyze</param>
        /// <param name="searchOptions">Search options for the request</param>
        /// <param name="fhirDataStore">The FHIR data store instance</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>SearchResult if this was a simple resource read, null otherwise</returns>
        private static async Task<SearchResult> TryHandleSimpleResourceReadAsync(SqlRootExpression expression, SqlSearchOptions searchOptions, IFhirDataStore fhirDataStore, CancellationToken cancellationToken)
        {
            // Only optimize for non-count queries without sorting or includes operations
            if (searchOptions.CountOnly ||
                searchOptions.IncludeTotal != TotalType.None ||
                searchOptions.IsIncludesOperation)
            {
                return null;
            }

            // Only optimize for Latest or History-inclusive searches (versioned reads)
            if (searchOptions.ResourceVersionTypes != ResourceVersionType.Latest &&
                !searchOptions.ResourceVersionTypes.HasFlag(ResourceVersionType.History))
            {
                return null;
            }

            // Ensure we have exactly two resource table expressions (one for resource type, one for resource ID)
            if (expression.ResourceTableExpressions.Count != 2 ||
                expression.SearchParamTableExpressions.Count > 0)
            {
                return null;
            }

            // Extract resource type and ID from the expressions
            if (!TryExtractResourceTypeAndId(expression, out string resourceType, out string resourceId, out string versionId))
            {
                return null;
            }

            // If requesting history, but not specific id, then this is not a simple read.
            if (searchOptions.ResourceVersionTypes.HasFlag(ResourceVersionType.History) && string.IsNullOrEmpty(versionId))
            {
                return null;
            }

            try
            {
                // Create ResourceKey with proper version handling
                var resourceKey = string.IsNullOrEmpty(versionId)
                    ? new ResourceKey(resourceType, resourceId)
                    : new ResourceKey(resourceType, resourceId, versionId);

                // Use FhirDataStore.GetAsync to retrieve the resource
                var resourceWrapper = await fhirDataStore.GetAsync(resourceKey, cancellationToken);

                if (resourceWrapper == null)
                {
                    // Resource not found - return empty search result
                    return new SearchResult(0, searchOptions.UnsupportedSearchParams);
                }

                // Convert to SearchResultEntry
                var searchResultEntry = new SearchResultEntry(resourceWrapper, SearchEntryMode.Match);
                var results = new List<SearchResultEntry> { searchResultEntry };

                return new SearchResult(results, null, null, searchOptions.UnsupportedSearchParams);
            }
            catch
            {
                // If anything goes wrong, fall back to SQL generation
                return null;
            }
        }

        /// <summary>
        /// Attempts to extract resource type and ID from a simple resource read expression.
        /// </summary>
        /// <param name="expression">The SQL root expression to analyze</param>
        /// <param name="resourceType">The extracted resource type</param>
        /// <param name="resourceId">The extracted resource ID</param>
        /// <param name="versionId">The extracted version ID (if any)</param>
        /// <returns>True if extraction was successful, false otherwise</returns>
        private static bool TryExtractResourceTypeAndId(SqlRootExpression expression, out string resourceType, out string resourceId, out string versionId)
        {
            resourceType = null;
            resourceId = null;
            versionId = null;

            // Find resource type and ID expressions in ResourceTableExpressions
            SearchParameterExpression resourceTypeExpression = null;
            SearchParameterExpression resourceIdExpression = null;

            foreach (var searchParamExpression in expression.ResourceTableExpressions.OfType<SearchParameterExpression>())
            {
                if (searchParamExpression.Parameter.Name == SearchParameterNames.ResourceType)
                {
                    resourceTypeExpression = searchParamExpression;
                }
                else if (searchParamExpression.Parameter.Name == SearchParameterNames.Id)
                {
                    resourceIdExpression = searchParamExpression;
                }
            }

            if (resourceTypeExpression == null || resourceIdExpression == null)
            {
                return false;
            }

            // Extract simple string values
            if (!TryExtractSimpleStringValue(resourceTypeExpression.Expression, out resourceType) ||
                !TryExtractSimpleStringValue(resourceIdExpression.Expression, out string fullResourceId))
            {
                return false;
            }

            // Parse version from ID if present (format: "Patient/123/_history/456")
            if (fullResourceId.Contains("/_history/", StringComparison.Ordinal))
            {
                var parts = fullResourceId.Split(["/_history/"], StringSplitOptions.None);
                if (parts.Length == 2)
                {
                    resourceId = parts[0];
                    versionId = parts[1];
                }
                else
                {
                    resourceId = fullResourceId;
                }
            }
            else
            {
                resourceId = fullResourceId;
            }

            return !string.IsNullOrEmpty(resourceType) && !string.IsNullOrEmpty(resourceId);
        }

        private class ResourceSearchParamStats
        {
            private readonly ConcurrentDictionary<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId), bool> _stats;

            public ResourceSearchParamStats(ISqlRetryService sqlRetryService, ILogger<SqlServerSearchService> logger, CancellationToken cancel)
            {
                _stats = new ConcurrentDictionary<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId), bool>();
                Init(sqlRetryService, logger, cancel).Wait(cancel);
            }

            public ICollection<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId)> GetStatsFromCache()
            {
                return _stats.Keys;
            }

            // The goal is not to be 100% accurate, but cover majority of simple cases and not crash in the others.
            // Simple expressions with one or more resource types are handled. For chains, resource types are derived from predecessor.
            // Composite searches are skipped. Number of handled cases can be extended.
            public async Task Create(SqlRootExpression expression, ISqlRetryService sqlRetryService, ILogger<SqlServerSearchService> logger, SqlServerFhirModel model, CancellationToken cancel)
            {
                for (var index = 0; index < expression.SearchParamTableExpressions.Count; index++)
                {
                    var tableExpression = expression.SearchParamTableExpressions[index];
                    if (tableExpression.Kind != SearchParamTableExpressionKind.Normal)
                    {
                        continue;
                    }

                    var table = tableExpression.QueryGenerator.Table.TableName;
                    var columns = GetKeyColumns(table);
                    if (columns.Count == 0)
                    {
                        return;
                    }

                    var searchParamId = (short)0;
                    var resourceTypeIds = new HashSet<short>();
                    if (tableExpression.ChainLevel == 0 && tableExpression.Predicate is MultiaryExpression multiExp)
                    {
                        foreach (var part in multiExp.Expressions)
                        {
                            if (part is SearchParameterExpression parameterExp)
                            {
                                if (parameterExp.Parameter.Name == SearchParameterNames.ResourceType)
                                {
                                    if (parameterExp.Expression is StringExpression stringExp)
                                    {
                                        if (model.TryGetResourceTypeId(stringExp.Value, out var resourceTypeId))
                                        {
                                            resourceTypeIds.Add(resourceTypeId);
                                        }
                                    }
                                    else if (parameterExp.Expression is MultiaryExpression multiExp2)
                                    {
                                        foreach (var part2 in multiExp2.Expressions)
                                        {
                                            if (part2 is SearchParameterExpression parameterExp2)
                                            {
                                                if (parameterExp2.Expression is StringExpression stringExp2)
                                                {
                                                    if (model.TryGetResourceTypeId(stringExp2.Value, out var resourceTypeId))
                                                    {
                                                        resourceTypeIds.Add(resourceTypeId);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                else if (parameterExp.Parameter.Name != SqlSearchParameters.PrimaryKeyParameterName && parameterExp.Parameter.Name != SqlSearchParameters.ResourceSurrogateIdParameterName)
                                {
                                    model.TryGetSearchParamId(parameterExp.Parameter.Url, out searchParamId);
                                }
                            }
                        }
                    }

                    if (tableExpression.ChainLevel == 1 && tableExpression.Predicate is SearchParameterExpression searchExpression)
                    {
                        searchParamId = model.GetSearchParamId(searchExpression.Parameter.Url);
                        var priorTableExpression = expression.SearchParamTableExpressions[index - 1];
                        if (priorTableExpression.Kind == SearchParamTableExpressionKind.Chain)
                        {
                            foreach (var type in ((SqlChainLinkExpression)priorTableExpression.Predicate).ResourceTypes)
                            {
                                if (model.TryGetResourceTypeId(type, out var resourceTypeId))
                                {
                                    resourceTypeIds.Add(resourceTypeId);
                                }
                            }
                        }
                    }

                    if (searchParamId != 0)
                    {
                        foreach (var resourceTypeId in resourceTypeIds)
                        {
                            foreach (var column in columns)
                            {
                                await Create(table, column, resourceTypeId, searchParamId, sqlRetryService, logger, cancel);
                            }
                        }
                    }
                }
            }

            private static HashSet<string> GetKeyColumns(string table)
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

                return results;
            }

            private async Task Create(string tableName, string columnName, short resourceTypeId, short searchParamId, ISqlRetryService sqlRetryService, ILogger<SqlServerSearchService> logger, CancellationToken cancel)
            {
                if (_stats.ContainsKey((tableName, columnName, resourceTypeId, searchParamId)))
                {
                    logger.LogInformation("ResourceSearchParamStats.FoundInCache Table={Table} Column={Column} Type={ResourceType} Param={SearchParam}", tableName, columnName, resourceTypeId, searchParamId);
                    return;
                }

                try
                {
                    using var cmd = new SqlCommand() { CommandText = "dbo.CreateResourceSearchParamStats", CommandType = CommandType.StoredProcedure };
                    cmd.Parameters.AddWithValue("@Table", tableName[4..]); // remove dbo.
                    cmd.Parameters.AddWithValue("@Column", columnName);
                    cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
                    cmd.Parameters.AddWithValue("@SearchParamId", searchParamId);
                    await cmd.ExecuteNonQueryAsync(sqlRetryService, logger, cancel);

                    _stats.TryAdd((tableName, columnName, resourceTypeId, searchParamId), true);

                    logger.LogInformation("ResourceSearchParamStats.CreateStats.Completed Table={Table} Column={Column} Type={ResourceType} Param={SearchParam}", tableName, columnName, resourceTypeId, searchParamId);
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

        // Class copied from src\Microsoft.Health.Fhir.SqlServer\Features\Schema\Model\VLatest.Generated.net7.0.cs .
        private class GetResourceSurrogateIdRangesProcedure : StoredProcedure
        {
            private readonly ParameterDefinition<short> _resourceTypeId = new ParameterDefinition<short>("@ResourceTypeId", global::System.Data.SqlDbType.SmallInt, false);
            private readonly ParameterDefinition<long> _startId = new ParameterDefinition<long>("@StartId", global::System.Data.SqlDbType.BigInt, false);
            private readonly ParameterDefinition<long> _endId = new ParameterDefinition<long>("@EndId", global::System.Data.SqlDbType.BigInt, false);
            private readonly ParameterDefinition<int> _rangeSize = new ParameterDefinition<int>("@RangeSize", global::System.Data.SqlDbType.Int, false);
            private readonly ParameterDefinition<int?> _numberOfRanges = new ParameterDefinition<int?>("@NumberOfRanges", global::System.Data.SqlDbType.Int, true);
            private readonly ParameterDefinition<bool?> _up = new ParameterDefinition<bool?>("@Up", global::System.Data.SqlDbType.Bit, true);
            private readonly ParameterDefinition<bool?> _activeOnly = new ParameterDefinition<bool?>("@ActiveOnly", global::System.Data.SqlDbType.Bit, true);

            internal GetResourceSurrogateIdRangesProcedure()
                : base("dbo.GetResourceSurrogateIdRanges")
            {
            }

            public void PopulateCommand(SqlCommand sqlCommand, short resourceTypeId, long startId, long endId, int rangeSize, int? numberOfRanges, bool? up, bool? activeOnly, SchemaInformation schemaInformation)
            {
                sqlCommand.CommandType = global::System.Data.CommandType.StoredProcedure;
                sqlCommand.CommandText = "dbo.GetResourceSurrogateIdRanges";
                _resourceTypeId.AddParameter(sqlCommand.Parameters, resourceTypeId);
                _startId.AddParameter(sqlCommand.Parameters, startId);
                _endId.AddParameter(sqlCommand.Parameters, endId);
                _rangeSize.AddParameter(sqlCommand.Parameters, rangeSize);
                _numberOfRanges.AddParameter(sqlCommand.Parameters, numberOfRanges);
                _up.AddParameter(sqlCommand.Parameters, up);

                if (schemaInformation.Current >= (int)SchemaVersion.V97)
                {
                    _activeOnly.AddParameter(sqlCommand.Parameters, activeOnly);
                }
            }
        }
    }
}
