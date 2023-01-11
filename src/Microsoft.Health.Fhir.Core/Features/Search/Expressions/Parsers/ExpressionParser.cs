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
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

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
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;

        private const char SearchSplitChar = ':';
        private const char ChainParameter = '.';
        internal const string ReverseChainParameter = "_has:";

        /// <summary>
        /// Initializes a new instance of the <see cref="ExpressionParser"/> class.
        /// </summary>
        /// <param name="searchParameterDefinitionManagerResolver">The search parameter definition manager.</param>
        /// <param name="searchParameterExpressionParser">The parser used to parse the search value into a search expression.</param>
        /// /// <param name="contextAccessor">Used to access the FhirRequestContext</param>
        public ExpressionParser(
            ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver searchParameterDefinitionManagerResolver,
            ISearchParameterExpressionParser searchParameterExpressionParser,
            RequestContextAccessor<IFhirRequestContext> contextAccessor)
        {
            EnsureArg.IsNotNull(searchParameterDefinitionManagerResolver, nameof(searchParameterDefinitionManagerResolver));
            EnsureArg.IsNotNull(searchParameterExpressionParser, nameof(searchParameterExpressionParser));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));

            _searchParameterDefinitionManager = searchParameterDefinitionManagerResolver();
            _searchParameterExpressionParser = searchParameterExpressionParser;
            _contextAccessor = contextAccessor;
        }

        /// <summary>
        /// Parses the input into a corresponding search expression.
        /// </summary>
        /// <param name="resourceTypes">The resource type.</param>
        /// <param name="key">The query key.</param>
        /// <param name="value">The query value.</param>
        /// <returns>An instance of search expression representing the search.</returns>
        public Expression Parse(string[] resourceTypes, string key, string value)
        {
            EnsureArg.IsNotNullOrWhiteSpace(key, nameof(key));
            EnsureArg.IsNotNullOrWhiteSpace(value, nameof(value));

            return ParseImpl(resourceTypes, key.AsSpan(), value);
        }

        public IncludeExpression ParseInclude(string[] resourceTypes, string includeValue, bool isReversed, bool iterate, IEnumerable<string> allowedResourceTypesByScope)
        {
            var valueSpan = includeValue.AsSpan();
            if (!TrySplit(SearchSplitChar, ref valueSpan, out ReadOnlySpan<char> originalType))
            {
                throw new InvalidSearchOperationException(isReversed ? Core.Resources.RevIncludeMissingType : Core.Resources.IncludeMissingType);
            }

            if (resourceTypes.Length == 1 && resourceTypes[0].Equals(KnownResourceTypes.DomainResource, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidSearchOperationException(Core.Resources.IncludeCannotBeAgainstBase);
            }

            allowedResourceTypesByScope = allowedResourceTypesByScope?.ToList();
            if (allowedResourceTypesByScope != null && !allowedResourceTypesByScope.Contains(KnownResourceTypes.All))
            {
                resourceTypes = resourceTypes.Intersect(allowedResourceTypesByScope).ToArray();
            }

            SearchParameterInfo refSearchParameter;
            List<string> referencedTypes = null;
            bool wildCard = false;
            string targetType = null;

            if (valueSpan.Equals("*".AsSpan(), StringComparison.InvariantCultureIgnoreCase))
            {
                refSearchParameter = null;
                wildCard = true;
            }
            else
            {
                if (!TrySplit(SearchSplitChar, ref valueSpan, out ReadOnlySpan<char> searchParam))
                {
                    searchParam = valueSpan;
                }
                else
                {
                    targetType = valueSpan.ToString();
                }

                refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter(originalType.ToString(), searchParam.ToString());
            }

            if (wildCard)
            {
                referencedTypes = new List<string>();
                var searchParameters = resourceTypes.SelectMany(t => _searchParameterDefinitionManager.GetSearchParameters(t))
                    .Where(p => p.Type == ValueSets.SearchParamType.Reference);

                foreach (var p in searchParameters)
                {
                    foreach (var t in p.TargetResourceTypes)
                    {
                        if (!referencedTypes.Contains(t))
                        {
                            referencedTypes.Add(t);
                        }
                    }
                }
            }

            if (allowedResourceTypesByScope != null && !allowedResourceTypesByScope.Contains(KnownResourceTypes.All) && referencedTypes != null)
            {
                referencedTypes = referencedTypes.Intersect(allowedResourceTypesByScope).ToList();
            }

            return new IncludeExpression(resourceTypes, refSearchParameter, originalType.ToString(), targetType, referencedTypes, wildCard, isReversed, iterate, allowedResourceTypesByScope);
        }

        private Expression ParseImpl(string[] resourceTypes, ReadOnlySpan<char> key, string value)
        {
            if (TryConsume(ReverseChainParameter.AsSpan(), ref key))
            {
                if (!TrySplit(SearchSplitChar, ref key, out ReadOnlySpan<char> type))
                {
                    throw new InvalidSearchOperationException(Core.Resources.ReverseChainMissingType);
                }

                if (!TrySplit(SearchSplitChar, ref key, out ReadOnlySpan<char> refParam))
                {
                    throw new InvalidSearchOperationException(Core.Resources.ReverseChainMissingReference);
                }

                string typeString = type.ToString();
                SearchParameterInfo refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter(typeString, refParam.ToString());

                return ParseChainedExpression(new[] { typeString }, refSearchParameter, resourceTypes, key, value, true);
            }

            if (TrySplit(ChainParameter, ref key, out ReadOnlySpan<char> chainedInput))
            {
                string[] targetType = Array.Empty<string>();

                if (TrySplit(SearchSplitChar, ref chainedInput, out ReadOnlySpan<char> refParamName))
                {
                    targetType = new[] { chainedInput.ToString() };
                }
                else
                {
                    refParamName = chainedInput;
                }

                if (refParamName.IsEmpty)
                {
                    throw new SearchParameterNotSupportedException(resourceTypes[0], key.ToString());
                }

                SearchParameterInfo refSearchParameter = _searchParameterDefinitionManager.GetSearchParameter(resourceTypes[0], refParamName.ToString());
                foreach (var resourceType in resourceTypes)
                {
                    if (refSearchParameter != _searchParameterDefinitionManager.GetSearchParameter(resourceType, refParamName.ToString()))
                    {
                        throw new BadRequestException(string.Format(Core.Resources.SearchParameterMustBeCommon, refParamName.ToString(), resourceTypes[0], resourceType));
                    }
                }

                return ParseChainedExpression(resourceTypes, refSearchParameter, targetType, key, value, false);
            }

            ReadOnlySpan<char> modifier;

            if (TrySplit(SearchSplitChar, ref key, out ReadOnlySpan<char> paramName))
            {
                modifier = key;
            }
            else
            {
                paramName = key;
                modifier = ReadOnlySpan<char>.Empty;
            }

            // Check to see if the search parameter is supported for this type or not.
            SearchParameterInfo searchParameter = _searchParameterDefinitionManager.GetSearchParameter(resourceTypes[0], paramName.ToString());
            foreach (var resourceType in resourceTypes)
            {
                if (searchParameter != _searchParameterDefinitionManager.GetSearchParameter(resourceType, paramName.ToString()))
                {
                    throw new BadRequestException(string.Format(Core.Resources.SearchParameterMustBeCommon, paramName.ToString(), resourceTypes[0], resourceType));
                }
            }

            return ParseSearchValueExpression(searchParameter, modifier.ToString(), value);
        }

        private Expression ParseChainedExpression(string[] resourceTypes, SearchParameterInfo searchParameter, string[] targetResourceTypes, ReadOnlySpan<char> remainingKey, string value, bool reversed)
        {
            // We have more paths after this so this is a chained expression.
            // Since this is chained expression, the expression must be a reference type.
            if (searchParameter.Type != ValueSets.SearchParamType.Reference)
            {
                // The search parameter is not a reference type, which is not allowed.
                throw new InvalidSearchOperationException(Core.Resources.ChainedParameterMustBeReferenceSearchParamType);
            }

            // Check to see if the client has specifically specified the target resource type to scope to.
            if (targetResourceTypes.Any())
            {
                // A target resource type is specified.
                foreach (var targetResourceType in targetResourceTypes)
                {
                    if (!ModelInfoProvider.IsKnownResource(targetResourceType))
                    {
                        throw new InvalidSearchOperationException(string.Format(Core.Resources.ResourceNotSupported, targetResourceType));
                    }

                    // check resource type restrictions from SMART clinical scopes
                    if (_contextAccessor.RequestContext?.AccessControlContext?.ApplyFineGrainedAccessControl == true)
                    {
                        if (!ResourceTypeAllowedByClinicalScopes(targetResourceType))
                        {
                            throw new InvalidSearchOperationException(string.Format(Core.Resources.ResourceTypeNotAllowedRestrictedByClinicalScopes, targetResourceType));
                        }
                    }
                }
            }

            var possibleTargetResourceTypes = targetResourceTypes.Any()
                ? targetResourceTypes.Intersect(searchParameter.TargetResourceTypes)
                : searchParameter.TargetResourceTypes;

            ChainedExpression chainedExpression = null;
            bool restrictedByClinicalScopes = false;

            foreach (var possibleTargetResourceType in possibleTargetResourceTypes)
            {
                var wrappedTargetResourceType = new[] { possibleTargetResourceType };
                var multipleChainType = reversed ? resourceTypes : wrappedTargetResourceType;

                // check resource type restrictions from SMART clinical scopes
                if (_contextAccessor.RequestContext?.AccessControlContext?.ApplyFineGrainedAccessControl == true)
                {
                    if (!ResourceTypeAllowedByClinicalScopes(possibleTargetResourceType))
                    {
                        restrictedByClinicalScopes = true;
                    }

                    foreach (var resourceType in multipleChainType)
                    {
                        if (!ResourceTypeAllowedByClinicalScopes(resourceType))
                        {
                            restrictedByClinicalScopes = true;
                        }
                    }

                    if (restrictedByClinicalScopes == true)
                    {
                        continue;
                    }
                }

                ChainedExpression expression;
                try
                {
                    expression = Expression.Chained(
                        resourceTypes,
                        searchParameter,
                        wrappedTargetResourceType,
                        reversed,
                        ParseImpl(
                            multipleChainType,
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
                else if (reversed)
                {
                    chainedExpression = Expression.Chained(
                        resourceTypes,
                        searchParameter,
                        chainedExpression.TargetResourceTypes.Append(possibleTargetResourceType).ToArray(),
                        reversed,
                        ParseImpl(
                            multipleChainType,
                            remainingKey,
                            value));
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
                            string.Join(Core.Resources.OrDelimiter, searchParameter.TargetResourceTypes.Select(c => $"{searchParameter.Code}:{c}"))));
                }
            }

            if (chainedExpression == null)
            {
                if (restrictedByClinicalScopes)
                {
                    throw new InvalidSearchOperationException(Core.Resources.ChainedResourceTypesNotAllowedDueToScope);
                }

                // There was no reference that supports the search parameter.
                throw new InvalidSearchOperationException(Core.Resources.ChainedParameterNotSupported);
            }

            return chainedExpression;
        }

        private Expression ParseSearchValueExpression(SearchParameterInfo searchParameter, string modifier, string value)
        {
            SearchModifier parsedModifier = ParseSearchParamModifier();
            return _searchParameterExpressionParser.Parse(searchParameter, parsedModifier, value);

            SearchModifier ParseSearchParamModifier()
            {
                if (string.IsNullOrEmpty(modifier))
                {
                    return null;
                }

                if (SearchParamModifierMapping.TryGetValue(modifier, out SearchModifierCode searchModifierCode))
                {
                    return new SearchModifier(searchModifierCode);
                }

                // Modifier on a Reference Search Parameter can be used to restrict target type
                if (searchParameter.Type == SearchParamType.Reference && searchParameter.TargetResourceTypes.Contains(modifier, StringComparer.OrdinalIgnoreCase))
                {
                    return new SearchModifier(SearchModifierCode.Type, modifier);
                }

                throw new InvalidSearchOperationException(
                    string.Format(Core.Resources.ModifierNotSupported, modifier, searchParameter.Code));
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

        private bool ResourceTypeAllowedByClinicalScopes(string resourceType)
        {
            var resourceTypes = _contextAccessor.RequestContext?.AccessControlContext.AllowedResourceActions.Select(r => r.Resource).Distinct().ToList();

            if (resourceTypes.Contains(KnownResourceTypes.All) || resourceTypes.Contains(resourceType))
            {
                return true;
            }

            return false;
        }
    }
}
