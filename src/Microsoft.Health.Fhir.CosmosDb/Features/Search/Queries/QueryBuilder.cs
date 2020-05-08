// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Health.CosmosDb.Features.Queries;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries
{
    public class QueryBuilder : IQueryBuilder
    {
        public SqlQuerySpec BuildSqlQuerySpec(SearchOptions searchOptions)
        {
            return new QueryBuilderHelper().BuildSqlQuerySpec(searchOptions);
        }

        public SqlQuerySpec GenerateHistorySql(SearchOptions searchOptions)
        {
            return new QueryBuilderHelper().GenerateHistorySql(searchOptions);
        }

        private class QueryBuilderHelper
        {
            private readonly StringBuilder _queryBuilder;
            private readonly QueryParameterManager _queryParameterManager;
            private readonly QueryHelper _queryHelper;

            public QueryBuilderHelper()
            {
                _queryBuilder = new StringBuilder();
                _queryParameterManager = new QueryParameterManager();
                _queryHelper = new QueryHelper(_queryBuilder, _queryParameterManager, SearchValueConstants.RootAliasName);
            }

            public SqlQuerySpec BuildSqlQuerySpec(SearchOptions searchOptions)
            {
                EnsureArg.IsNotNull(searchOptions, nameof(searchOptions));

                if (searchOptions.CountOnly)
                {
                    AppendSelectFromRoot("VALUE COUNT(1)");
                }
                else
                {
                    AppendSelectFromRoot();
                }

                AppendSystemDataFilter();

                var expressionQueryBuilder = new ExpressionQueryBuilder(
                    _queryBuilder,
                    _queryParameterManager);

                if (searchOptions.Expression != null)
                {
                    _queryBuilder.Append("AND ");
                    searchOptions.Expression.AcceptVisitor(expressionQueryBuilder);
                }

                AppendFilterCondition(
                   "AND",
                   (KnownResourceWrapperProperties.IsHistory, false),
                   (KnownResourceWrapperProperties.IsDeleted, false));

                if (!searchOptions.CountOnly)
                {
                    var hasOrderBy = false;
                    foreach (var sortOptions in searchOptions.Sort)
                    {
                        if (string.Equals(sortOptions.searchParameterInfo.Name, KnownQueryParameterNames.LastUpdated, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!hasOrderBy)
                            {
                                _queryBuilder.Append("ORDER BY ");
                                hasOrderBy = true;
                            }

                            _queryBuilder.Append(SearchValueConstants.RootAliasName).Append(".")
                                .Append(KnownResourceWrapperProperties.LastModified).Append(" ")
                                .AppendLine(sortOptions.sortOrder == SortOrder.Ascending ? "ASC" : "DESC");
                        }
                        else
                        {
                            throw new SearchParameterNotSupportedException(string.Format(Core.Resources.SearchSortParameterNotSupported, sortOptions.searchParameterInfo.Name));
                        }
                    }
                }

                var query = new SqlQuerySpec(
                    _queryBuilder.ToString(),
                    _queryParameterManager.ToSqlParameterCollection());

                return query;
            }

            public SqlQuerySpec GenerateHistorySql(SearchOptions searchOptions)
            {
                EnsureArg.IsNotNull(searchOptions, nameof(searchOptions));

                AppendSelectFromRoot();

                AppendSystemDataFilter();

                var expressionQueryBuilder = new ExpressionQueryBuilder(
                    _queryBuilder,
                    _queryParameterManager);

                if (searchOptions.Expression != null)
                {
                    _queryBuilder.Append("AND ");
                    searchOptions.Expression.AcceptVisitor(expressionQueryBuilder);
                }

                _queryBuilder
                    .Append("ORDER BY ")
                    .Append(SearchValueConstants.RootAliasName).Append(".").Append(KnownResourceWrapperProperties.LastModified)
                    .AppendLine(" DESC");

                var sqlParameterCollection = _queryParameterManager.ToSqlParameterCollection();

                var query = new SqlQuerySpec(
                    _queryBuilder.ToString(),
                    sqlParameterCollection);

                return query;
            }

            private void AppendSelectFromRoot(string selectList = SearchValueConstants.RootAliasName)
            {
                _queryHelper.AppendSelectFromRoot(selectList);
            }

            private void AppendFilterCondition(string logicalOperator, params (string, object)[] conditions)
            {
                _queryHelper.AppendFilterCondition(logicalOperator, conditions);
            }

            private void AppendFilterCondition(string name, object value)
            {
                _queryHelper.AppendFilterCondition(name, value);
            }

            private void AppendSystemDataFilter()
            {
                _queryHelper.AppendSystemDataFilter(false);
            }
        }
    }
}
