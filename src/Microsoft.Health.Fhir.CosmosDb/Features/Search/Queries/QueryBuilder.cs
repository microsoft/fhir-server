// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using EnsureThat;
using Microsoft.Azure.Documents;
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

        public SqlQuerySpec GenerateHistorySql(string resourceType, SearchOptions searchOptions)
        {
            return new QueryBuilderHelper().GenerateHistorySql(resourceType, searchOptions);
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

                AppendSystemDataFilter("WHERE");

                MultiaryExpression expression = searchOptions.Expression;

                if (expression != null)
                {
                    var expressionQueryBuilder = new ExpressionQueryBuilder(
                        _queryBuilder,
                        _queryParameterManager);

                    for (int i = 0; i < expression.Expressions.Count; i++)
                    {
                        _queryBuilder.Append("AND ");

                        expressionQueryBuilder.AppendSubquery(expression.Expressions[i]);
                    }
                }

                AppendFilterCondition(
                    "AND",
                    (KnownResourceWrapperProperties.ResourceTypeName, searchOptions.ResourceType),
                    (KnownResourceWrapperProperties.IsHistory, false),
                    (KnownResourceWrapperProperties.IsDeleted, false));

                SqlQuerySpec query = new SqlQuerySpec(
                    _queryBuilder.ToString(),
                    _queryParameterManager.ToSqlParameterCollection());

                return query;
            }

            public SqlQuerySpec GenerateHistorySql(string resourceType, SearchOptions searchOptions)
            {
                EnsureArg.IsNotNull(searchOptions, nameof(searchOptions));

                AppendSelectFromRoot();

                AppendSystemDataFilter("WHERE");

                MultiaryExpression expression = searchOptions.Expression;

                if (expression != null)
                {
                    var expressionQueryBuilder = new ExpressionQueryBuilder(
                        _queryBuilder,
                        _queryParameterManager);

                    for (int i = 0; i < expression.Expressions.Count; i++)
                    {
                        _queryBuilder.Append("AND ");

                        expressionQueryBuilder.AppendSubquery(expression.Expressions[i]);
                    }
                }

                if (!string.IsNullOrEmpty(resourceType))
                {
                    AppendFilterCondition(
                        "AND",
                        (KnownResourceWrapperProperties.ResourceTypeName, searchOptions.ResourceType));
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

            private void AppendSystemDataFilter(string keyword = null)
            {
                // Ensure that we exclude system metadata

                if (!string.IsNullOrEmpty(keyword))
                {
                    _queryBuilder.Append(keyword).Append(" ");
                }

                _queryBuilder
                    .Append("(")
                    .Append("IS_DEFINED(").Append(SearchValueConstants.RootAliasName).Append(".isSystem)")
                    .Append(" = ").Append(_queryParameterManager.AddOrGetParameterMapping(false))
                    .Append(" OR ")
                    .Append(SearchValueConstants.RootAliasName).Append(".isSystem")
                    .Append(" = ").Append(_queryParameterManager.AddOrGetParameterMapping(false))
                    .AppendLine(")");
            }
        }
    }
}
