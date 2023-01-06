// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
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
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Storage;
using static Microsoft.Health.Fhir.SqlServer.Features.Storage.SqlRetryService;
using SortOrder = Microsoft.Health.Fhir.Core.Features.Search.SortOrder;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal class SqlServerSearchService : SearchService
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SqlRootExpressionRewriter _sqlRootExpressionRewriter;

        private readonly SortRewriter _sortRewriter;
        private readonly PartitionEliminationRewriter _partitionEliminationRewriter;
        private readonly CompartmentSearchRewriter _compartmentSearchRewriter;
        private readonly SmartCompartmentSearchRewriter _smartCompartmentSearchRewriter;
        private readonly ChainFlatteningRewriter _chainFlatteningRewriter;
        private readonly ILogger<SqlServerSearchService> _logger;
        private readonly BitColumn _isMatch = new BitColumn("IsMatch");
        private readonly BitColumn _isPartial = new BitColumn("IsPartial");
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ISqlRetryService _sqlRetryService;
        private const string SortValueColumnName = "SortValue";
        private readonly SchemaInformation _schemaInformation;
        private readonly ICompressedRawResourceConverter _compressedRawResourceConverter;
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private const int _defaultNumberOfColumnsReadFromResult = 11;
        private readonly SearchParameterInfo _fakeLastUpdate = new SearchParameterInfo(SearchParameterNames.LastUpdated, SearchParameterNames.LastUpdated);

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
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ISqlRetryService sqlRetryService,
            SchemaInformation schemaInformation,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            ICompressedRawResourceConverter compressedRawResourceConverter,
            ILogger<SqlServerSearchService> logger)
            : base(searchOptionsFactory, fhirDataStore)
        {
            EnsureArg.IsNotNull(sqlRootExpressionRewriter, nameof(sqlRootExpressionRewriter));
            EnsureArg.IsNotNull(chainFlatteningRewriter, nameof(chainFlatteningRewriter));
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(partitionEliminationRewriter, nameof(partitionEliminationRewriter));
            EnsureArg.IsNotNull(compartmentSearchRewriter, nameof(compartmentSearchRewriter));
            EnsureArg.IsNotNull(smartCompartmentSearchRewriter, nameof(smartCompartmentSearchRewriter));
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _model = model;
            _sqlRootExpressionRewriter = sqlRootExpressionRewriter;
            _sortRewriter = sortRewriter;
            _partitionEliminationRewriter = partitionEliminationRewriter;
            _compartmentSearchRewriter = compartmentSearchRewriter;
            _smartCompartmentSearchRewriter = smartCompartmentSearchRewriter;
            _chainFlatteningRewriter = chainFlatteningRewriter;
            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _sqlRetryService = sqlRetryService;
            _logger = logger;

            _schemaInformation = schemaInformation;
            _requestContextAccessor = requestContextAccessor;
            _compressedRawResourceConverter = compressedRawResourceConverter;
        }

        public override async Task<SearchResult> SearchAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            SqlSearchOptions sqlSearchOptions = new SqlSearchOptions(searchOptions);
            SearchResult searchResult = await SearchImpl(sqlSearchOptions, SqlSearchType.Default, null, cancellationToken);
            int resultCount = searchResult.Results.Count();
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
                    (sqlSearchOptions.Sort[0].sortOrder == SortOrder.Descending && sqlSearchOptions.DidWeSearchForSortValue.HasValue && sqlSearchOptions.DidWeSearchForSortValue.Value))
                {
                    if (sqlSearchOptions.MaxItemCount - resultCount == 0)
                    {
                        // Since we are already returning MaxItemCount number of resources we don't want
                        // to execute another search right now just to drop all the resources. We will return
                        // a "special" ct so that we the subsequent request will be handled correctly.
                        var ct = new ContinuationToken(new object[]
                            {
                                    SqlSearchConstants.SortSentinelValueForCt,
                                    0,
                            });

                        searchResult = new SearchResult(searchResult.Results, ct.ToJson(), searchResult.SortOrder, searchResult.UnsupportedSearchParameters);
                    }
                    else
                    {
                        var finalResultsInOrder = new List<SearchResultEntry>();
                        finalResultsInOrder.AddRange(searchResult.Results);
                        sqlSearchOptions.SortQuerySecondPhase = true;
                        sqlSearchOptions.MaxItemCount -= resultCount;

                        searchResult = await SearchImpl(sqlSearchOptions, SqlSearchType.Default, null, cancellationToken);

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
                        var countOnlySearchResult = await SearchImpl(sqlSearchOptions, SqlSearchType.Default, null, cancellationToken);

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

        protected override async Task<SearchResult> SearchHistoryInternalAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            SqlSearchOptions sqlSearchOptions = new SqlSearchOptions(searchOptions);
            return await SearchImpl(sqlSearchOptions, SqlSearchType.History, null, cancellationToken);
        }

        private async Task<SearchResult> SearchImpl(SqlSearchOptions sqlSearchOptions, SqlSearchType searchType, string currentSearchParameterHash, CancellationToken cancellationToken)
        {
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

                        Expression lastUpdatedExpression = null;
                        if (!optimize)
                        {
                            lastUpdatedExpression = Expression.GreaterThan(fieldName, null, keyValue);
                        }
                        else
                        {
                            if (sortOrder == SortOrder.Ascending)
                            {
                                lastUpdatedExpression = Expression.GreaterThan(fieldName, null, keyValue);
                            }
                            else
                            {
                                lastUpdatedExpression = Expression.LessThan(fieldName, null, keyValue);
                            }
                        }

                        var tokenExpression = Expression.SearchParameter(parameter, lastUpdatedExpression);
                        searchExpression = searchExpression == null ? tokenExpression : Expression.And(tokenExpression, searchExpression);
                    }
                }
                else
                {
                    throw new BadRequestException(Resources.InvalidContinuationToken);
                }
            }

            var originalSort = new List<(SearchParameterInfo, SortOrder)>(sqlSearchOptions.Sort);
            SqlSearchOptions clonedSearchOptions = UpdateSort(sqlSearchOptions, searchExpression, searchType);

            if (clonedSearchOptions.CountOnly)
            {
                // if we're only returning a count, discard any _include parameters since included resources are not counted.
                searchExpression = searchExpression?.AcceptVisitor(RemoveIncludesRewriter.Instance);
            }

            SqlRootExpression expression = (SqlRootExpression)searchExpression
                                               ?.AcceptVisitor(LastUpdatedToResourceSurrogateIdRewriter.Instance)
                                               .AcceptVisitor(_compartmentSearchRewriter)
                                               .AcceptVisitor(_smartCompartmentSearchRewriter)
                                               .AcceptVisitor(DateTimeEqualityRewriter.Instance)
                                               .AcceptVisitor(FlatteningRewriter.Instance)
                                               .AcceptVisitor(UntypedReferenceRewriter.Instance)
                                               .AcceptVisitor(_sqlRootExpressionRewriter)
                                               .AcceptVisitor(_partitionEliminationRewriter)
                                               .AcceptVisitor(_sortRewriter, clonedSearchOptions)
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
                                               .AcceptVisitor(TopRewriter.Instance, clonedSearchOptions)
                                               .AcceptVisitor(IncludeRewriter.Instance)
                                           ?? SqlRootExpression.WithResourceTableExpressions();

            using (SqlCommand sqlCommand = new SqlCommand()) // WARNING, this code will not set sqlCommand.Transaction. Sql transactions via C#/.NET are not supported in this method.
            {
                var exportTimeTravel = clonedSearchOptions.QueryHints != null && _schemaInformation.Current >= SchemaVersionConstants.ExportTimeTravel;
                if (exportTimeTravel)
                {
                    PopulateSqlCommandFromQueryHints(clonedSearchOptions, sqlCommand);
                    sqlCommand.CommandTimeout = 1200; // set to 20 minutes, as dataset is usually large // TODO: read from same place where other timeout is read from (settings?)
                }
                else
                {
                    var stringBuilder = new IndentedStringBuilder(new StringBuilder());

                    EnableTimeAndIoMessageLogging(stringBuilder);

                    var queryGenerator = new SqlQueryGenerator(
                        stringBuilder,
                        new HashingSqlQueryParameterManager(new SqlQueryParameterManager(sqlCommand.Parameters)),
                        _model,
                        searchType,
                        _schemaInformation,
                        currentSearchParameterHash);

                    expression.AcceptVisitor(queryGenerator, clonedSearchOptions);

                    SqlCommandSimplifier.RemoveRedundantParameters(stringBuilder, sqlCommand.Parameters, _logger);

#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
                    sqlCommand.CommandText = stringBuilder.ToString();
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities
                }

                LogSqlCommand(sqlCommand);

                if (clonedSearchOptions.CountOnly)
                {
                    var countResult = await _sqlRetryService.ProcessSqlDataReaderRowsWithRetries(
                        sqlCommand,
                        GetSqlConnectionInitializer(),
                        () => new CountResult(),
                        (SqlDataReader reader, CountResult result, CancellationToken cancellationToken) =>
                        {
                            result.Count = reader.GetInt64(0);
                            if (result.Count > int.MaxValue)
                            {
                                _requestContextAccessor.RequestContext.BundleIssues.Add(
                                    new OperationOutcomeIssue(
                                        OperationOutcomeConstants.IssueSeverity.Error,
                                        OperationOutcomeConstants.IssueType.NotSupported,
                                        string.Format(Core.Resources.SearchCountResultsExceedLimit, result.Count, int.MaxValue)));

                                throw new InvalidSearchOperationException(string.Format(Core.Resources.SearchCountResultsExceedLimit, result.Count, int.MaxValue));
                            }

                            return Task.FromResult(false); // Only first row.
                        },
                        cancellationToken);

                    return new SearchResult((int)countResult.Count, clonedSearchOptions.UnsupportedSearchParams);
                }

                RowProcessorResult result = await _sqlRetryService.ProcessSqlDataReaderRowsWithRetries(
                    sqlCommand,
                    GetSqlConnectionInitializer(),
                    () =>
                    {
                        var r = new RowProcessorResult();
                        r.MatchCount = 0;
                        r.NumberOfColumnsRead = 0;
                        r.ClonedSearchOptions = clonedSearchOptions;
                        r.MoreResults = false;
                        r.NewContinuationType = null;
                        r.NewContinuationId = null;
                        r.SortValue = null;
                        r.IsResultPartial = false;
                        r.Resources = new List<SearchResultEntry>(sqlSearchOptions.MaxItemCount);
                        return r;
                    },
                    ProcessRow,
                    cancellationToken);

                ContinuationToken continuationToken =
                    result.MoreResults && !exportTimeTravel // with query hints all results are returned on single page
                        ? new ContinuationToken(
                            clonedSearchOptions.Sort.Select(s =>
                                s.searchParameterInfo.Name switch
                                {
                                    SearchParameterNames.ResourceType => (object)result.NewContinuationType,
                                    SearchParameterNames.LastUpdated => result.NewContinuationId,
                                    _ => result.SortValue,
                                }).ToArray())
                        : null;

                if (result.IsResultPartial)
                {
                    _requestContextAccessor.RequestContext.BundleIssues.Add(
                        new OperationOutcomeIssue(
                            OperationOutcomeConstants.IssueSeverity.Warning,
                            OperationOutcomeConstants.IssueType.Incomplete,
                            Core.Resources.TruncatedIncludeMessage));
                }

                // If this is a sort query, lets keep track of whether we actually searched for sort values.
                if (clonedSearchOptions.Sort != null &&
                    clonedSearchOptions.Sort.Count > 0 &&
                    clonedSearchOptions.Sort[0].searchParameterInfo.Code != KnownQueryParameterNames.LastUpdated)
                {
                    sqlSearchOptions.DidWeSearchForSortValue = result.NumberOfColumnsRead > _defaultNumberOfColumnsReadFromResult;
                }

                // This value is set inside the SortRewriter. If it is set, we need to pass
                // this value back to the caller.
                if (clonedSearchOptions.IsSortWithFilter)
                {
                    sqlSearchOptions.IsSortWithFilter = true;
                }

                return new SearchResult(result.Resources, continuationToken?.ToJson(), originalSort, clonedSearchOptions.UnsupportedSearchParams);
            }
        }

        private async Task<bool> ProcessRow(SqlDataReader reader, RowProcessorResult result, CancellationToken cancellationToken)
        {
            PopulateResourceTableColumnsToRead(
                reader,
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
                out Stream rawResourceStream);
            result.NumberOfColumnsRead = reader.FieldCount;

            string rawResource;
            await using (rawResourceStream)
            {
                // If we get to this point, we know there are more results so we need a continuation token
                // Additionally, this resource shouldn't be included in the results
                if (result.MatchCount >= result.ClonedSearchOptions.MaxItemCount && isMatch)
                {
                    result.MoreResults = true;

                    return true;
                }

                rawResource = _compressedRawResourceConverter.ReadCompressedRawResource(rawResourceStream);
            }

            if (string.IsNullOrEmpty(rawResource))
            {
                rawResource = MissingResourceFactory.CreateJson(resourceId, _model.GetResourceTypeName(resourceTypeId), "warning", "incomplete");
                _requestContextAccessor.SetMissingResourceCode(System.Net.HttpStatusCode.PartialContent);
            }

            // See if this resource is a continuation token candidate and increase the count
            if (isMatch)
            {
                result.NewContinuationType = resourceTypeId;
                result.NewContinuationId = resourceSurrogateId;

                // For normal queries, we select _defaultNumberOfColumnsReadFromResult number of columns.
                // If we have more, that means we have an extra column tracking sort value.
                // Keep track of sort value if this is the last row.
                if (result.MatchCount == result.ClonedSearchOptions.MaxItemCount - 1 && reader.FieldCount > _defaultNumberOfColumnsReadFromResult)
                {
                    var tempSortValue = reader.GetValue(SortValueColumnName);
                    if ((tempSortValue as DateTime?) != null)
                    {
                        result.SortValue = (tempSortValue as DateTime?).Value.ToString("o");
                    }
                    else
                    {
                        result.SortValue = tempSortValue.ToString();
                    }
                }

                result.MatchCount++;
            }

            // as long as at least one entry was marked as partial, this resultset
            // should be marked as partial
            result.IsResultPartial = result.IsResultPartial || isPartialEntry;

            result.Resources.Add(new SearchResultEntry(
                new ResourceWrapper(
                    resourceId,
                    version.ToString(CultureInfo.InvariantCulture),
                    _model.GetResourceTypeName(resourceTypeId),
                    new RawResource(rawResource, FhirResourceFormat.Json, isMetaSet: isRawResourceMetaSet),
                    new ResourceRequest(requestMethod),
                    new DateTimeOffset(ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(resourceSurrogateId), TimeSpan.Zero),
                    isDeleted,
                    null,
                    null,
                    null,
                    searchParameterHash),
                isMatch ? SearchEntryMode.Match : SearchEntryMode.Include));
            return true;
        }

        /*private void PopulateSqlCommandFromQueryHints(SqlSearchOptions options, SqlCommandWrapper cmd)
        {
            var hints = options.QueryHints;
            var type = _model.GetResourceTypeId(hints.First(_ => _.Param == KnownQueryParameterNames.Type).Value);
            var startId = long.Parse(hints.First(_ => _.Param == KnownQueryParameterNames.StartSurrogateId).Value);
            var endId = long.Parse(hints.First(_ => _.Param == KnownQueryParameterNames.EndSurrogateId).Value);
            var globalStartId = long.Parse(hints.First(_ => _.Param == KnownQueryParameterNames.GlobalStartSurrogateId).Value);
            var globalEndId = long.Parse(hints.First(_ => _.Param == KnownQueryParameterNames.GlobalEndSurrogateId).Value);
            VLatest.GetResourcesByTypeAndSurrogateIdRange.PopulateCommand(cmd, type, startId, endId, globalStartId, globalEndId);
        }*/

        private void PopulateSqlCommandFromQueryHints(SqlSearchOptions options, SqlCommand command)
        {
            var hints = options.QueryHints;
            var type = _model.GetResourceTypeId(hints.First(_ => _.Param == KnownQueryParameterNames.Type).Value);
            var startId = long.Parse(hints.First(_ => _.Param == KnownQueryParameterNames.StartSurrogateId).Value);
            var endId = long.Parse(hints.First(_ => _.Param == KnownQueryParameterNames.EndSurrogateId).Value);
            var globalStartId = long.Parse(hints.First(_ => _.Param == KnownQueryParameterNames.GlobalStartSurrogateId).Value);
            var globalEndId = long.Parse(hints.First(_ => _.Param == KnownQueryParameterNames.GlobalEndSurrogateId).Value);
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "dbo.GetResourcesByTypeAndSurrogateIdRange";
            command.Parameters.AddWithValue("@ResourceTypeId", type);
            command.Parameters.AddWithValue("@StartId", startId);
            command.Parameters.AddWithValue("@EndId", endId);
            command.Parameters.AddWithValue("@GlobalStartId", globalStartId);
            command.Parameters.AddWithValue("@GlobalEndId", globalEndId);
        }

        /// <summary>
        /// Searches for resources by their type and surrogate id
        /// </summary>
        /// <param name="resourceType">The resource type to search</param>
        /// <param name="startId">The lower bound for surrogate ids to find</param>
        /// <param name="endId">The upper bound for surrogate ids to find</param>
        /// <param name="windowStartId">The lower bound for the window of time to consider for historical records</param>
        /// <param name="windowEndId">The upper bound for the window of time to consider for historical records</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>All resources with surrogate ids greater than or equal to startId and less than endId. If windowEndId is set it will return the most recent version of a resource that was created before windowEndId that is within the range of startId to endId.</returns>
        public async Task<SearchResult> SearchBySurrogateIdRange(string resourceType, long startId, long endId, long? windowStartId, long? windowEndId, CancellationToken cancellationToken)
        {
            var resourceTypeId = _model.GetResourceTypeId(resourceType);
            try
            {
                using SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
                using SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();

                VLatest.GetResourcesByTypeAndSurrogateIdRange.PopulateCommand(sqlCommandWrapper, resourceTypeId, startId, endId, windowStartId, windowEndId);
                try
                {
                    using SqlDataReader reader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                    var resources = new List<SearchResultEntry>();
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        PopulateResourceTableColumnsToRead(
                            reader,
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
                            out Stream rawResourceStream);

                        string rawResource;
                        using (rawResourceStream)
                        {
                            rawResource = _compressedRawResourceConverter.ReadCompressedRawResource(rawResourceStream);
                        }

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
                                new DateTimeOffset(ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(resourceSurrogateId), TimeSpan.Zero),
                                isDeleted,
                                null,
                                null,
                                null,
                                searchParameterHash),
                            isMatch ? SearchEntryMode.Match : SearchEntryMode.Include));
                    }

                    return new SearchResult(resources, null, null, new List<Tuple<string, string>>());
                }
                catch (SqlException)
                {
                    throw;
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        public override async Task<IReadOnlyList<(long StartId, long EndId)>> GetSurrogateIdRanges(string resourceType, long startId, long endId, int rangeSize, int numberOfRanges, bool up, CancellationToken cancellationToken)
        {
            var resourceTypeId = _model.GetResourceTypeId(resourceType);
            var ranges = new List<(long start, long end)>(numberOfRanges);
            using SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
            using SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();
            sqlCommandWrapper.CommandTimeout = 1200; // Should be >> average execution time which for 100K resources is ~3 minutes.
            VLatest.GetResourceSurrogateIdRanges.PopulateCommand(sqlCommandWrapper, resourceTypeId, startId, endId, rangeSize, numberOfRanges, up);
            using SqlDataReader reader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ranges.Add((reader.GetInt64(1), reader.GetInt64(2)));
            }

            return ranges;
        }

        public override long GetSurrogateId(DateTime dateTime)
        {
            return ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(dateTime);
        }

        public override async Task<IReadOnlyList<(short ResourceTypeId, string Name)>> GetUsedResourceTypes(CancellationToken cancellationToken)
        {
            var resourceTypes = new List<(short ResourceTypeId, string Name)>();
            using SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
            using SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();
            VLatest.GetUsedResourceTypes.PopulateCommand(sqlCommandWrapper);
            using SqlDataReader reader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                resourceTypes.Add((reader.GetInt16(0), reader.GetString(1)));
            }

            return resourceTypes;
        }

        /// <summary>
        /// If no sorting fields are specified, sets the sorting fields to the primary key. (This is either ResourceSurrogateId or ResourceTypeId, ResourceSurrogateId).
        /// If sorting only by ResourceTypeId, adds in ResourceSurrogateId as the second sort column.
        /// If sorting by ResourceSurrogateId and using partitioned tables and searching over a single type, sets the sort to ResourceTypeId, ResourceSurrogateId
        /// </summary>
        /// <param name="searchOptions">The input SearchOptions</param>
        /// <param name="searchExpression">The searchExpression</param>
        /// <param name="sqlSearchType">The type of search being performed</param>
        /// <returns>If the sort needs to be updated, a new <see cref="SearchOptions"/> instance, otherwise, the same instance as <paramref name="searchOptions"/></returns>
        private SqlSearchOptions UpdateSort(SqlSearchOptions searchOptions, Expression searchExpression, SqlSearchType sqlSearchType)
        {
            SqlSearchOptions newSearchOptions = searchOptions;
            if (sqlSearchType == SqlSearchType.History)
            {
                // history is always sorted by _lastUpdated.
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

        private void PopulateResourceTableColumnsToRead(
            SqlDataReader reader,
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
            out Stream rawResourceStream)
        {
            searchParameterHash = null;

            if (_schemaInformation.Current >= SchemaVersionConstants.SearchParameterHashSchemaVersion)
            {
                (resourceTypeId, resourceId, version, isDeleted, resourceSurrogateId, requestMethod, isMatch, isPartialEntry,
                    isRawResourceMetaSet, searchParameterHash, rawResourceStream) = reader.ReadRow(
                    VLatest.Resource.ResourceTypeId,
                    VLatest.Resource.ResourceId,
                    VLatest.Resource.Version,
                    VLatest.Resource.IsDeleted,
                    VLatest.Resource.ResourceSurrogateId,
                    VLatest.Resource.RequestMethod,
                    _isMatch,
                    _isPartial,
                    VLatest.Resource.IsRawResourceMetaSet,
                    VLatest.Resource.SearchParamHash,
                    VLatest.Resource.RawResource);
            }
            else
            {
                (resourceTypeId, resourceId, version, isDeleted, resourceSurrogateId, requestMethod, isMatch, isPartialEntry,
                    isRawResourceMetaSet, rawResourceStream) = reader.ReadRow(
                    VLatest.Resource.ResourceTypeId,
                    VLatest.Resource.ResourceId,
                    VLatest.Resource.Version,
                    VLatest.Resource.IsDeleted,
                    VLatest.Resource.ResourceSurrogateId,
                    VLatest.Resource.RequestMethod,
                    _isMatch,
                    _isPartial,
                    VLatest.Resource.IsRawResourceMetaSet,
                    VLatest.Resource.RawResource);
            }
        }

        private SqlConnectionInitializer GetSqlConnectionInitializer()
        {
#if DEBUG
            return (sqlConnection) => sqlConnection.InfoMessage += (sender, args) => _logger.LogInformation("SQL message: {Message}", args.Message);
#else
            null;
#endif
        }

        [Conditional("DEBUG")]
        private static void EnableTimeAndIoMessageLogging(IndentedStringBuilder stringBuilder)
        {
            stringBuilder.AppendLine("SET STATISTICS IO ON;");
            stringBuilder.AppendLine("SET STATISTICS TIME ON;");
            stringBuilder.AppendLine();
        }

        /// <summary>
        /// Logs the parameter declarations and command text of a SQL command
        /// </summary>
        [Conditional("DEBUG")]
        private void LogSqlCommand(SqlCommand sqlCommand)
        {
            var sb = new StringBuilder();
            foreach (SqlParameter p in sqlCommand.Parameters)
            {
                sb.Append("DECLARE ")
                    .Append(p)
                    .Append(' ')
                    .Append(p.SqlDbType)
                    .Append(p.Value is string ? (p.Size == -1 ? "(MAX)" : $"({p.Size})") : p.Value is decimal ? $"({p.Precision},{p.Scale})" : null)
                    .Append(" = ")
                    .Append(p.SqlDbType == SqlDbType.NChar || p.SqlDbType == SqlDbType.NText || p.SqlDbType == SqlDbType.NVarChar ? "N" : null)
                    .Append(p.Value is string || p.Value is DateTime ? $"'{p.Value:O}'" : p.Value.ToString())
                    .AppendLine(";");
            }

            sb.AppendLine();

            sb.AppendLine(sqlCommand.CommandText);
            sb.AppendLine($"{nameof(sqlCommand.CommandTimeout)} = " + TimeSpan.FromSeconds(sqlCommand.CommandTimeout).Duration().ToString());
            _logger.LogInformation("{SqlQuery}", sb.ToString());
        }

        protected async override Task<SearchResult> SearchForReindexInternalAsync(SearchOptions searchOptions, string searchParameterHash, CancellationToken cancellationToken)
        {
            SqlSearchOptions sqlSearchOptions = new SqlSearchOptions(searchOptions);
            return await SearchImpl(sqlSearchOptions, SqlSearchType.Reindex, searchParameterHash, cancellationToken);
        }

        /*private class ResourceTableRow
        {
            public short ResourceTypeId { get; set; }

            public string ResourceId { get; set; }

            public int Version { get; set; }

            public bool IsDeleted { get; set; }

            public long ResourceSurrogateId { get; set; }

            public string RequestMethod { get; set; }

            public bool IsMatch { get; set; }

            public bool IsPartialEntry { get; set; }

            public bool IsRawResourceMetaSet { get; set; }

            public string SearchParameterHash { get; set; }

            public Stream RawResourceStream { get; set; }

            public int FieldCount { get; set; }

            public object SortValue { get; set; }
        }*/

        private class RowProcessorResult
        {
            public int MatchCount { get; set; }

            public int NumberOfColumnsRead { get; set; }

            public SqlSearchOptions ClonedSearchOptions { get; set; }

            public bool MoreResults { get; set; }

            public short? NewContinuationType { get; set; }

            public long? NewContinuationId { get; set; }

            public string SortValue { get; set; }

            public bool IsResultPartial { get; set; }

            public List<SearchResultEntry> Resources { get; set; }
        }

        private class CountResult
        {
            public long Count { get; set; } = 0;
        }
    }
}
