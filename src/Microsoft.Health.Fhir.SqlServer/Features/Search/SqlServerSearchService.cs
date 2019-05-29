// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    public class SqlServerSearchService : SearchService
    {
        private readonly SqlServerFhirModel _model;
        private readonly SqlServerDataStoreConfiguration _configuration;

        public SqlServerSearchService(
            ISearchOptionsFactory searchOptionsFactory,
            IBundleFactory bundleFactory,
            IFhirDataStore fhirDataStore,
            IModelInfoProvider modelInfoProvider,
            SqlServerFhirModel model,
            SqlServerDataStoreConfiguration configuration)
            : base(searchOptionsFactory, bundleFactory, fhirDataStore, modelInfoProvider)
        {
            _model = model;
            _configuration = configuration;
        }

        protected override async Task<SearchResult> SearchInternalAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            await _model.EnsureInitialized();

            Expression searchExpression = searchOptions.Expression;

            // AND in the continuation token
            if (!string.IsNullOrWhiteSpace(searchOptions.ContinuationToken))
            {
                if (long.TryParse(searchOptions.ContinuationToken, NumberStyles.None, CultureInfo.InvariantCulture, out var token))
                {
                    // TODO: respect order by when implemented
                    var tokenExpression = Expression.SearchParameter(SqlSearchParameters.ResourceSurrogateIdParameter, Expression.GreaterThan(SqlFieldName.ResourceSurrogateId, null, token));
                    searchExpression = searchExpression == null ? tokenExpression : (Expression)Expression.And(tokenExpression, searchExpression);
                }
                else
                {
                    throw new BadRequestException(Resources.InvalidContinuationToken);
                }
            }

            var expression = (SqlRootExpression)searchExpression
                                 ?.AcceptVisitor(FlatteningRewriter.Instance)
                                 ?.AcceptVisitor(ExpressionWithSqlRootRewriter.Instance)
                             ?? SqlRootExpression.WithDenormalizedPredicates();

            expression = (SqlRootExpression)expression.AcceptVisitor(DenormalizedPredicateRewriter.Instance);

            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                connection.Open();

                using (SqlCommand sqlCommand = connection.CreateCommand())
                {
                    var stringBuilder = new IndentedStringBuilder(new StringBuilder());
                    var queryGenerator = new SqlQueryGenerator(stringBuilder, new SqlQueryParameterManager(sqlCommand.Parameters), _model);

                    expression.AcceptVisitor(queryGenerator, searchOptions);

                    sqlCommand.CommandText = stringBuilder.ToString();

                    using (var reader = await sqlCommand.ExecuteReaderAsync(cancellationToken))
                    {
                        List<ResourceWrapper> resources = new List<ResourceWrapper>(searchOptions.MaxItemCount);
                        long? lastSurrogateId = null;
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            (short resourceTypeId, string resourceId, int version, bool isHistory, bool isDeleted, long resourceSurrogateId, DateTime lastUpdated, string requestMethod, Stream rawResourceStream) = reader.ReadRow(
                                V1.Resource.ResourceTypeId,
                                V1.Resource.ResourceId,
                                V1.Resource.Version,
                                V1.Resource.IsHistory,
                                V1.Resource.IsDeleted,
                                V1.Resource.ResourceSurrogateId,
                                V1.Resource.LastUpdated,
                                V1.Resource.RequestMethod,
                                V1.Resource.RawResource);

                            lastSurrogateId = resourceSurrogateId;
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
                                new ResourceRequest(default(Uri), requestMethod),
                                new DateTimeOffset(lastUpdated, TimeSpan.Zero),
                                isDeleted,
                                null,
                                null,
                                null));
                        }

                        return new SearchResult(resources, lastSurrogateId?.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
        }

        protected override Task<SearchResult> SearchHistoryInternalAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            throw new System.NotImplementedException();
        }
    }
}
