// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Common;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal class SqlServerSearchService : SearchService
    {
        private readonly SqlServerFhirModel _model;
        private readonly SqlRootExpressionRewriter _sqlRootExpressionRewriter;
        private readonly ChainFlatteningRewriter _chainFlatteningRewriter;
        private readonly StringOverflowRewriter _stringOverflowRewriter;
        private readonly SqlServerDataStoreConfiguration _configuration;
        private readonly ILogger<SqlServerSearchService> _logger;

        public SqlServerSearchService(
            ISearchOptionsFactory searchOptionsFactory,
            IFhirDataStore fhirDataStore,
            IModelInfoProvider modelInfoProvider,
            SqlServerFhirModel model,
            SqlRootExpressionRewriter sqlRootExpressionRewriter,
            ChainFlatteningRewriter chainFlatteningRewriter,
            StringOverflowRewriter stringOverflowRewriter,
            SqlServerDataStoreConfiguration configuration,
            ILogger<SqlServerSearchService> logger)
            : base(searchOptionsFactory, fhirDataStore, modelInfoProvider)
        {
            EnsureArg.IsNotNull(sqlRootExpressionRewriter, nameof(sqlRootExpressionRewriter));
            EnsureArg.IsNotNull(chainFlatteningRewriter, nameof(chainFlatteningRewriter));
            EnsureArg.IsNotNull(stringOverflowRewriter, nameof(stringOverflowRewriter));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _model = model;
            _sqlRootExpressionRewriter = sqlRootExpressionRewriter;
            _chainFlatteningRewriter = chainFlatteningRewriter;
            _stringOverflowRewriter = stringOverflowRewriter;
            _configuration = configuration;
            _logger = logger;
        }

        protected override Task<SearchResult> SearchInternalAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            return SearchImpl(searchOptions, false, cancellationToken);
        }

        protected override Task<SearchResult> SearchHistoryInternalAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            return SearchImpl(searchOptions, true, cancellationToken);
        }

        private async Task<SearchResult> SearchImpl(SearchOptions searchOptions, bool historySearch, CancellationToken cancellationToken)
        {
            await _model.EnsureInitialized();

            Expression searchExpression = searchOptions.Expression;

            // AND in the continuation token
            if (!string.IsNullOrWhiteSpace(searchOptions.ContinuationToken))
            {
                if (long.TryParse(searchOptions.ContinuationToken, NumberStyles.None, CultureInfo.InvariantCulture, out var token))
                {
                    var tokenExpression = Expression.SearchParameter(SqlSearchParameters.ResourceSurrogateIdParameter, Expression.GreaterThan(SqlFieldName.ResourceSurrogateId, null, token));
                    searchExpression = searchExpression == null ? tokenExpression : (Expression)Expression.And(tokenExpression, searchExpression);
                }
                else
                {
                    throw new BadRequestException(Resources.InvalidContinuationToken);
                }
            }

            SqlRootExpression expression = (SqlRootExpression)searchExpression
                                               ?.AcceptVisitor(LastUpdatedToResourceSurrogateIdRewriter.Instance)
                                               .AcceptVisitor(DateTimeEqualityRewriter.Instance)
                                               .AcceptVisitor(FlatteningRewriter.Instance)
                                               .AcceptVisitor(_sqlRootExpressionRewriter)
                                               .AcceptVisitor(TableExpressionCombiner.Instance)
                                               .AcceptVisitor(DenormalizedPredicateRewriter.Instance)
                                               .AcceptVisitor(NormalizedPredicateReorderer.Instance)
                                               .AcceptVisitor(_chainFlatteningRewriter)
                                               .AcceptVisitor(DateTimeBoundedRangeRewriter.Instance)
                                               .AcceptVisitor(_stringOverflowRewriter)
                                               .AcceptVisitor(NumericRangeRewriter.Instance)
                                               .AcceptVisitor(MissingSearchParamVisitor.Instance)
                                               .AcceptVisitor(TopRewriter.Instance, searchOptions)
                                           ?? SqlRootExpression.WithDenormalizedExpressions();

            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                connection.Open();

                using (SqlCommand sqlCommand = connection.CreateCommand())
                {
                    var stringBuilder = new IndentedStringBuilder(new StringBuilder());

                    EnableTimeAndIoMessageLogging(stringBuilder, connection);

                    var queryGenerator = new SqlQueryGenerator(stringBuilder, new SqlQueryParameterManager(sqlCommand.Parameters), _model, historySearch);

                    expression.AcceptVisitor(queryGenerator, searchOptions);

                    sqlCommand.CommandText = stringBuilder.ToString();

                    LogSqlComand(sqlCommand);

                    using (var reader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                    {
                        if (searchOptions.CountOnly)
                        {
                            await reader.ReadAsync(cancellationToken);
                            return new SearchResult(reader.GetInt32(0), searchOptions.UnsupportedSearchParams);
                        }

                        var resources = new List<ResourceWrapper>(searchOptions.MaxItemCount);
                        long? newContinuationId = null;
                        bool moreResults = false;

                        while (await reader.ReadAsync(cancellationToken))
                        {
                            (short resourceTypeId, string resourceId, int version, bool isDeleted, long resourceSurrogateId, string requestMethod, Stream rawResourceStream) = reader.ReadRow(
                                V1.Resource.ResourceTypeId,
                                V1.Resource.ResourceId,
                                V1.Resource.Version,
                                V1.Resource.IsDeleted,
                                V1.Resource.ResourceSurrogateId,
                                V1.Resource.RequestMethod,
                                V1.Resource.RawResource);

                            if (resources.Count == searchOptions.MaxItemCount)
                            {
                                moreResults = true;
                                break;
                            }

                            newContinuationId = resourceSurrogateId;

                            string rawResource;

                            using (rawResourceStream)
                            using (var gzipStream = new GZipStream(rawResourceStream, CompressionMode.Decompress))
                            using (var streamReader = new StreamReader(gzipStream, SqlServerFhirDataStore.ResourceEncoding))
                            {
                                rawResource = await streamReader.ReadToEndAsync();
                            }

                            resources.Add(new ResourceWrapper(
                                resourceId,
                                version.ToString(CultureInfo.InvariantCulture),
                                _model.GetResourceTypeName(resourceTypeId),
                                new RawResource(rawResource, FhirResourceFormat.Json),
                                new ResourceRequest(requestMethod),
                                new DateTimeOffset(ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(resourceSurrogateId), TimeSpan.Zero),
                                isDeleted,
                                null,
                                null,
                                null));
                        }

                        // call NextResultAsync to get the info messages
                        await reader.NextResultAsync(cancellationToken);

                        IReadOnlyList<(string parameterName, string reason)> unsupportedSortingParameters;
                        if (searchOptions.Sort?.Count > 0)
                        {
                            // we don't currently support sort
                            unsupportedSortingParameters = searchOptions.UnsupportedSortingParams.Concat(searchOptions.Sort.Select(s => (s.searchParameterInfo.Name, Core.Resources.SortNotSupported))).ToList();
                        }
                        else
                        {
                            unsupportedSortingParameters = searchOptions.UnsupportedSortingParams;
                        }

                        return new SearchResult(resources, searchOptions.UnsupportedSearchParams, unsupportedSortingParameters, moreResults ? newContinuationId.Value.ToString(CultureInfo.InvariantCulture) : null);
                    }
                }
            }
        }

        [Conditional("DEBUG")]
        private void EnableTimeAndIoMessageLogging(IndentedStringBuilder stringBuilder, SqlConnection connection)
        {
            stringBuilder.AppendLine("SET STATISTICS IO ON;");
            stringBuilder.AppendLine("SET STATISTICS TIME ON;");
            stringBuilder.AppendLine();
            connection.InfoMessage += (sender, args) => _logger.LogInformation($"SQL message: {args.Message}");
        }

        /// <summary>
        /// Logs the parameter declarations and command text of a SQL command
        /// </summary>
        [Conditional("DEBUG")]
        private void LogSqlComand(SqlCommand sqlCommand)
        {
            var sb = new StringBuilder();
            foreach (SqlParameter p in sqlCommand.Parameters)
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

            sb.AppendLine(sqlCommand.CommandText);
            _logger.LogInformation(sb.ToString());
        }
    }
}
