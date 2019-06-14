// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;
using static Hl7.Fhir.Model.SearchParameter;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers
{
    /// <summary>
    /// Provides mechanism to parse the search expression.
    /// </summary>
    public class ExpressionParser : IExpressionParser
    {
        private static readonly Dictionary<string, SearchModifierCode> SearchParamModifierMapping = Enum.GetNames(typeof(SearchModifierCode))
            .Select(e => (SearchModifierCode)Enum.Parse(typeof(SearchModifierCode), e))
            .ToDictionary(
                e => e.GetLiteral(),
                e => e,
                StringComparer.Ordinal);

        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISearchParameterExpressionParser _searchParameterExpressionParser;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExpressionParser"/> class.
        /// </summary>
        /// <param name="searchParameterDefinitionManager">The search parameter definition manager.</param>
        /// <param name="searchParameterExpressionParser">The parser used to parse the search value into a search expression.</param>
        public ExpressionParser(
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ISearchParameterExpressionParser searchParameterExpressionParser)
        {
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(searchParameterExpressionParser, nameof(searchParameterExpressionParser));

            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _searchParameterExpressionParser = searchParameterExpressionParser;
        }

        /// <summary>
        /// Parses the input into a corresponding search expression.
        /// </summary>
        /// <param name="resourceType">The resource type.</param>
        /// <param name="key">The query key.</param>
        /// <param name="value">The query value.</param>
        /// <returns>An instance of search expression representing the search.</returns>
        public Expression Parse(string resourceType, string key, string value)
        {
            EnsureArg.IsNotNullOrWhiteSpace(key, nameof(key));
            EnsureArg.IsNotNullOrWhiteSpace(value, nameof(value));

            return ParseImpl(resourceType, key.AsSpan(), value);
        }

        private Expression ParseImpl(string resourceType, ReadOnlySpan<char> key, string value)
        {
            if (TryConsume("_has:".AsSpan(), ref key))
            {
                if (!TrySplit(':', ref key, out var type))
                {
                    throw new Exception("missing type");
                }

                if (!TrySplit(':', ref key, out var refParam))
                {
                    throw new Exception("missing search reference search param");
                }

                // return ParseReverseChainedExpression()

                throw new NotImplementedException();
            }

            if (TrySplit('.', ref key, out var chainedInput))
            {
                ReadOnlySpan<char> targetTypeName;

                if (TrySplit(':', ref chainedInput, out var refParamName))
                {
                    targetTypeName = chainedInput;
                }
                else
                {
                    refParamName = chainedInput;
                    targetTypeName = ReadOnlySpan<char>.Empty;
                }

                if (refParamName.IsEmpty)
                {
                    throw new SearchParameterNotSupportedException(resourceType, key.ToString());
                }

                SearchParameterInfo refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter(resourceType, refParamName.ToString());

                return ParseChainedExpression(resourceType, refSearchParameter, targetTypeName.ToString(), key, value);
            }

            ReadOnlySpan<char> modifier;

            if (TrySplit(':', ref key, out var paramName))
            {
                modifier = key;
            }
            else
            {
                paramName = key;
                modifier = ReadOnlySpan<char>.Empty;
            }

            // Check to see if the search parameter is supported for this type or not.
            SearchParameterInfo searchParameter = _searchParameterDefinitionManager.GetSearchParameter(resourceType, paramName.ToString());

            return ParseSearchValueExpression(searchParameter, modifier.ToString(), value);
        }

        private Expression ParseChainedExpression(string resourceType, SearchParameterInfo searchParameter, string targetResourceType, ReadOnlySpan<char> remainingKey, string value)
        {
            // We have more paths after this so this is a chained expression.
            // Since this is chained expression, the expression must be a reference type.
            if (searchParameter.Type != ValueSets.SearchParamType.Reference)
            {
                // The search parameter is not a reference type, which is not allowed.
                throw new InvalidSearchOperationException(Core.Resources.ChainedParameterMustBeReferenceSearchParamType);
            }

            // Check to see if the client has specifically specified the target resource type to scope to.
            if (!string.IsNullOrEmpty(targetResourceType))
            {
                // A target resource type is specified.
                if (!Enum.TryParse<ResourceType>(targetResourceType, out _))
                {
                    throw new InvalidSearchOperationException(string.Format(Core.Resources.ResourceNotSupported, targetResourceType));
                }
            }

            ChainedExpression chainedExpression = null;

            foreach (var possibleTargetResourceType in searchParameter.TargetResourceTypes)
            {
                if (!string.IsNullOrEmpty(targetResourceType) && targetResourceType != possibleTargetResourceType)
                {
                    continue;
                }

                ChainedExpression expression;
                try
                {
                    expression = Expression.Chained(
                        resourceType,
                        searchParameter,
                        possibleTargetResourceType,
                        ParseImpl(
                            possibleTargetResourceType,
                            remainingKey,
                            value));
                }
                catch (Exception ex) when (ex is ResourceNotSupportedException || ex is SearchParameterNotSupportedException)
                {
                    // The resource or search parameter is not supported for the resource.
                    // We will ignore these unsupported types.
                    continue;
                }

                if (chainedExpression == null)
                {
                    chainedExpression = expression;
                }
                else
                {
                    // If the target resource type is ambiguous, we throw an error.
                    // At the moment, this is not supported

                    throw new InvalidSearchOperationException(
                        string.Format(
                            CultureInfo.CurrentCulture,
                            Core.Resources.ChainedParameterSpecifyType,
                            searchParameter.Name,
                            string.Join(Core.Resources.OrDelimiter, searchParameter.TargetResourceTypes.Select(c => $"{searchParameter.Name}:{c}"))));
                }
            }

            if (chainedExpression == null)
            {
                // There was no reference that supports the search parameter.
                throw new InvalidSearchOperationException(Core.Resources.ChainedParameterNotSupported);
            }

            return chainedExpression;
        }

        private Expression ParseSearchValueExpression(SearchParameterInfo searchParameter, string modifier, string value)
        {
            SearchModifierCode? parsedModifier = ParseSearchParamModifier();

            return _searchParameterExpressionParser.Parse(searchParameter, parsedModifier, value);

            SearchModifierCode? ParseSearchParamModifier()
            {
                if (string.IsNullOrEmpty(modifier))
                {
                    return null;
                }

                if (SearchParamModifierMapping.TryGetValue(modifier, out SearchModifierCode searchModifierCode))
                {
                    return searchModifierCode;
                }

                throw new InvalidSearchOperationException(
                    string.Format(Core.Resources.ModifierNotSupported, modifier, searchParameter.Name));
            }
        }

        private static bool TrySplit(char splitChar, ref ReadOnlySpan<char> input, out ReadOnlySpan<char> captured)
        {
            int splitIndex = input.IndexOf(splitChar);
            if (splitIndex < 0)
            {
                captured = ReadOnlySpan<char>.Empty;
                return false;
            }

            captured = input.Slice(0, splitIndex);
            Advance(ref input, splitIndex + 1);
            return true;
        }

        private static bool TryConsume(ReadOnlySpan<char> toConsume, ref ReadOnlySpan<char> input)
        {
            if (input.StartsWith(toConsume))
            {
                Advance(ref input, toConsume.Length);
                return true;
            }

            return false;
        }

        private static void Advance(ref ReadOnlySpan<char> input, int to)
        {
            if (input.Length > to)
            {
                input = input.Slice(to);
            }
            else
            {
                input = ReadOnlySpan<char>.Empty;
            }
        }
    }
}
