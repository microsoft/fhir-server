// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Utility;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers
{
    /// <summary>
    /// A builder used to build expression from the search value.
    /// </summary>
    public class SearchParameterExpressionParser : ISearchParameterExpressionParser
    {
        private static readonly Tuple<string, SearchComparator>[] SearchParamComparators = Enum.GetValues(typeof(SearchComparator))
            .Cast<SearchComparator>()
            .Select(e => Tuple.Create(e.GetLiteral(), e)).ToArray();

        private readonly Dictionary<SearchParamType, Func<string, ISearchValue>> _parserDictionary;

        public SearchParameterExpressionParser(IReferenceSearchValueParser referenceSearchValueParser)
        {
            EnsureArg.IsNotNull(referenceSearchValueParser, nameof(referenceSearchValueParser));

            _parserDictionary = new (SearchParamType type, Func<string, ISearchValue> parser)[]
                {
                    (SearchParamType.Date, DateTimeSearchValue.Parse),
                    (SearchParamType.Number, NumberSearchValue.Parse),
                    (SearchParamType.Quantity, QuantitySearchValue.Parse),
                    (SearchParamType.Reference, referenceSearchValueParser.Parse),
                    (SearchParamType.String, StringSearchValue.Parse),
                    (SearchParamType.Token, TokenSearchValue.Parse),
                    (SearchParamType.Uri, str => UriSearchValue.Parse(str, false, ModelInfoProvider.Instance)),
                }
                .ToDictionary(entry => entry.type, entry => CreateParserWithErrorHandling(entry.parser));
        }

        public Expression Parse(
            SearchParameterInfo searchParameter,
            SearchModifier modifier,
            string value)
        {
            EnsureArg.IsNotNull(searchParameter, nameof(searchParameter));
            EnsureArg.IsNotNullOrWhiteSpace(value, nameof(value));

            Expression outputExpression;

            if (modifier?.SearchModifierCode == SearchModifierCode.Missing)
            {
                // We have to handle :missing modifier specially because if :missing modifier is specified,
                // then the value is a boolean string indicating whether the parameter is missing or not instead of
                // the search value type associated with the search parameter.
                if (!bool.TryParse(value, out bool isMissing))
                {
                    // An invalid value was specified.
                    throw new InvalidSearchOperationException(Resources.InvalidValueTypeForMissingModifier);
                }

                return Expression.MissingSearchParameter(searchParameter, isMissing);
            }

            if (modifier?.SearchModifierCode == SearchModifierCode.Text)
            {
                // We have to handle :text modifier specially because if :text modifier is supplied for token search param,
                // then we want to search the display text using the specified text, and therefore
                // we don't want to actually parse the specified text into token.
                if (searchParameter.Type != SearchParamType.Token)
                {
                    throw new InvalidSearchOperationException(
                        string.Format(CultureInfo.InvariantCulture, Resources.ModifierNotSupported, modifier, searchParameter.Code));
                }

                outputExpression = Expression.StartsWith(FieldName.TokenText, null, value, true);
            }
            else
            {
                // Build the expression for based on the search value.
                if (searchParameter.Type == SearchParamType.Composite)
                {
                    if (modifier != null)
                    {
                        throw new InvalidSearchOperationException(
                            string.Format(CultureInfo.InvariantCulture, Resources.ModifierNotSupported, modifier, searchParameter.Code));
                    }

                    IReadOnlyList<string> orParts = value.SplitByOrSeparator();
                    var orExpressions = new Expression[orParts.Count];
                    for (int orIndex = 0; orIndex < orParts.Count; orIndex++)
                    {
                        IReadOnlyList<string> compositeValueParts = orParts[orIndex].SplitByCompositeSeparator();

                        if (compositeValueParts.Count > searchParameter.Component.Count)
                        {
                            throw new InvalidSearchOperationException(
                                string.Format(CultureInfo.InvariantCulture, Resources.NumberOfCompositeComponentsExceeded, searchParameter.Code));
                        }

                        var compositeExpressions = new Expression[compositeValueParts.Count];

                        for (int componentIndex = 0; componentIndex < compositeValueParts.Count; componentIndex++)
                        {
                            // Find the corresponding search parameter info.
                            SearchParameterInfo componentSearchParameter = searchParameter.Component[componentIndex].ResolvedSearchParameter;

                            string componentValue = compositeValueParts[componentIndex];

                            compositeExpressions[componentIndex] = Build(
                                componentSearchParameter,
                                modifier: null,
                                componentIndex: componentIndex,
                                value: componentValue);
                        }

                        orExpressions[orIndex] = Expression.And(compositeExpressions);
                    }

                    outputExpression = orExpressions.Length == 1 ? orExpressions[0] : Expression.Or(orExpressions);
                }
                else
                {
                    outputExpression = Build(
                        searchParameter,
                        modifier,
                        componentIndex: null,
                        value: value);
                }
            }

            return Expression.SearchParameter(searchParameter, outputExpression);
        }

        private Expression Build(
            SearchParameterInfo searchParameter,
            SearchModifier modifier,
            int? componentIndex,
            string value)
        {
            ReadOnlySpan<char> valueSpan = value.AsSpan();

            // By default, the comparator is equal.
            SearchComparator comparator = SearchComparator.Eq;

            if (searchParameter.Type == SearchParamType.Date ||
                searchParameter.Type == SearchParamType.Number ||
                searchParameter.Type == SearchParamType.Quantity)
            {
                // If the search parameter type supports comparator, parse the comparator (if present).
                Tuple<string, SearchComparator> matchedComparator = SearchParamComparators.FirstOrDefault(
                    s => value.StartsWith(s.Item1, StringComparison.Ordinal));

                if (matchedComparator != null)
                {
                    comparator = matchedComparator.Item2;
                    valueSpan = valueSpan.Slice(matchedComparator.Item1.Length);
                }
            }

            // Parse the value.
            Func<string, ISearchValue> parser = _parserDictionary[Enum.Parse<SearchParamType>(searchParameter.Type.ToString())];

            // Build the expression.
            var helper = new SearchValueExpressionBuilderHelper();

            // If the value contains comma, then we need to convert it into in expression.
            // But in this case, the user cannot specify prefix.
            IReadOnlyList<string> parts = value.SplitByOrSeparator();

            if (parts.Count == 1)
            {
                // This is a single value expression.
                ISearchValue searchValue = parser(valueSpan.ToString());
                searchValue = ApplyTargetTypeModifier(modifier, searchValue);

                return helper.Build(
                    searchParameter.Code,
                    modifier,
                    comparator,
                    componentIndex,
                    searchValue);
            }
            else
            {
                if (comparator != SearchComparator.Eq)
                {
                    throw new InvalidSearchOperationException(Resources.SearchComparatorNotSupported);
                }

                // This is a multiple value expression.
                if (modifier?.SearchModifierCode == SearchModifierCode.Not)
                {
                    Expression[] expressions = parts.Select(part =>
                    {
                        ISearchValue searchValue = parser(part);

                        return helper.Build(
                            searchParameter.Code,
                            null,
                            comparator,
                            componentIndex,
                            searchValue);
                    }).ToArray();

                    return Expression.Not(Expression.Or(expressions));
                }
                else
                {
                    Expression[] expressions = parts.Select(part =>
                    {
                        ISearchValue searchValue = parser(part);
                        searchValue = ApplyTargetTypeModifier(modifier, searchValue);

                        return helper.Build(
                            searchParameter.Code,
                            modifier,
                            comparator,
                            componentIndex,
                            searchValue);
                    }).ToArray();

                    return Expression.Or(expressions);
                }
            }

            ISearchValue ApplyTargetTypeModifier(SearchModifier modifier, ISearchValue source)
            {
                var referenceSearchValue = source as ReferenceSearchValue;
                if (referenceSearchValue == null || modifier?.SearchModifierCode != SearchModifierCode.Type)
                {
                    return source;
                }

                if (!string.IsNullOrEmpty(referenceSearchValue.ResourceType))
                {
                    if (string.Equals(referenceSearchValue.ResourceType, modifier.ResourceType, StringComparison.OrdinalIgnoreCase))
                    {
                        return source;
                    }

                    throw new InvalidSearchOperationException(
                        string.Format(Core.Resources.ModifierNotSupported, modifier, searchParameter.Code));
                }

                try
                {
                    return new ReferenceSearchValue(
                        referenceSearchValue.Kind,
                        referenceSearchValue.BaseUri,
                        modifier.ResourceType,
                        referenceSearchValue.ResourceId);
                }
                catch (ArgumentException)
                {
                    throw new InvalidSearchOperationException(
                        string.Format(Core.Resources.ModifierNotSupported, modifier, searchParameter.Code));
                }
            }
        }

        private static Func<string, ISearchValue> CreateParserWithErrorHandling(Func<string, ISearchValue> parser) =>
            input =>
            {
                try
                {
                    return parser(input);
                }
                catch (FormatException e)
                {
                    throw new BadRequestException(e.Message);
                }
                catch (OverflowException e)
                {
                    throw new BadRequestException(e.Message);
                }
                catch (ArgumentException e)
                {
                    throw new BadRequestException(e.Message);
                }
            };
    }
}
