// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    public class SqlServerSearchService : SearchService
    {
        public SqlServerSearchService(
            ISearchOptionsFactory searchOptionsFactory,
            IBundleFactory bundleFactory,
            IFhirDataStore fhirDataStore,
            IModelInfoProvider modelInfoProvider)
            : base(searchOptionsFactory, bundleFactory, fhirDataStore, modelInfoProvider)
        {
        }

        protected override Task<SearchResult> SearchInternalAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
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

            throw new NotImplementedException();
        }

        protected override Task<SearchResult> SearchHistoryInternalAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            throw new System.NotImplementedException();
        }
    }
}
