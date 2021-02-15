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
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Context;
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
        private readonly SqlServerFhirModel _model;
        private readonly SqlRootExpressionRewriter _sqlRootExpressionRewriter;

        private readonly SortRewriter _sortRewriter;
        private readonly ChainFlatteningRewriter _chainFlatteningRewriter;
        private readonly ILogger<SqlServerSearchService> _logger;
        private readonly BitColumn _isMatch = new BitColumn("IsMatch");
        private readonly BitColumn _isPartial = new BitColumn("IsPartial");
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private const string SortValueColumnName = "SortValue";
        private readonly SchemaInformation _schemaInformation;
        private readonly ISortingValidator _sortingValidator;
        private readonly IFhirRequestContextAccessor _requestContextAccessor;
        private const int _resourceTableColumnCount = 11;

        public SqlServerSearchService(
            ISearchOptionsFactory searchOptionsFactory,
            IFhirDataStore fhirDataStore,
            SqlServerFhirModel model,
            SqlRootExpressionRewriter sqlRootExpressionRewriter,
            ChainFlatteningRewriter chainFlatteningRewriter,
            SortRewriter sortRewriter,
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            SchemaInformation schemaInformation,
            ISortingValidator sortingValidator,
            IFhirRequestContextAccessor requestContextAccessor,
            ILogger<SqlServerSearchService> logger)
            : base(searchOptionsFactory, fhirDataStore)
        {
            EnsureArg.IsNotNull(sqlRootExpressionRewriter, nameof(sqlRootExpressionRewriter));
            EnsureArg.IsNotNull(chainFlatteningRewriter, nameof(chainFlatteningRewriter));
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(sortingValidator, nameof(sortingValidator));
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _model = model;
            _sqlRootExpressionRewriter = sqlRootExpressionRewriter;
            _sortRewriter = sortRewriter;
            _chainFlatteningRewriter = chainFlatteningRewriter;
            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _logger = logger;

            _schemaInformation = schemaInformation;
            _sortingValidator = sortingValidator;
            _requestContextAccessor = requestContextAccessor;
        }

        protected override async Task<SearchResult> SearchInternalAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            SearchResult searchResult;

            // If we should include the total count of matching search results
            if (searchOptions.IncludeTotal == TotalType.Accurate && !searchOptions.CountOnly)
            {
                searchResult = await SearchImpl(searchOptions, false, false, null, cancellationToken);

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
                        var countOnlySearchResult = await SearchImpl(searchOptions, false, false, null, cancellationToken);

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
                searchResult = await SearchImpl(searchOptions, false, false, null, cancellationToken);
            }

            return searchResult;
        }

        protected override async Task<SearchResult> SearchHistoryInternalAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            return await SearchImpl(searchOptions, true, false, null, cancellationToken);
        }

        private async Task<SearchResult> SearchImpl(SearchOptions searchOptions, bool isHistorySearch, bool isReindexSearch, string currentSearchParameterHash, CancellationToken cancellationToken)
        {
            Expression searchExpression = searchOptions.Expression;

            // AND in the continuation token
            if (!string.IsNullOrWhiteSpace(searchOptions.ContinuationToken) && !searchOptions.CountOnly)
            {
                var continuationToken = ContinuationToken.FromString(searchOptions.ContinuationToken);
                if (continuationToken != null)
                {
                    // in case it's a _lastUpdated sort optimization
                    if (string.IsNullOrEmpty(continuationToken.SortValue))
                    {
                        (SearchParameterInfo searchParamInfo, SortOrder sortOrder) = searchOptions.Sort.Count == 0 ? default : searchOptions.Sort[0];

                        Expression lastUpdatedExpression = sortOrder == SortOrder.Ascending
                            ? Expression.GreaterThan(SqlFieldName.ResourceSurrogateId, null, continuationToken.ResourceSurrogateId)
                            : Expression.LessThan(SqlFieldName.ResourceSurrogateId, null, continuationToken.ResourceSurrogateId);

                        var tokenExpression = Expression.SearchParameter(SqlSearchParameters.ResourceSurrogateIdParameter, lastUpdatedExpression);
                        searchExpression = searchExpression == null ? tokenExpression : (Expression)Expression.And(tokenExpression, searchExpression);
                    }
                }
                else
                {
                    throw new BadRequestException(Resources.InvalidContinuationToken);
                }
            }

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
                                               .AcceptVisitor(_sortRewriter, searchOptions)
                                               .AcceptVisitor(SearchParamTableExpressionReorderer.Instance)
                                               .AcceptVisitor(MissingSearchParamVisitor.Instance)
                                               .AcceptVisitor(NotExpressionRewriter.Instance)
                                               .AcceptVisitor(_chainFlatteningRewriter)
                                               .AcceptVisitor(ResourceColumnPredicatePushdownRewriter.Instance)
                                               .AcceptVisitor(DateTimeBoundedRangeRewriter.Instance)
                                               .AcceptVisitor(StringOverflowRewriter.Instance)
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
                    new SqlQueryParameterManager(sqlCommandWrapper.Parameters),
                    _model,
                    isHistorySearch,
                    _schemaInformation,
                    isReindexSearch,
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

                            // At this point we are at the last row.
                            // if we have more columns, it means sort expressions were added.
                            if (reader.FieldCount > _resourceTableColumnCount)
                            {
                                sortValue = reader.GetValue(SortValueColumnName) as DateTime?;
                            }

                            continue;
                        }

                        // See if this resource is a continuation token candidate and increase the count
                        if (isMatch)
                        {
                            newContinuationId = resourceSurrogateId;
                            matchCount++;
                        }

                        string rawResource;
                        using (rawResourceStream)
                        {
                            rawResource = await CompressedRawResourceConverter.ReadCompressedRawResource(rawResourceStream);
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

                    // Continuation token prep
                    ContinuationToken continuationToken = null;
                    if (moreResults)
                    {
                        if (sortValue.HasValue)
                        {
                            continuationToken = new ContinuationToken(new object[]
                            {
                                sortValue.Value.ToString("o"),
                                newContinuationId ?? 0,
                            });
                        }
                        else
                        {
                            continuationToken = new ContinuationToken(new object[]
                            {
                                newContinuationId ?? 0,
                            });
                        }
                    }

                    if (isResultPartial)
                    {
                        _requestContextAccessor.FhirRequestContext.BundleIssues.Add(
                            new OperationOutcomeIssue(
                                OperationOutcomeConstants.IssueSeverity.Warning,
                                OperationOutcomeConstants.IssueType.Incomplete,
                                Core.Resources.TruncatedIncludeMessage));
                    }

                    return new SearchResult(resources, continuationToken?.ToJson(), searchOptions.Sort, searchOptions.UnsupportedSearchParams);
                }
            }
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
                    .Append(" ")
                    .Append(p.SqlDbType)
                    .Append(p.Value is string ? $"({p.Size})" : p.Value is decimal ? $"({p.Precision},{p.Scale})" : null)
                    .Append(" = ")
                    .Append(p.SqlDbType == SqlDbType.NChar || p.SqlDbType == SqlDbType.NText || p.SqlDbType == SqlDbType.NVarChar ? "N" : null)
                    .Append(p.Value is string || p.Value is DateTime ? $"'{p.Value}'" : p.Value.ToString())
                    .AppendLine(";");
            }

            sb.AppendLine();

            sb.AppendLine(sqlCommandWrapper.CommandText);
            _logger.LogInformation(sb.ToString());
        }

        protected async override Task<SearchResult> SearchForReindexInternalAsync(SearchOptions searchOptions, string searchParameterHash, CancellationToken cancellationToken)
        {
            return await SearchImpl(searchOptions, false, true, searchParameterHash, cancellationToken);
        }
    }
}
