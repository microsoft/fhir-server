// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
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
    internal class QueryBuilder : IQueryBuilder
    {
        public QueryDefinition BuildSqlQuerySpec(SearchOptions searchOptions, QueryBuilderOptions queryOptions = null)
        {
            return new QueryBuilderHelper().BuildSqlQuerySpec(searchOptions, queryOptions ?? new QueryBuilderOptions());
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

            public QueryDefinition BuildSqlQuerySpec(SearchOptions searchOptions, QueryBuilderOptions queryOptions)
            {
                EnsureArg.IsNotNull(searchOptions, nameof(searchOptions));
                EnsureArg.IsNotNull(queryOptions, nameof(queryOptions));

                if (searchOptions.CountOnly)
                {
                    AppendSelectFromRoot("VALUE COUNT(1)");
                }
                else if (queryOptions.Projection == QueryProjection.IdAndType)
                {
                    AppendSelectFromRoot($"r.{KnownResourceWrapperProperties.ResourceId}, r.{KnownResourceWrapperProperties.ResourceTypeName}", queryOptions.Includes);
                }
                else if (queryOptions.Projection == QueryProjection.ReferencesOnly)
                {
                    AppendSelectFromRoot(string.Empty, queryOptions.Includes);
                }
                else
                {
                    AppendSelectFromRoot(includes: queryOptions.Includes);
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
                    if (searchOptions.Sort.Any())
                    {
                        var sortOption = searchOptions.Sort[0];
                        _queryBuilder.Append("ORDER BY ");

                        if (string.Equals(sortOption.searchParameterInfo.Code, KnownQueryParameterNames.LastUpdated, StringComparison.OrdinalIgnoreCase))
                        {
#pragma warning disable CA1834 // Consider using 'StringBuilder.Append(char)' when applicable
                            _queryBuilder.Append(SearchValueConstants.RootAliasName).Append('.')
#pragma warning restore CA1834 // Consider using 'StringBuilder.Append(char)' when applicable
                                .Append(KnownResourceWrapperProperties.LastModified).Append(' ')
                                .AppendLine(sortOption.sortOrder == SortOrder.Ascending ? "ASC" : "DESC");
                        }
                        else
                        {
#pragma warning disable CA1834 // Consider using 'StringBuilder.Append(char)' when applicable
                            _queryBuilder.Append(SearchValueConstants.RootAliasName)
#pragma warning restore CA1834 // Consider using 'StringBuilder.Append(char)' when applicable
                                .Append(".sort[\"").Append(sortOption.searchParameterInfo.Code).Append("\"].")
                                .Append(sortOption.sortOrder == SortOrder.Ascending ? SearchValueConstants.SortLowValueFieldName : SearchValueConstants.SortHighValueFieldName)
                                .Append(' ').AppendLine(sortOption.sortOrder == SortOrder.Ascending ? "ASC" : "DESC");
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

                _queryBuilder.Append("ORDER BY ");
                var sortOption = searchOptions.Sort[0];

#pragma warning disable CA1834 // Consider using 'StringBuilder.Append(char)' when applicable
                _queryBuilder.Append(SearchValueConstants.RootAliasName).Append('.')
#pragma warning restore CA1834 // Consider using 'StringBuilder.Append(char)' when applicable
                    .Append(KnownResourceWrapperProperties.LastModified).Append(' ')
                    .AppendLine(sortOption.sortOrder == SortOrder.Ascending ? "ASC" : "DESC");

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
                    if (!string.IsNullOrEmpty(selectList))
                    {
                        // add to the projection an array of {r1
                        _queryBuilder.AppendLine(",");
                    }

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

                        _queryBuilder.Append('(');

                        if (includeExpression.WildCard)
                        {
                            _queryBuilder.Append("IS_DEFINED(p.").Append(SearchValueConstants.ReferenceResourceIdName).Append(')');
                        }
                        else
                        {
                            _queryBuilder
                                .Append("p.")
#pragma warning disable CA1834 // Consider using 'StringBuilder.Append(char)' when applicable
                                .Append(SearchValueConstants.ParamName)
#pragma warning restore CA1834 // Consider using 'StringBuilder.Append(char)' when applicable
                                .Append(" = '")
                                .Append(includeExpression.ReferenceSearchParameter.Code)
                                .Append('\'');
                        }

                        if (!string.IsNullOrEmpty(includeExpression.TargetResourceType))
                        {
                            _queryBuilder
                                .Append(" AND p.")
                                .Append(SearchValueConstants.ReferenceResourceTypeName)
                                .Append(" = '")
                                .Append(includeExpression.TargetResourceType)
                                .Append('\'');
                        }

                        _queryBuilder.Append(')');
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
