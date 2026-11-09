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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers
{
    /// <summary>
    /// A builder used to build expression from the search value.
    /// </summary>
    public class SearchParameterExpressionParser : ISearchParameterExpressionParser
    {
        private static readonly Tuple<string, SearchComparator>[] SearchParamComparators = Enum.GetValues<SearchComparator>()
            .Cast<SearchComparator>()
            .Select(e => Tuple.Create(e.GetLiteral(), e)).ToArray();

        private readonly Dictionary<SearchParamType, Func<string, ISearchValue>> _parserDictionary;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly ResourceIdPolicy _resourceIdPolicy;
        private readonly ILogger<SearchParameterExpressionParser> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchParameterExpressionParser"/> class that doesn't report
        /// reference values with resource ids longer than the maximum resource id length.
        /// </summary>
        /// <param name="referenceSearchValueParser">The reference search value parser.</param>
        public SearchParameterExpressionParser(IReferenceSearchValueParser referenceSearchValueParser)
            : this(referenceSearchValueParser, contextAccessor: null, ResourceIdPolicy.Standard, NullLogger<SearchParameterExpressionParser>.Instance)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchParameterExpressionParser"/> class.
        /// </summary>
        /// <param name="referenceSearchValueParser">The reference search value parser.</param>
        /// <param name="contextAccessor">The request context accessor, used to add search warnings to the response. Can be <c>null</c>.</param>
        /// <param name="resourceIdPolicy">The resource id policy, used for the maximum resource id length in the warning.</param>
        /// <param name="logger">The logger.</param>
        public SearchParameterExpressionParser(
            IReferenceSearchValueParser referenceSearchValueParser,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            ResourceIdPolicy resourceIdPolicy,
            ILogger<SearchParameterExpressionParser> logger)
        {
            EnsureArg.IsNotNull(referenceSearchValueParser, nameof(referenceSearchValueParser));
            EnsureArg.IsNotNull(resourceIdPolicy, nameof(resourceIdPolicy));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _contextAccessor = contextAccessor;
            _resourceIdPolicy = resourceIdPolicy;
            _logger = logger;

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
                    throw new InvalidSearchOperationException(Core.Resources.InvalidValueTypeForMissingModifier);
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
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.ModifierNotSupported, modifier, searchParameter.Code));
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
                            string.Format(CultureInfo.InvariantCulture, Core.Resources.ModifierNotSupported, modifier, searchParameter.Code));
                    }

                    IReadOnlyList<string> orParts = value.SplitByOrSeparator();
                    var orExpressions = new Expression[orParts.Count];
                    for (int orIndex = 0; orIndex < orParts.Count; orIndex++)
                    {
                        IReadOnlyList<string> compositeValueParts = orParts[orIndex].SplitByCompositeSeparator();

                        if (compositeValueParts.Count > searchParameter.Component.Count)
                        {
                            throw new InvalidSearchOperationException(
                                string.Format(CultureInfo.InvariantCulture, Core.Resources.NumberOfCompositeComponentsExceeded, searchParameter.Code));
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
                                value: componentValue,
                                reportedParameterCode: searchParameter.Code);
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
                        value: value,
                        reportedParameterCode: searchParameter.Code);
                }
            }

            return Expression.SearchParameter(searchParameter, outputExpression);
        }

        private Expression Build(
            SearchParameterInfo searchParameter,
            SearchModifier modifier,
            int? componentIndex,
            string value,
            string reportedParameterCode)
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
                ReportOverlongReferenceId(reportedParameterCode, searchValue);
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
                    throw new InvalidSearchOperationException(Core.Resources.SearchComparatorNotSupported);
                }

                // This is a multiple value expression.
                if (modifier?.SearchModifierCode == SearchModifierCode.Not)
                {
                    Expression[] expressions = parts.Select(part =>
                    {
                        ISearchValue searchValue = parser(part);
                        ReportOverlongReferenceId(reportedParameterCode, searchValue);

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
                        ReportOverlongReferenceId(reportedParameterCode, searchValue);
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
                        referenceSearchValue.ResourceId)
                    {
                        OverlongResourceIdLength = referenceSearchValue.OverlongResourceIdLength,
                        MaxResourceIdLength = referenceSearchValue.MaxResourceIdLength,
                    };
                }
                catch (ArgumentException)
                {
                    throw new InvalidSearchOperationException(
                        string.Format(Core.Resources.ModifierNotSupported, modifier, searchParameter.Code));
                }
            }
        }

        /// <summary>
        /// Logs a reference search value whose resource id is longer than the maximum resource id length, and adds a warning
        /// for its search parameter to the response. The search still runs; this is a documented limitation.
        /// This covers every search that is parsed here: regular and chained searches, SMART scope restrictions,
        /// conditional create/update/delete, bulk delete/update, and search entries in bundles.
        /// </summary>
        /// <param name="searchParameterCode">The code of the search parameter the value belongs to.</param>
        /// <param name="searchValue">The parsed search value.</param>
        private void ReportOverlongReferenceId(string searchParameterCode, ISearchValue searchValue)
        {
            if (searchValue is not ReferenceSearchValue { OverlongResourceIdLength: int length })
            {
                return;
            }

            IList<OperationOutcomeIssue> bundleIssues = _contextAccessor?.RequestContext?.BundleIssues;

            // A request can parse the same search several times (for example per page, or per bulk job batch), so warn and log once.
            if (bundleIssues != null && !LongReferenceIdIssue.TryAdd(bundleIssues, searchParameterCode, _resourceIdPolicy.MaxLength))
            {
                return;
            }

            // The value isn't logged because it may contain sensitive data.
            _logger.LogWarning(
                "Search used a reference whose resource id is longer than the maximum resource id length. SearchParameter={SearchParameter}, Length={Length}, MaxLength={MaxLength}",
                searchParameterCode,
                length,
                _resourceIdPolicy.MaxLength);
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
