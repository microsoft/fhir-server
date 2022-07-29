// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search.Expressions;

/// <summary>
/// This lets us walk to the end of the expression to start filtering, the results are used to filter against the parent layer.
/// </summary>
internal class ChainedExpressionEnumerable : IAsyncEnumerable<Expression>
{
    private readonly IQueryBuilder _queryBuilder;
    private readonly SearchFunc _searchFunc;
    private readonly ChainedExpression _expression;
    private readonly SearchOptions _chainedOptions;
    private readonly SearchParameterInfo _resourceTypeSearchParameter;
    private readonly SearchParameterInfo _resourceIdSearchParameter;

    public ChainedExpressionEnumerable(IQueryBuilder queryBuilder, SearchFunc searchFunc, ChainedExpression expression, SearchOptions chainedOptions, SearchParameterInfo resourceTypeSearchParameter, SearchParameterInfo resourceIdSearchParameter)
    {
        _queryBuilder = queryBuilder;
        _searchFunc = searchFunc;
        _expression = expression;
        _chainedOptions = chainedOptions.Clone();
        _resourceTypeSearchParameter = resourceTypeSearchParameter;
        _resourceIdSearchParameter = resourceIdSearchParameter;
    }

    internal delegate Task<(IReadOnlyList<FhirCosmosResourceWrapper> results, string continuationToken, int? maxConcurrency)> SearchFunc(
        QueryDefinition sqlQuerySpec,
        SearchOptions searchOptions,
        string continuationToken,
        TimeSpan? searchEnumerationTimeoutOverrideIfSequential,
        QueryRequestOptions queryRequestOptionsOverride,
        CancellationToken cancellationToken);

