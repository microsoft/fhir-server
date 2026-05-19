// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;
using Expression = Microsoft.Health.Fhir.Core.Features.Search.Expressions.Expression;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal static class SqlSearchQueryComplexityCalculator
    {
        private const int StandardThreshold = 30;
        private const int ComplexThreshold = 100;
        private const int BestEffortThreshold = 200;

        public static SqlSearchQueryComplexityResult Calculate(SearchOptions searchOptions)
        {
            EnsureArg.IsNotNull(searchOptions, nameof(searchOptions));

            QueryComplexityComponents components = Visit(searchOptions.Expression, includeIncludes: !searchOptions.CountOnly, allowResourceTypeConstraint: true);
            int score = components.Score;

            if (!components.HasResourceTypeConstraint && !components.HasCompartmentConstraint)
            {
                score += 50;
            }

            if (searchOptions.IncludeTotal == TotalType.Accurate)
            {
                score += 20;
            }

            if (HasNonLastUpdatedSort(searchOptions.Sort))
            {
                score += 25;
            }

            if (searchOptions.MaxItemCount > 100)
            {
                score += 10;
            }

            if (searchOptions.MaxItemCount >= 1000)
            {
                score += 30;
            }

            if (components.Includes.Count > 0 && searchOptions.IncludeCount >= 1000)
            {
                score += 30;
            }

            int includeGraphDepth = CalculateIncludeGraphDepth(components.Includes);
            score += includeGraphDepth * includeGraphDepth * 10;

            if (components.MaxChainDepth > 0)
            {
                score += components.MaxChainDepth * components.MaxChainDepth * 15;
            }

            return new SqlSearchQueryComplexityResult(score, GetTier(score));
        }

        private static SqlSearchQueryComplexityTier GetTier(int score)
        {
            if (score <= StandardThreshold)
            {
                return SqlSearchQueryComplexityTier.Standard;
            }

            if (score <= ComplexThreshold)
            {
                return SqlSearchQueryComplexityTier.Complex;
            }

            if (score <= BestEffortThreshold)
            {
                return SqlSearchQueryComplexityTier.BestEffort;
            }

            return SqlSearchQueryComplexityTier.Rejected;
        }

        private static QueryComplexityComponents Visit(Expression expression, bool includeIncludes, bool allowResourceTypeConstraint, int chainDepth = 0)
        {
            var components = new QueryComplexityComponents();
            if (expression == null)
            {
                return components;
            }

            switch (expression)
            {
                case MultiaryExpression multiaryExpression:
                    foreach (Expression childExpression in multiaryExpression.Expressions)
                    {
                        components.Add(Visit(childExpression, includeIncludes, allowResourceTypeConstraint, chainDepth));
                    }

                    break;
                case UnionExpression unionExpression:
                    foreach (Expression childExpression in unionExpression.Expressions)
                    {
                        components.Add(Visit(childExpression, includeIncludes, allowResourceTypeConstraint, chainDepth));
                    }

                    break;
                case NotExpression notExpression:
                    components.Add(Visit(notExpression.Expression, includeIncludes, allowResourceTypeConstraint: false, chainDepth));
                    break;
                case SearchParameterExpression searchParameterExpression:
                    components.Add(Visit(searchParameterExpression.Expression, includeIncludes, allowResourceTypeConstraint, chainDepth));
                    AddSearchParameterCost(searchParameterExpression.Parameter, searchParameterExpression.Expression, components, allowResourceTypeConstraint);
                    break;
                case MissingSearchParameterExpression missingSearchParameterExpression:
                    AddSearchParameterCost(missingSearchParameterExpression.Parameter, null, components, allowResourceTypeConstraint);
                    break;
                case ChainedExpression chainedExpression:
                    components.MaxChainDepth = Math.Max(components.MaxChainDepth, chainDepth + 1);
                    components.Add(Visit(chainedExpression.Expression, includeIncludes, allowResourceTypeConstraint, chainDepth + 1));
                    if (chainedExpression.TargetResourceTypes.Length != 1)
                    {
                        components.Score += 25;
                    }

                    break;
                case IncludeExpression includeExpression:
                    if (!includeIncludes)
                    {
                        break;
                    }

                    components.Includes.Add(includeExpression);
                    components.Score += 20;
                    if (includeExpression.Iterate)
                    {
                        components.Score += 40;
                    }

                    if (includeExpression.WildCard)
                    {
                        components.Score += 100;
                    }

                    if (IsUntypedReferenceTarget(includeExpression))
                    {
                        components.Score += 25;
                    }

                    break;
                case CompartmentSearchExpression:
                    components.HasCompartmentConstraint = true;
                    break;
            }

            return components;
        }

        private static void AddSearchParameterCost(SearchParameterInfo searchParameter, Expression expression, QueryComplexityComponents components, bool allowResourceTypeConstraint)
        {
            if (IsResourceTypeParameter(searchParameter))
            {
                if (allowResourceTypeConstraint)
                {
                    components.HasResourceTypeConstraint = true;
                    return;
                }
            }

            components.Score += searchParameter.Code == SearchParameterNames.Id
                ? 1
                : ScoreBySearchParameterType(searchParameter, expression);

            if (IsUntypedReferenceSearch(searchParameter, expression))
            {
                components.Score += 25;
            }
        }

        private static int ScoreBySearchParameterType(SearchParameterInfo searchParameter, Expression expression)
        {
            return searchParameter.Type switch
            {
                SearchParamType.Token => 3,
                SearchParamType.Reference => 3,
                SearchParamType.Date => 5,
                SearchParamType.String => HasExpensiveStringOperator(expression) ? 20 : 10,
                SearchParamType.Number => 5,
                SearchParamType.Quantity => 5,
                SearchParamType.Uri => 5,
                SearchParamType.Composite => 10,
                SearchParamType.Special => 20,
                _ => 10,
            };
        }

        private static bool HasExpensiveStringOperator(Expression expression)
        {
            switch (expression)
            {
                case StringExpression stringExpression:
                    return stringExpression.StringOperator is StringOperator.Contains or StringOperator.NotContains or StringOperator.EndsWith or StringOperator.NotEndsWith;
                case MultiaryExpression multiaryExpression:
                    return multiaryExpression.Expressions.Any(HasExpensiveStringOperator);
                case UnionExpression unionExpression:
                    return unionExpression.Expressions.Any(HasExpensiveStringOperator);
                case NotExpression notExpression:
                    return HasExpensiveStringOperator(notExpression.Expression);
                default:
                    return false;
            }
        }

        private static bool ContainsField(Expression expression, FieldName fieldName)
        {
            switch (expression)
            {
                case BinaryExpression binaryExpression:
                    return binaryExpression.FieldName == fieldName;
                case StringExpression stringExpression:
                    return stringExpression.FieldName == fieldName;
                case MissingFieldExpression missingFieldExpression:
                    return missingFieldExpression.FieldName == fieldName;
                case MultiaryExpression multiaryExpression:
                    return multiaryExpression.Expressions.Any(childExpression => ContainsField(childExpression, fieldName));
                case UnionExpression unionExpression:
                    return unionExpression.Expressions.Any(childExpression => ContainsField(childExpression, fieldName));
                case NotExpression notExpression:
                    return ContainsField(notExpression.Expression, fieldName);
                default:
                    return false;
            }
        }

        private static bool IsResourceTypeParameter(SearchParameterInfo searchParameter)
        {
            return string.Equals(searchParameter.Code, SearchParameterNames.ResourceType, StringComparison.Ordinal);
        }

        private static bool HasNonLastUpdatedSort(IReadOnlyList<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)> sort)
        {
            return sort?.Any(x =>
                !string.Equals(x.searchParameterInfo.Code, SearchParameterNames.LastUpdated, StringComparison.Ordinal) &&
                !string.Equals(x.searchParameterInfo.Code, SearchParameterNames.ResourceType, StringComparison.Ordinal)) == true;
        }

        private static bool IsUntypedReferenceTarget(IncludeExpression includeExpression)
        {
            return !includeExpression.WildCard &&
                includeExpression.TargetResourceType == null &&
                (includeExpression.ReferenceSearchParameter?.TargetResourceTypes?.Count ?? 0) != 1;
        }

        private static bool IsUntypedReferenceSearch(SearchParameterInfo searchParameter, Expression expression)
        {
            return searchParameter.Type == SearchParamType.Reference &&
                (searchParameter.TargetResourceTypes?.Count ?? 0) != 1 &&
                !ContainsField(expression, FieldName.ReferenceResourceType);
        }

        private static int CalculateIncludeGraphDepth(List<IncludeExpression> includes)
        {
            if (includes.Count == 0)
            {
                return 0;
            }

            var resourceDepths = new Dictionary<string, int>(StringComparer.Ordinal);
            var maxDepth = 0;
            for (var i = 0; i < includes.Count; i++)
            {
                var changed = false;
                foreach (IncludeExpression include in includes)
                {
                    int predecessorDepth = include.Requires.Select(resourceType => resourceDepths.TryGetValue(resourceType, out int depth) ? depth : 1).DefaultIfEmpty(1).Max();
                    int producedDepth = predecessorDepth + 1;
                    foreach (string producedResourceType in include.Produces)
                    {
                        if (!resourceDepths.TryGetValue(producedResourceType, out int existingDepth) || producedDepth > existingDepth)
                        {
                            resourceDepths[producedResourceType] = producedDepth;
                            maxDepth = Math.Max(maxDepth, producedDepth);
                            changed = true;
                        }
                    }
                }

                if (!changed)
                {
                    break;
                }
            }

            return maxDepth;
        }

        private sealed class QueryComplexityComponents
        {
            public int Score { get; set; }

            public bool HasResourceTypeConstraint { get; set; }

            public bool HasCompartmentConstraint { get; set; }

            public int MaxChainDepth { get; set; }

            public List<IncludeExpression> Includes { get; } = new List<IncludeExpression>();

            public void Add(QueryComplexityComponents other)
            {
                Score += other.Score;
                HasResourceTypeConstraint |= other.HasResourceTypeConstraint;
                HasCompartmentConstraint |= other.HasCompartmentConstraint;
                MaxChainDepth = Math.Max(MaxChainDepth, other.MaxChainDepth);
                Includes.AddRange(other.Includes);
            }
        }
    }
}
