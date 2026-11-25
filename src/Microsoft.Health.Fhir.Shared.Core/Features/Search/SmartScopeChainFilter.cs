// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Models;
using Expression = Microsoft.Health.Fhir.Core.Features.Search.Expressions.Expression;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Applies SMART v2 scope search-parameter filters to chained and reverse-chained (_has) searches.
    /// </summary>
    internal static class SmartScopeChainFilter
    {
        /// <summary>
        /// Applies SMART v2 scope search-parameter filters to the resources reached through chained and
        /// reverse-chained (_has) searches. Without this, a chain could match far-side resources that the
        /// granted scope's filter excludes (e.g. a Condition.rs?category=encounter-diagnosis token matching
        /// any Condition through _has). When a filter cannot be enforced inside the chain, the request fails closed.
        /// </summary>
        /// <param name="searchExpressions">The parsed search expressions; chained expressions are rewritten in place.</param>
        /// <param name="grantedScopes">The SMART scopes granting the requested data actions.</param>
        /// <param name="expressionParser">The parser used to build scope filter expressions.</param>
        /// <param name="searchParameterDefinitionManager">The search parameter definitions used to inspect scope filter types.</param>
        internal static void Apply(
            List<Expression> searchExpressions,
            IReadOnlyCollection<ScopeRestriction> grantedScopes,
            IExpressionParser expressionParser,
            ISearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            // An unfiltered wildcard scope grants every resource of every type, so there is nothing to apply.
            if (grantedScopes.Any(r => r.Resource == KnownResourceTypes.All && !SearchOptionsFactory.HasScopeSearchParameters(r)))
            {
                return;
            }

            var context = new ChainFilterContext(grantedScopes, expressionParser, searchParameterDefinitionManager);
            for (int i = 0; i < searchExpressions.Count; i++)
            {
                searchExpressions[i] = ApplySmartScopeFiltersToChains(searchExpressions[i], context);
            }
        }

        private static Expression ApplySmartScopeFiltersToChains(Expression expression, ChainFilterContext context)
        {
            switch (expression)
            {
                case ChainedExpression chained:
                    return ApplySmartScopeFiltersToChain(chained, context);

                // Scope-injected expressions come from the token itself and never contain chains.
                case MultiaryExpression multiary when !multiary.IsSmartV2UnionExpressionForScopesSearchParameters:
                    var rewritten = multiary.Expressions.Select(e => ApplySmartScopeFiltersToChains(e, context)).ToList();
                    return rewritten.SequenceEqual(multiary.Expressions)
                        ? multiary
                        : new MultiaryExpression(multiary.MultiaryOperation, rewritten);

                default:
                    return expression;
            }
        }

        private static ChainedExpression ApplySmartScopeFiltersToChain(ChainedExpression chained, ChainFilterContext context)
        {
            Expression inner = ApplySmartScopeFiltersToChains(chained.Expression, context);

            // The far side of a forward chain is its target; for _has it is the referencing resource type.
            string[] farSideTypes = chained.Reversed ? chained.ResourceTypes : chained.TargetResourceTypes;

            IReadOnlyList<Tuple<string, string>> filteringParameters = null;
            foreach (string farSideType in farSideTypes)
            {
                IReadOnlyList<Tuple<string, string>> parameters = GetChainFilteringParameters(farSideType, context);
                if (parameters == null)
                {
                    continue;
                }

                // A chain spanning several far-side types would need a different filter per type, which a
                // single chain expression cannot express.
                if (farSideTypes.Length > 1)
                {
                    throw new InvalidSearchOperationException(
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.SmartScopeChainedSearchNotEnforceable, farSideType));
                }

                filteringParameters = parameters;
            }

            if (filteringParameters == null)
            {
                return ReferenceEquals(inner, chained.Expression)
                    ? chained
                    : new ChainedExpression(chained.ResourceTypes, chained.ReferenceSearchParameter, chained.TargetResourceTypes, chained.Reversed, inner);
            }

            string targetType = farSideTypes[0];

            // Backends evaluate each AND child of a chain link as a separate predicate on the same resource; a nested
            // chain at this level is a further link and cannot be combined with predicates on this link.
            if (ContainsChain(inner))
            {
                throw new InvalidSearchOperationException(
                    string.Format(CultureInfo.InvariantCulture, Core.Resources.SmartScopeChainedSearchNotEnforceable, targetType));
            }

            List<Expression> filters = BuildChainScopeFilters(filteringParameters, targetType, context.ExpressionParser);

            var children = new List<Expression>();
            if (inner is MultiaryExpression { MultiaryOperation: MultiaryOperator.And } innerAnd)
            {
                children.AddRange(innerAnd.Expressions);
            }
            else
            {
                children.Add(inner);
            }

            children.AddRange(filters);

            return new ChainedExpression(
                chained.ResourceTypes,
                chained.ReferenceSearchParameter,
                chained.TargetResourceTypes,
                chained.Reversed,
                Expression.And(children.ToArray()));
        }

        /// <summary>
        /// Returns the scope filter parameters that govern <paramref name="resourceType"/> inside a chain, or null when
        /// the type is either granted without a filter or not granted at all (the latter is denied by the chained type check).
        /// As in direct search (ADR 2609), a filtered wildcard scope is mandatory: it governs the chain link even when a
        /// resource-specific scope also grants the type, and the request is denied if its filter cannot be enforced.
        /// Otherwise, several filtered resource-specific scopes are alternatives (OR); one whose filter is unavailable is
        /// withdrawn, and the request is denied only when no scope is left.
        /// Chain links only support ANDed predicates, so alternatives are combined only when each filters on the same
        /// single parameter, as one comma-separated (OR) value; otherwise the request is rejected. Modified or chained
        /// scope parameters (e.g. category:not) are always rejected, because a chain link evaluates each predicate per
        /// indexed value rather than per resource.
        /// </summary>
        private static IReadOnlyList<Tuple<string, string>> GetChainFilteringParameters(string resourceType, ChainFilterContext context)
        {
            var applicable = context.GrantedScopes
                .Where(r => r.Resource == resourceType || r.Resource == KnownResourceTypes.All)
                .ToList();

            if (applicable.Count == 0)
            {
                return null;
            }

            // An unfiltered wildcard has already returned early, so every wildcard here carries a filter.
            var wildcardParameters = new List<List<Tuple<string, string>>>();
            foreach (ScopeRestriction restriction in applicable.Where(r => r.Resource == KnownResourceTypes.All))
            {
                List<Tuple<string, string>> parameters = restriction.SearchParameters.Parameters.ToList();
                string unusableReason = GetChainScopeUnusableReason(parameters, resourceType, context.ExpressionParser);
                if (unusableReason != null)
                {
                    // A wildcard constraint applies to every scope, so it cannot be withdrawn without widening access.
                    throw new InvalidSearchOperationException(unusableReason);
                }

                wildcardParameters.Add(parameters);
            }

            if (wildcardParameters.Count > 0)
            {
                return MergeChainScopeAlternatives(wildcardParameters, resourceType, context.SearchParameterDefinitionManager);
            }

            if (applicable.Any(r => !SearchOptionsFactory.HasScopeSearchParameters(r)))
            {
                return null;
            }

            var scopeParameters = new List<List<Tuple<string, string>>>();
            string withdrawnReason = null;
            foreach (ScopeRestriction restriction in applicable)
            {
                List<Tuple<string, string>> parameters = restriction.SearchParameters.Parameters.ToList();
                string unusableReason = GetChainScopeUnusableReason(parameters, resourceType, context.ExpressionParser);

                if (unusableReason == null)
                {
                    scopeParameters.Add(parameters);
                }
                else
                {
                    withdrawnReason ??= unusableReason;
                }
            }

            if (scopeParameters.Count == 0)
            {
                throw new InvalidSearchOperationException(withdrawnReason);
            }

            return MergeChainScopeAlternatives(scopeParameters, resourceType, context.SearchParameterDefinitionManager);
        }

        /// <summary>
        /// Combines alternative scope filters for <paramref name="resourceType"/> into the parameters of one chain link,
        /// or denies the request when they cannot be combined.
        /// </summary>
        private static IReadOnlyList<Tuple<string, string>> MergeChainScopeAlternatives(
            List<List<Tuple<string, string>>> scopeParameters,
            string resourceType,
            ISearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            // Identical grants are the same alternative and must not be treated as distinct ones that need merging.
            // Compare the parameter pairs themselves: decoded values may contain '&' or '=', so a joined string is ambiguous.
            var distinctSorted = new List<List<Tuple<string, string>>>();
            var distinctParameters = new List<List<Tuple<string, string>>>();
            foreach (List<Tuple<string, string>> parameters in scopeParameters)
            {
                List<Tuple<string, string>> sorted = parameters
                    .OrderBy(p => p.Item1, StringComparer.Ordinal)
                    .ThenBy(p => p.Item2, StringComparer.Ordinal)
                    .ToList();

                if (!distinctSorted.Any(d => d.SequenceEqual(sorted)))
                {
                    distinctSorted.Add(sorted);
                    distinctParameters.Add(parameters);
                }
            }

            scopeParameters = distinctParameters;

            if (scopeParameters.Count == 1)
            {
                return scopeParameters[0];
            }

            string key = scopeParameters[0][0].Item1;

            bool canMerge = scopeParameters.All(p => p.Count == 1 && string.Equals(p[0].Item1, key, StringComparison.Ordinal)) &&
                !scopeParameters.Any(p => EndsWithDanglingEscape(p[0].Item2)) &&
                !HasComparatorValue(searchParameterDefinitionManager, resourceType, key, scopeParameters.Select(p => p[0].Item2));

            if (!canMerge)
            {
                throw new InvalidSearchOperationException(
                    string.Format(CultureInfo.InvariantCulture, Core.Resources.SmartScopeChainedSearchNotEnforceable, resourceType));
            }

            string mergedValue = string.Join(",", scopeParameters.Select(p => p[0].Item2).Distinct(StringComparer.Ordinal));
            return new[] { Tuple.Create(key, mergedValue) };
        }

        /// <summary>
        /// Returns why one scope's filter cannot be enforced on a chain link for <paramref name="resourceType"/>, or null when it can.
        /// Resource-level parameters (_id, _lastUpdated, _type) are not evaluated as per-link predicates inside a chain by
        /// every backend.
        /// </summary>
        private static string GetChainScopeUnusableReason(IReadOnlyList<Tuple<string, string>> parameters, string resourceType, IExpressionParser expressionParser)
        {
            if (parameters.Any(param => param.Item1.Contains(':', StringComparison.Ordinal) || param.Item1.Contains('.', StringComparison.Ordinal)))
            {
                return string.Format(CultureInfo.InvariantCulture, Core.Resources.SmartScopeChainedSearchNotEnforceable, resourceType);
            }

            var filters = new List<Expression>();
            foreach (Tuple<string, string> param in parameters)
            {
                if (!SearchOptionsFactory.TryParseScopeParameter(expressionParser, param, resourceType, filters, out string unenforceableParameter))
                {
                    return string.Format(CultureInfo.InvariantCulture, Core.Resources.SmartScopeSearchParameterNotEnforceable, unenforceableParameter);
                }
            }

            if (filters.Any(IsResourceLevelParameter))
            {
                return string.Format(CultureInfo.InvariantCulture, Core.Resources.SmartScopeChainedSearchNotEnforceable, resourceType);
            }

            return null;
        }

        /// <summary>
        /// A value ending in an unmatched escape character would escape the comma added when values are merged,
        /// turning the alternatives into a single different value.
        /// </summary>
        private static bool EndsWithDanglingEscape(string value)
        {
            int trailingEscapes = 0;
            for (int i = value.Length - 1; i >= 0 && value[i] == '\\'; i--)
            {
                trailingEscapes++;
            }

            return trailingEscapes % 2 == 1;
        }

        /// <summary>
        /// Comparator prefixes (e.g. ge, lt) cannot be combined with a comma-separated (OR) value, so scopes whose
        /// values use them cannot be merged.
        /// </summary>
        private static bool HasComparatorValue(
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            string resourceType,
            string parameterCode,
            IEnumerable<string> values)
        {
            if (!searchParameterDefinitionManager.TryGetSearchParameter(resourceType, parameterCode, out SearchParameterInfo searchParameter) ||
                (searchParameter.Type != ValueSets.SearchParamType.Date &&
                 searchParameter.Type != ValueSets.SearchParamType.Number &&
                 searchParameter.Type != ValueSets.SearchParamType.Quantity))
            {
                return false;
            }

            return values
                .SelectMany(v => v.SplitByOrSeparator())
                .Any(v => Enum.GetValues<ValueSets.SearchComparator>().Any(c => v.StartsWith(c.GetLiteral(), StringComparison.Ordinal)));
        }

        private static List<Expression> BuildChainScopeFilters(IReadOnlyList<Tuple<string, string>> parameters, string targetResourceType, IExpressionParser expressionParser)
        {
            var filters = new List<Expression>();
            foreach (Tuple<string, string> param in parameters)
            {
                if (!SearchOptionsFactory.TryParseScopeParameter(expressionParser, param, targetResourceType, filters, out string unenforceableParameter))
                {
                    throw new InvalidSearchOperationException(
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.SmartScopeSearchParameterNotEnforceable, unenforceableParameter));
                }
            }

            return filters;
        }

        private static bool IsResourceLevelParameter(Expression expression)
        {
            return expression is SearchParameterExpressionBase { Parameter.Code: SearchParameterNames.Id or SearchParameterNames.LastUpdated or SearchParameterNames.ResourceType };
        }

        private static bool ContainsChain(Expression expression)
        {
            return expression switch
            {
                ChainedExpression => true,
                MultiaryExpression multiary => multiary.Expressions.Any(ContainsChain),
                UnionExpression union => union.Expressions.Any(ContainsChain),
                NotExpression not => ContainsChain(not.Expression),
                _ => false,
            };
        }

        private sealed record ChainFilterContext(
            IReadOnlyCollection<ScopeRestriction> GrantedScopes,
            IExpressionParser ExpressionParser,
            ISearchParameterDefinitionManager SearchParameterDefinitionManager);
    }
}
