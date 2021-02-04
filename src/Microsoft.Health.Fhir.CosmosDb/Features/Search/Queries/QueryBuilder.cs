// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.CosmosDb.Features.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries
{
    public class QueryBuilder : IQueryBuilder
    {
        public QueryDefinition BuildSqlQuerySpec(SearchOptions searchOptions, IReadOnlyList<IncludeExpression> includes)
        {
            return new QueryBuilderHelper().BuildSqlQuerySpec(searchOptions, includes);
        }

        public QueryDefinition GenerateHistorySql(SearchOptions searchOptions)
        {
            return new QueryBuilderHelper().GenerateHistorySql(searchOptions);
        }

        public QueryDefinition GenerateReindexSql(SearchOptions searchOptions, string searchParameterHash)
        {
            return new QueryBuilderHelper().GenerateReindexSql(searchOptions, searchParameterHash);
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

            public QueryDefinition BuildSqlQuerySpec(SearchOptions searchOptions, IReadOnlyList<IncludeExpression> includes)
            {
                EnsureArg.IsNotNull(searchOptions, nameof(searchOptions));

                if (searchOptions.CountOnly)
                {
                    AppendSelectFromRoot("VALUE COUNT(1)");
                }
                else
                {
                    AppendSelectFromRoot(includes: includes);
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
                   true,
                   (KnownResourceWrapperProperties.IsHistory, false),
                   (KnownResourceWrapperProperties.IsDeleted, false));

                if (!searchOptions.CountOnly)
                {
                    var hasOrderBy = false;
                    foreach (var sortOptions in searchOptions.Sort)
                    {
                        if (string.Equals(sortOptions.searchParameterInfo.Code, KnownQueryParameterNames.LastUpdated, StringComparison.OrdinalIgnoreCase))
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
                            throw new SearchParameterNotSupportedException(string.Format(Core.Resources.SearchSortParameterNotSupported, sortOptions.searchParameterInfo.Code));
                        }
                    }
                }

                var query = new QueryDefinition(_queryBuilder.ToString());
                _queryParameterManager.AddToQuery(query);

                return query;
            }

            public QueryDefinition GenerateHistorySql(SearchOptions searchOptions)
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

                var query = new QueryDefinition(_queryBuilder.ToString());
                _queryParameterManager.AddToQuery(query);

                return query;
            }

            public QueryDefinition GenerateReindexSql(SearchOptions searchOptions, string searchParameterHash)
            {
                EnsureArg.IsNotNull(searchOptions, nameof(searchOptions));
                EnsureArg.IsNotNull(searchParameterHash, nameof(searchParameterHash));

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
                   true,
                   (KnownResourceWrapperProperties.IsDeleted, false));

                _queryHelper.AppendSearchParameterHashFliter(searchParameterHash);

                var query = new QueryDefinition(_queryBuilder.ToString());
                _queryParameterManager.AddToQuery(query);

                return query;
            }

            private void AppendSelectFromRoot(string selectList = SearchValueConstants.SelectedFields, IReadOnlyList<IncludeExpression> includes = null)
            {
                _queryHelper.AppendSelect(selectList);

                if (includes?.Count > 0)
                {
                    // add to the projection an array of {r1
                    _queryBuilder.AppendLine(",");

                    _queryBuilder
                        .Append("ARRAY(SELECT p.")
                        .Append(SearchValueConstants.ReferenceResourceTypeName)
                        .Append(", p.")
                        .Append(SearchValueConstants.ReferenceResourceIdName)
                        .Append(" FROM p in r.")
                        .Append(KnownResourceWrapperProperties.SearchIndices).Append(" WHERE ");

                    for (var i = 0; i < includes.Count; i++)
                    {
                        IncludeExpression includeExpression = includes[i];

                        if (i != 0)
                        {
                            _queryBuilder.Append(" OR ");
                        }

                        _queryBuilder.Append("(");

                        if (includeExpression.WildCard)
                        {
                            _queryBuilder.Append("IS_DEFINED(p.").Append(SearchValueConstants.ReferenceResourceIdName).Append(")");
                        }
                        else
                        {
                            _queryBuilder
                                .Append("p.")
                                .Append(SearchValueConstants.ParamName)
                                .Append(" = '")
                                .Append(includeExpression.ReferenceSearchParameter.Code)
                                .Append("'");
                        }

                        if (!string.IsNullOrEmpty(includeExpression.TargetResourceType))
                        {
                            _queryBuilder
                                .Append(" AND p.")
                                .Append(SearchValueConstants.ReferenceResourceTypeName)
                                .Append(" = '")
                                .Append(includeExpression.TargetResourceType)
                                .Append("'");
                        }

                        _queryBuilder.Append(")");
                    }

                    _queryBuilder.Append(") AS ").Append(KnownDocumentProperties.ReferencesToInclude);
                }

                _queryHelper.AppendFromRoot();
            }

            private void AppendFilterCondition(string logicalOperator, bool equal, params (string, object)[] conditions)
            {
                _queryHelper.AppendFilterCondition(logicalOperator, equal, conditions);
            }

            private void AppendFilterCondition(string name, object value, bool equal)
            {
                _queryHelper.AppendFilterCondition(name, value, equal);
            }

            private void AppendSystemDataFilter()
            {
                _queryHelper.AppendSystemDataFilter(false);
            }
        }
    }
}
