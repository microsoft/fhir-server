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
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
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
using SortOrder = Microsoft.Health.Fhir.Core.Features.Search.SortOrder;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal class SqlServerSearchService : SearchService
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SqlRootExpressionRewriter _sqlRootExpressionRewriter;

        private readonly SortRewriter _sortRewriter;
        private readonly PartitionEliminationRewriter _partitionEliminationRewriter;
        private readonly ChainFlatteningRewriter _chainFlatteningRewriter;
        private readonly ILogger<SqlServerSearchService> _logger;
        private readonly BitColumn _isMatch = new BitColumn("IsMatch");
        private readonly BitColumn _isPartial = new BitColumn("IsPartial");
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private const string SortValueColumnName = "SortValue";
        private readonly SchemaInformation _schemaInformation;
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver _searchParameterDefinitionManagerResolver;
        private const int _resourceTableColumnCount = 10;

        public SqlServerSearchService(
            ISearchOptionsFactory searchOptionsFactory,
            IFhirDataStore fhirDataStore,
            ISqlServerFhirModel model,
            SqlRootExpressionRewriter sqlRootExpressionRewriter,
            ChainFlatteningRewriter chainFlatteningRewriter,
            SortRewriter sortRewriter,
            PartitionEliminationRewriter partitionEliminationRewriter,
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            SchemaInformation schemaInformation,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver searchParameterDefinitionManagerResolver,
            ILogger<SqlServerSearchService> logger)
            : base(searchOptionsFactory, fhirDataStore)
        {
            EnsureArg.IsNotNull(sqlRootExpressionRewriter, nameof(sqlRootExpressionRewriter));
            EnsureArg.IsNotNull(chainFlatteningRewriter, nameof(chainFlatteningRewriter));
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(partitionEliminationRewriter, nameof(partitionEliminationRewriter));
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            EnsureArg.IsNotNull(searchParameterDefinitionManagerResolver, nameof(searchParameterDefinitionManagerResolver));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _model = model;
            _sqlRootExpressionRewriter = sqlRootExpressionRewriter;
            _sortRewriter = sortRewriter;
            _partitionEliminationRewriter = partitionEliminationRewriter;
            _chainFlatteningRewriter = chainFlatteningRewriter;
            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _logger = logger;

            _schemaInformation = schemaInformation;
            _requestContextAccessor = requestContextAccessor;
            _searchParameterDefinitionManagerResolver = searchParameterDefinitionManagerResolver;
        }

        public override async Task<SearchResult> SearchAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            SearchResult searchResult;

            // If we should include the total count of matching search results
            if (searchOptions.IncludeTotal == TotalType.Accurate && !searchOptions.CountOnly)
            {
                searchResult = await SearchImpl(searchOptions, SqlSearchType.Default, null, cancellationToken);

                // If this is the first page and there aren't any more pages
                if (searchOptions.ContinuationToken == null && searchResult.ContinuationToken == null)
                {
                    // Count the match results on the page.
                    searchResult.TotalCount = searchResult.Results.Count(r => r.SearchEntryMode == SearchEntryMode.Match);
                }
                else
                {
                    try
                    {
                        // Otherwise, indicate that we'd like to get the count
                        searchOptions.CountOnly = true;

                        // And perform a second read.
                        var countOnlySearchResult = await SearchImpl(searchOptions, SqlSearchType.Default, null, cancellationToken);

                        searchResult.TotalCount = countOnlySearchResult.TotalCount;
                    }
                    finally
                    {
                        // Ensure search options is set to its original state.
                        searchOptions.CountOnly = false;
                    }
                }
            }
            else
            {
                searchResult = await SearchImpl(searchOptions, SqlSearchType.Default, null, cancellationToken);
            }

            return searchResult;
        }

        protected override async Task<SearchResult> SearchHistoryInternalAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            return await SearchImpl(searchOptions, SqlSearchType.History, null, cancellationToken);
        }

        private async Task<SearchResult> SearchImpl(SearchOptions searchOptions, SqlSearchType searchType, string currentSearchParameterHash, CancellationToken cancellationToken)
        {
            Expression searchExpression = searchOptions.Expression;

            // AND in the continuation token
            if (!string.IsNullOrWhiteSpace(searchOptions.ContinuationToken) && !searchOptions.CountOnly)
            {
                var continuationToken = ContinuationToken.FromString(searchOptions.ContinuationToken);
                if (continuationToken != null)
                {
                    if (string.IsNullOrEmpty(continuationToken.SortValue))
                    {
                        // it's a _lastUpdated or (_type,_lastUpdated) sort optimization

                        (SearchParameterInfo _, SortOrder sortOrder) = searchOptions.Sort.Count == 0 ? default : searchOptions.Sort[0];

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

                        Expression lastUpdatedExpression = sortOrder == SortOrder.Ascending
                            ? Expression.GreaterThan(fieldName, null, keyValue)
                            : Expression.LessThan(fieldName, null, keyValue);

                        var tokenExpression = Expression.SearchParameter(parameter, lastUpdatedExpression);
                        searchExpression = searchExpression == null ? tokenExpression : Expression.And(tokenExpression, searchExpression);
                    }
                }
                else
                {
                    throw new BadRequestException(Resources.InvalidContinuationToken);
                }
            }

            var originalSort = searchOptions.Sort;
            searchOptions = UpdateSort(searchOptions, searchExpression, searchType);

            if (searchOptions.CountOnly)
            {
                // if we're only returning a count, discard any _include parameters since included resources are not counted.
                searchExpression = searchExpression?.AcceptVisitor(RemoveIncludesRewriter.Instance);
            }

            SqlRootExpression expression = (SqlRootExpression)searchExpression
                                               ?.AcceptVisitor(LastUpdatedToResourceSurrogateIdRewriter.Instance)
                                               .AcceptVisitor(DateTimeEqualityRewriter.Instance)
                                               .AcceptVisitor(FlatteningRewriter.Instance)
                                               .AcceptVisitor(UntypedReferenceRewriter.Instance)
                                               .AcceptVisitor(_sqlRootExpressionRewriter)
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
                                               .AcceptVisitor(TopRewriter.Instance, searchOptions)
                                               .AcceptVisitor(IncludeRewriter.Instance)
                                           ?? SqlRootExpression.WithResourceTableExpressions();

            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                var stringBuilder = new IndentedStringBuilder(new StringBuilder());

                EnableTimeAndIoMessageLogging(stringBuilder, sqlConnectionWrapper);

                var queryGenerator = new SqlQueryGenerator(
                    stringBuilder,
                    new HashingSqlQueryParameterManager(new SqlQueryParameterManager(sqlCommandWrapper.Parameters)),
                    _model,
                    searchType,
                    _schemaInformation,
                    currentSearchParameterHash);

                expression.AcceptVisitor(queryGenerator, searchOptions);

                sqlCommandWrapper.CommandText = stringBuilder.ToString();

                LogSqlCommand(sqlCommandWrapper);

                using (var reader = await sqlCommandWrapper.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    if (searchOptions.CountOnly)
                    {
                        await reader.ReadAsync(cancellationToken);
                        var searchResult = new SearchResult(reader.GetInt32(0), searchOptions.UnsupportedSearchParams);

                        // call NextResultAsync to get the info messages
                        await reader.NextResultAsync(cancellationToken);

                        return searchResult;
                    }

                    var resources = new List<SearchResultEntry>(searchOptions.MaxItemCount);
                    short? newContinuationType = null;
                    long? newContinuationId = null;
                    bool moreResults = false;
                    int matchCount = 0;

                    // Currently we support only date time sort type.
                    DateTime? sortValue = null;

                    var isResultPartial = false;

                    while (await reader.ReadAsync(cancellationToken))
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

                        // If we get to this point, we know there are more results so we need a continuation token
                        // Additionally, this resource shouldn't be included in the results
                        if (matchCount >= searchOptions.MaxItemCount && isMatch)
                        {
                            moreResults = true;

                            continue;
                        }

                        string rawResource;
                        using (rawResourceStream)
                        {
                            rawResource = await CompressedRawResourceConverter.ReadCompressedRawResource(rawResourceStream);
                        }

                        // See if this resource is a continuation token candidate and increase the count
                        if (isMatch)
                        {
                            newContinuationType = resourceTypeId;
                            newContinuationId = resourceSurrogateId;

                            // Keep track of sort value if this is the last row.
                            // if we have more than 10 columns, it means sort expressions were added.
                            if (matchCount == searchOptions.MaxItemCount - 1 && reader.FieldCount > _resourceTableColumnCount + 1)
                            {
                                sortValue = reader.GetValue(SortValueColumnName) as DateTime?;
                            }

                            matchCount++;
                        }

                        // as long as at least one entry was marked as partial, this resultset
                        // should be marked as partial
                        isResultPartial = isResultPartial || isPartialEntry;

                        resources.Add(new SearchResultEntry(
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
                    }

                    // call NextResultAsync to get the info messages
                    await reader.NextResultAsync(cancellationToken);

                    ContinuationToken continuationToken =
                        moreResults
                            ? new ContinuationToken(
                                searchOptions.Sort.Select(s =>
                                    s.searchParameterInfo.Name switch
                                    {
                                        SearchParameterNames.ResourceType => (object)newContinuationType,
                                        SearchParameterNames.LastUpdated => newContinuationId,
                                        _ => sortValue.Value.ToString("o"),
                                    }).ToArray())
                            : null;

                    if (isResultPartial)
                    {
                        _requestContextAccessor.RequestContext.BundleIssues.Add(
                            new OperationOutcomeIssue(
                                OperationOutcomeConstants.IssueSeverity.Warning,
                                OperationOutcomeConstants.IssueType.Incomplete,
                                Core.Resources.TruncatedIncludeMessage));
                    }

                    return new SearchResult(resources, continuationToken?.ToJson(), originalSort, searchOptions.UnsupportedSearchParams);
                }
            }
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
        private SearchOptions UpdateSort(SearchOptions searchOptions, Expression searchExpression, SqlSearchType sqlSearchType)
        {
            if (sqlSearchType == SqlSearchType.History)
            {
                // history is always sorted by _lastUpdated.
                searchOptions = searchOptions.Clone();

                ISearchParameterDefinitionManager searchParameterDefinitionManager = _searchParameterDefinitionManagerResolver.Invoke();

                searchOptions.Sort = new (SearchParameterInfo searchParameterInfo, SortOrder sortOrder)[]
                {
                    (searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.LastUpdated), SortOrder.Ascending),
                };

                return searchOptions;
            }

            if (searchOptions.Sort.Count == 0)
            {
                searchOptions = searchOptions.Clone();

                ISearchParameterDefinitionManager searchParameterDefinitionManager = _searchParameterDefinitionManagerResolver.Invoke();

                if (_schemaInformation.Current < SchemaVersionConstants.PartitionedTables)
                {
                    searchOptions.Sort = new (SearchParameterInfo searchParameterInfo, SortOrder sortOrder)[]
                    {
                        (searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.LastUpdated), SortOrder.Ascending),
                    };
                }
                else
                {
                    searchOptions.Sort = new (SearchParameterInfo searchParameterInfo, SortOrder sortOrder)[]
                    {
                        (searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.ResourceType), SortOrder.Ascending),
                        (searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.LastUpdated), SortOrder.Ascending),
                    };
                }

                return searchOptions;
            }

            if (searchOptions.Sort.Count == 1 && searchOptions.Sort[0].searchParameterInfo.Name == SearchParameterNames.ResourceType)
            {
                // We will not get here unless the schema version is at least SchemaVersionConstants.PartitionedTables.

                // Add _lastUpdated to the sort list so that there is a deterministic key to sort on

                searchOptions = searchOptions.Clone();

                ISearchParameterDefinitionManager searchParameterDefinitionManager = _searchParameterDefinitionManagerResolver.Invoke();

                searchOptions.Sort = new (SearchParameterInfo searchParameterInfo, SortOrder sortOrder)[]
                {
                    (searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.ResourceType), searchOptions.Sort[0].sortOrder),
                    (searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.LastUpdated), searchOptions.Sort[0].sortOrder),
                };

                return searchOptions;
            }

            if (searchOptions.Sort.Count == 1 && searchOptions.Sort[0].searchParameterInfo.Name == SearchParameterNames.LastUpdated && _schemaInformation.Current >= SchemaVersionConstants.PartitionedTables)
            {
                (short? singleAllowedTypeId, BitArray allowedTypes) = TypeConstraintVisitor.Instance.Visit(searchExpression, _model);

                if (singleAllowedTypeId != null && allowedTypes != null)
                {
                    // this means that this search is over a single type.
                    searchOptions = searchOptions.Clone();

                    ISearchParameterDefinitionManager searchParameterDefinitionManager = _searchParameterDefinitionManagerResolver.Invoke();

                    searchOptions.Sort = new (SearchParameterInfo searchParameterInfo, SortOrder sortOrder)[]
                    {
                        (searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.ResourceType), searchOptions.Sort[0].sortOrder),
                        (searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.LastUpdated), searchOptions.Sort[0].sortOrder),
                    };
                }

                return searchOptions;
            }

            if (searchOptions.Sort[^1].searchParameterInfo.Name != SearchParameterNames.LastUpdated)
            {
                // Make sure custom sort has _lastUpdated as the last sort parameter.

                searchOptions = searchOptions.Clone();

                ISearchParameterDefinitionManager searchParameterDefinitionManager = _searchParameterDefinitionManagerResolver.Invoke();

                searchOptions.Sort = new List<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)>(searchOptions.Sort)
                {
                    (searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.LastUpdated), SortOrder.Ascending),
                };

                return searchOptions;
            }

            return searchOptions;
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

        [Conditional("DEBUG")]
        private void EnableTimeAndIoMessageLogging(IndentedStringBuilder stringBuilder, SqlConnectionWrapper sqlConnectionWrapper)
        {
            stringBuilder.AppendLine("SET STATISTICS IO ON;");
            stringBuilder.AppendLine("SET STATISTICS TIME ON;");
            stringBuilder.AppendLine();
            sqlConnectionWrapper.SqlConnection.InfoMessage += (sender, args) => _logger.LogInformation($"SQL message: {args.Message}");
        }

        /// <summary>
        /// Logs the parameter declarations and command text of a SQL command
        /// </summary>
        [Conditional("DEBUG")]
        private void LogSqlCommand(SqlCommandWrapper sqlCommandWrapper)
        {
            var sb = new StringBuilder();
            foreach (SqlParameter p in sqlCommandWrapper.Parameters)
            {
                sb.Append("DECLARE ")
                    .Append(p)
                    .Append(' ')
                    .Append(p.SqlDbType)
                    .Append(p.Value is string ? $"({p.Size})" : p.Value is decimal ? $"({p.Precision},{p.Scale})" : null)
                    .Append(" = ")
                    .Append(p.SqlDbType == SqlDbType.NChar || p.SqlDbType == SqlDbType.NText || p.SqlDbType == SqlDbType.NVarChar ? "N" : null)
                    .Append(p.Value is string || p.Value is DateTime ? $"'{p.Value:O}'" : p.Value.ToString())
                    .AppendLine(";");
            }

            sb.AppendLine();

            sb.AppendLine(sqlCommandWrapper.CommandText);
            _logger.LogInformation(sb.ToString());
        }

        protected async override Task<SearchResult> SearchForReindexInternalAsync(SearchOptions searchOptions, string searchParameterHash, CancellationToken cancellationToken)
        {
            return await SearchImpl(searchOptions, SqlSearchType.Reindex, searchParameterHash, cancellationToken);
        }
    }
}
