// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using static Hl7.Fhir.Model.SearchParameter;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.Expressions
{
    public class LegacySearchValueParser : ILegacySearchValueParser
    {
        private static readonly IEnumerable<Tuple<string, SearchComparator>> SearchParamComparators = Enum.GetNames(typeof(SearchComparator))
            .Select(e => (SearchComparator)Enum.Parse(typeof(SearchComparator), e))
            .Select(e => Tuple.Create(e.GetLiteral(), e));

        private static readonly Dictionary<string, SearchModifierCode> SearchParamModifierMapping = Enum.GetNames(typeof(SearchModifierCode))
            .Select(e => (SearchModifierCode)Enum.Parse(typeof(SearchModifierCode), e))
            .ToDictionary(
            e => e.GetLiteral(),
            e => e,
            StringComparer.Ordinal);

        private readonly ISearchParamDefinitionManager _searchParamDefinitionManager;
        private readonly ILegacySearchValueExpressionBuilder _searchValueExpressionBuilder;

        public LegacySearchValueParser(
            ISearchParamDefinitionManager searchParamDefinitionManager,
            ILegacySearchValueExpressionBuilder searchValueExpressionBuilder)
        {
            EnsureArg.IsNotNull(searchParamDefinitionManager, nameof(searchParamDefinitionManager));
            EnsureArg.IsNotNull(searchValueExpressionBuilder, nameof(searchValueExpressionBuilder));

            _searchParamDefinitionManager = searchParamDefinitionManager;
            _searchValueExpressionBuilder = searchValueExpressionBuilder;
        }

        public Expression Parse(SearchParam searchParam, string modifierOrResourceType, string value)
        {
            EnsureArg.IsNotNull(searchParam, nameof(searchParam));
            EnsureArg.IsNotNullOrWhiteSpace(value, nameof(value));

            SearchModifierCode? modifier = ParseSearchParamModifier();

            // Parse the comparator and the value.
            SearchParamType searchParamType = _searchParamDefinitionManager.GetSearchParamType(
                searchParam.ResourceType,
                searchParam.ParamName);

            string valueToParse = value;
            int comparatorIndex = 0;

            if (searchParam is CompositeSearchParam compositeSearchParam)
            {
                searchParamType = compositeSearchParam.UnderlyingSearchParamType;

                IReadOnlyList<string> compositeParts = value.SplitByCompositeSeparator();

                comparatorIndex = compositeParts[0].Length + 1;
            }

            // By default, the comparator is equal.
            SearchComparator comparator = SearchComparator.Eq;

            if (searchParamType == SearchParamType.Date ||
                searchParamType == SearchParamType.Number ||
                searchParamType == SearchParamType.Quantity)
            {
                // If the search parameter type supports comparator, parse the comparator (if present).
                Tuple<string, SearchComparator> matchedComparator = SearchParamComparators.FirstOrDefault(
                    s => value.IndexOf(s.Item1, comparatorIndex, StringComparison.Ordinal) == comparatorIndex);

                if (matchedComparator != null)
                {
                    comparator = matchedComparator.Item2;
                    valueToParse = value.Substring(0, comparatorIndex) + value.Substring(comparatorIndex + matchedComparator.Item1.Length);
                }
            }

            // If the value contains comma, then we need to convert it into in expression.
            // But in this case, the user cannot specify prefix.
            IReadOnlyList<string> parts = value.SplitByOrSeparator();

            if (parts.Count == 1)
            {
                // This is a single value expression.
                return _searchValueExpressionBuilder.Build(
                    searchParam,
                    modifier,
                    comparator,
                    valueToParse);
            }
            else
            {
                if (comparator != SearchComparator.Eq)
                {
                    throw new InvalidSearchOperationException(Core.Resources.SearchComparatorNotSupported);
                }

                // This is a multiple value expression.
                Expression[] expressions = parts.Select(item => _searchValueExpressionBuilder.Build(
                    searchParam,
                    modifier,
                    comparator,
                    item)).ToArray();

                return Expression.Or(expressions);
            }

            SearchModifierCode? ParseSearchParamModifier()
            {
                if (modifierOrResourceType == null)
                {
                    return null;
                }

                if (SearchParamModifierMapping.TryGetValue(modifierOrResourceType, out SearchModifierCode searchModifierCode))
                {
                    return searchModifierCode;
                }

                throw new InvalidSearchOperationException(
                    string.Format(Core.Resources.ModifierNotSupported, modifierOrResourceType, searchParam.ParamName));
            }
        }
    }
}
