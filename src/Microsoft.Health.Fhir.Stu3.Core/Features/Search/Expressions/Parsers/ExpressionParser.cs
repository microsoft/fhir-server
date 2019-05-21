// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Utility;
using Microsoft.Health.Fhir.Core.Features.Definition;
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

            // Split the key by the chain separator.
            PathSegment[] paths = key.Split(new[] { SearchParams.SEARCH_CHAINSEPARATOR }, StringSplitOptions.RemoveEmptyEntries)
                 .Select(path => PathSegment.Parse(path))
                 .ToArray();

            if (paths?.Length == 0)
            {
                throw new SearchParameterNotSupportedException(resourceType, key);
            }

            return Parse(resourceType, paths, currentIndex: 0, value: value);
        }

        private Expression Parse(string resourceType, PathSegment[] paths, int currentIndex, string value)
        {
            Debug.Assert(
                currentIndex >= 0 && currentIndex < paths.Length,
                $"The {nameof(currentIndex)} is invalid.");

            // TODO: We should keep track of the recursive calls to make sure we won't end up in a loop.
            PathSegment currentPath = paths[currentIndex];

            // Check to see if the search parameter is supported for this type or not.
            SearchParameter searchParameter = _searchParameterDefinitionManager.GetSearchParameter(resourceType, currentPath.Path);

            if (currentIndex != paths.Length - 1)
            {
                return ParseChainedExpression(resourceType, searchParameter, paths, currentIndex, value);
            }
            else
            {
                return ParseSearchValueExpression(searchParameter, currentPath.ModifierOrResourceType, value);
            }
        }

        private Expression ParseChainedExpression(string resourceType, SearchParameter searchParameter, PathSegment[] paths, int currentIndex, string value)
        {
            Debug.Assert(
                currentIndex >= 0 && currentIndex < paths.Length,
                $"The {nameof(currentIndex)} is invalid.");

            PathSegment path = paths[currentIndex];
            string currentPath = path.Path;
            string targetResourceType = path.ModifierOrResourceType;

            // We have more paths after this so this is a chained expression.
            // Since this is chained expression, the expression must be a reference type.
            if (searchParameter.Type != SearchParamType.Reference)
            {
                // The search parameter is not a reference type, which is not allowed.
                throw new InvalidSearchOperationException(Core.Resources.ChainedParameterMustBeReferenceSearchParamType);
            }

            ResourceType? scopedTargetResourceType = null;

            // Check to see if the client has specifically specified the target resource type to scope to.
            if (targetResourceType != null)
            {
                // A target resource type is specified.
                if (!Enum.TryParse(targetResourceType, out ResourceType parsedResourceType))
                {
                    throw new InvalidSearchOperationException(string.Format(Core.Resources.ResourceNotSupported, resourceType));
                }

                scopedTargetResourceType = parsedResourceType;
            }

            // If the scoped target resource type is specified, we will scope to that; otherwise, all target resource types are considered.
            ChainedExpression[] chainedExpressions = searchParameter.Target
                .Where(targetType => targetType != null && (scopedTargetResourceType ?? targetType) == targetType)
                .Select(targetType =>
                {
                    try
                    {
                        return Expression.Chained(
                            resourceType,
                            currentPath,
                            targetType.Value.ToString(),
                            Parse(
                                targetType.Value.ToString(),
                                paths,
                                currentIndex + 1,
                                value));
                    }
                    catch (Exception ex) when (ex is ResourceNotSupportedException || ex is SearchParameterNotSupportedException)
                    {
                        // The resource or search parameter is not supported for the resource.
                        // We will ignore these unsupported types.
                        return null;
                    }
                })
                .Where(item => item != null)
                .ToArray();

            if (!chainedExpressions.Any())
            {
                // There was no reference that supports the search parameter.
                throw new InvalidSearchOperationException(Core.Resources.ChainedParameterNotSupported);
            }

            return Expression.Or(chainedExpressions);
        }

        private Expression ParseSearchValueExpression(SearchParameter searchParameter, string modifier, string value)
        {
            SearchModifierCode? parsedModifier = ParseSearchParamModifier();

            return _searchParameterExpressionParser.Parse(searchParameter, parsedModifier, value);

            SearchModifierCode? ParseSearchParamModifier()
            {
                if (modifier == null)
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

        private struct PathSegment
        {
            public PathSegment(string path, string modifier)
            {
                Path = path;
                ModifierOrResourceType = modifier;
            }

            public string Path { get; }

            public string ModifierOrResourceType { get; }

            public static PathSegment Parse(string s)
            {
                // The format of each of the path segment is X:Y where X is the search parameter name and
                // Y is either a modifier or a resource type depending on the search parameter type.
                // For example, in the case of gender:missing=true, gender is a token search parameter and missing is the modifier.
                // However, in the case of subject:Patient.name=peter, subject is a reference search parameter
                // and Patient is the target resource type. Since subject can be reference to various resource types,
                // the client may specify the target resource type to limit the scope of the search.
                string[] parts = s.Split(
                    new[] { SearchParams.SEARCH_MODIFIERSEPARATOR },
                    StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length > 2)
                {
                    throw new InvalidSearchOperationException(Core.Resources.OnlyOneModifierSeparatorSupported);
                }

                string path = parts[0];
                string modifierOrResourceType = parts.Length == 2 ? parts[1] : null;

                return new PathSegment(path, modifierOrResourceType);
            }
        }
    }
}
