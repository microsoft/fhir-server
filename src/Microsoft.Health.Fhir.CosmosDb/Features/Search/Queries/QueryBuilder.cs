// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;

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
            private StringBuilder _queryBuilder;
            private QueryParameterManager _queryParameterManager;

            public QueryBuilderHelper()
            {
                _queryBuilder = new StringBuilder();
                _queryParameterManager = new QueryParameterManager();
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

                SqlQuerySpec query = new SqlQuerySpec(
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
                _queryBuilder
                    .Append("SELECT ")
                    .Append(selectList)
                    .Append(" FROM root ")
                    .AppendLine(SearchValueConstants.RootAliasName);
            }

            private void AppendFilterCondition(string logicalOperator, params (string, object)[] conditions)
            {
                for (int i = 0; i < conditions.Length; i++)
                {
                    _queryBuilder
                        .Append(logicalOperator)
                        .Append(" ");

                    (string name, object value) = conditions[i];

                    AppendFilterCondition(name, value);
                }
            }

            private void AppendFilterCondition(string name, object value)
            {
                _queryBuilder
                        .Append(SearchValueConstants.RootAliasName).Append(".").Append(name)
                        .Append(" = ")
                        .AppendLine(_queryParameterManager.AddOrGetParameterMapping(value));
            }

            private void AppendSystemDataFilter()
            {
                _queryBuilder
                    .Append(" WHERE ")
                    .Append(SearchValueConstants.RootAliasName).Append(".isSystem")
                    .Append(" = ")
                    .AppendLine(_queryParameterManager.AddOrGetParameterMapping(false));
            }
        }
    }
}