    public IAsyncEnumerator<Expression> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        return new ChainedExpressionEnumerator(_queryBuilder, _searchFunc, _expression, _chainedOptions, _resourceTypeSearchParameter, _resourceIdSearchParameter, cancellationToken);
    }

    private sealed class ChainedExpressionEnumerator : IAsyncEnumerator<Expression>
    {
        private readonly IQueryBuilder _queryBuilder;
        private readonly SearchFunc _searchFunc;
        private readonly ChainedExpression _expression;
        private readonly SearchOptions _chainedOptions;
        private readonly SearchParameterInfo _resourceTypeSearchParameter;
        private readonly SearchParameterInfo _resourceIdSearchParameter;
        private readonly CancellationToken _cancellationToken;
        private readonly IAsyncEnumerator<Expression> _innerChainedEnumerator;
        private string _continuationToken;

        private Expression _resolvedChainedResults;

        public ChainedExpressionEnumerator(IQueryBuilder queryBuilder, SearchFunc searchFunc, ChainedExpression expression, SearchOptions chainedOptions, SearchParameterInfo resourceTypeSearchParameter,  SearchParameterInfo resourceIdSearchParameter, CancellationToken cancellationToken)
        {
            _queryBuilder = queryBuilder;
            _searchFunc = searchFunc;
            _expression = expression;
            _chainedOptions = chainedOptions;
            _resourceTypeSearchParameter = resourceTypeSearchParameter;
            _resourceIdSearchParameter = resourceIdSearchParameter;
            _cancellationToken = cancellationToken;

            if (expression.Expression is ChainedExpression chainedExpression)
            {
                _innerChainedEnumerator = new ChainedExpressionEnumerator(_queryBuilder, searchFunc, chainedExpression, chainedOptions, resourceTypeSearchParameter, _resourceIdSearchParameter, cancellationToken);
            }
        }

        public Expression Current => _resolvedChainedResults;

        public async ValueTask<bool> MoveNextAsync()
        {
            Expression resolvedInnerResults;

            if (_innerChainedEnumerator != null)
            {
                var currentResults = await _innerChainedEnumerator.MoveNextAsync();
                resolvedInnerResults = _innerChainedEnumerator.Current;

                if (!currentResults)
                {
                    _resolvedChainedResults = null;
                    return false;
                }
            }
            else
            {
                resolvedInnerResults = _expression.Expression;
            }

            string filteredType = _expression.TargetResourceTypes.First();
            var includeExpressions = new List<IncludeExpression>();

            if (_expression.Reversed)
            {
                // When reversed we'll use the Include expression code to return the ids
                // in the search index on the matched resources
                foreach (var targetInclude in _expression.TargetResourceTypes)
                {
                    includeExpressions.Add(Expression.Include(
                        _expression.ResourceTypes,
                        _expression.ReferenceSearchParameter,
                        null,
                        targetInclude,
                        _expression.TargetResourceTypes,
                        false,
                        false,
                        false));
                }

                // When reversed the ids from the sub-query will match the base resource type
                filteredType = _expression.ResourceTypes.First();
            }

            MultiaryExpression filterExpression = Expression.And(
                Expression.SearchParameter(_resourceTypeSearchParameter, Expression.StringEquals(FieldName.TokenCode, null, filteredType, false)),
                resolvedInnerResults);

            _chainedOptions.Expression = filterExpression;

            (IReadOnlyList<FhirCosmosResourceWrapper> results, string continuationToken, int? maxConcurrency) results = await _searchFunc(
                _queryBuilder.BuildSqlQuerySpec(_chainedOptions, new QueryBuilderOptions(includeExpressions, projection: includeExpressions.Any() ? QueryProjection.ReferencesOnly : QueryProjection.IdAndType)),
                _chainedOptions,
                continuationToken: _continuationToken,
                cancellationToken: _cancellationToken,
                searchEnumerationTimeoutOverrideIfSequential: null,
                queryRequestOptionsOverride: null);

            var chainedResults = results.results;
            _continuationToken = results.continuationToken;

            if (!chainedResults.Any())
            {
                _resolvedChainedResults = null;
                return false;
            }

            if (_expression.Reversed)
            {
                // When reverse chained, we take the ids and types from the child object and use it to filter the parent objects.
                // e.g. Patient?_has:Group:member:_id=group1. In this case we would have run the query there Group.id = group1
                // and returned the indexed entries for Group.member. The following query will use these items to filter the parent Patient query.

                IEnumerable<ResourceTypeAndId> resourceTypeAndIds = chainedResults.SelectMany(x => x.ReferencesToInclude).Distinct();

                if (!resourceTypeAndIds.Any())
                {
                    _resolvedChainedResults = null;
                    return false;
                }

                IEnumerable<MultiaryExpression> typeAndResourceExpressions = resourceTypeAndIds
                    .GroupBy(x => x.ResourceTypeName)
                    .Select(g =>
                        Expression.And(
                            Expression.SearchParameter(_resourceTypeSearchParameter, Expression.Equals(FieldName.TokenCode, null, g.Key)),
                            Expression.SearchParameter(_resourceIdSearchParameter, Expression.In(FieldName.TokenCode, null, g.Select(x => x.ResourceId)))));

                _resolvedChainedResults = typeAndResourceExpressions.Count() == 1 ? typeAndResourceExpressions.First() : Expression.Or(typeAndResourceExpressions.ToArray());
            }

            // When normal chained expression we can filter using references in the parent object. e.g. Observation.subject
            // The following expression constrains "subject" references on "Observation" with the ids that have matched the sub-query

            _resolvedChainedResults = Expression.SearchParameter(
                _expression.ReferenceSearchParameter,
                Expression.Or(
                    chainedResults
                        .GroupBy(m => m.ResourceTypeName)
                        .Select(g =>
                            Expression.And(
                                Expression.Equals(FieldName.ReferenceResourceType, null, g.Key),
                                Expression.In(FieldName.ReferenceResourceId, null, g.Select(x => x.ResourceId)))).ToList()));

            return true;
        }

        public async ValueTask DisposeAsync()
        {
            if (_innerChainedEnumerator != null)
            {
                await _innerChainedEnumerator.DisposeAsync();
            }
        }
    }
}
