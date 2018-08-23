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
        private const int SearchCriteriaLimit = 5;

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

                MultiaryExpression expression = searchOptions.Expression;

                if (expression != null)
                {
                    if (expression.Expressions.Count > SearchCriteriaLimit)
                    {
                        throw new SearchOperationNotSupportedException(
                            string.Format(Resources.ExceededSearchCriteriaLimit, SearchCriteriaLimit));
                    }

                    var expressionQueryBuilder = new ExpressionQueryBuilder(
                        _queryBuilder,
                        _queryParameterManager);

                    ExpressionToJoin(expressionQueryBuilder, expression);
                }

                AppendFilterCondition(
                    "AND",
                    (KnownResourceWrapperProperties.ResourceTypeName, searchOptions.ResourceType),
                    (KnownResourceWrapperProperties.IsHistory, false),
                    (KnownResourceWrapperProperties.IsDeleted, false));

                AppendSystemDataFilter("AND");

                SqlQuerySpec query = new SqlQuerySpec(
                    _queryBuilder.ToString(),
                    _queryParameterManager.ToSqlParameterCollection());

                return query;
            }

            public SqlQuerySpec GenerateHistorySql(string resourceType, SearchOptions searchOptions)
            {
                EnsureArg.IsNotNull(searchOptions, nameof(searchOptions));

                AppendSelectFromRoot();

                MultiaryExpression expression = searchOptions.Expression;

                if (expression != null)
                {
                    var expressionQueryBuilder = new ExpressionQueryBuilder(
                        _queryBuilder,
                        _queryParameterManager);

                    ExpressionToJoin(expressionQueryBuilder, expression);
                }

                if (!string.IsNullOrEmpty(resourceType))
                {
                    AppendFilterCondition(
                        "AND",
                        (KnownResourceWrapperProperties.ResourceTypeName, searchOptions.ResourceType));

                    AppendSystemDataFilter("AND");
                }
                else
                {
                    AppendSystemDataFilter("WHERE");
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

            private void ExpressionToJoin(ExpressionQueryBuilder expressionQueryBuilder, MultiaryExpression expression)
            {
                for (int i = 0; i < expression.Expressions.Count; i++)
                {
                    expressionQueryBuilder.SearchIndexAliasName = $"{SearchValueConstants.SearchIndexAliasName}{i}";

                    Expression subExpression = expression.Expressions[i];

                    _queryBuilder
                        .Append("JOIN (SELECT VALUE ")
                        .Append(expressionQueryBuilder.SearchIndexAliasName)
                        .Append(" FROM ")
                        .Append(expressionQueryBuilder.SearchIndexAliasName)
                        .Append(" IN ")
                        .Append(SearchValueConstants.RootAliasName).Append(".").Append(KnownResourceWrapperProperties.SearchIndices)
                        .Append(" WHERE (");
                    subExpression.AcceptVisitor(expressionQueryBuilder);
                    _queryBuilder.AppendLine("))");
                }
            }

            private void AppendSelectFromRoot(string selectList = SearchValueConstants.RootAliasName)
            {
                _queryBuilder
                    .Append("SELECT ")
                    .Append(selectList)
                    .Append(" FROM root ")
                    .AppendLine(SearchValueConstants.RootAliasName);
            }

            private void AppendFilterCondition(string logicalOperator, params (string Name, object Value)[] conditions)
            {
                _queryBuilder.Append("WHERE ");

                for (int i = 0; i < conditions.Length; i++)
                {
                    var condition = conditions[i];

                    _queryBuilder
                        .Append(SearchValueConstants.RootAliasName).Append(".").Append(condition.Name)
                        .Append(" = ")
                        .AppendLine(_queryParameterManager.AddOrGetParameterMapping(condition.Value));

                    if (i != conditions.Length - 1)
                    {
                        _queryBuilder
                            .Append(logicalOperator)
                            .Append(" ");
                    }
                }
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
