// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.Expressions
{
    /// <summary>
    /// Provides mechanism to parse the search expression.
    /// </summary>
    public class LegacyExpressionParser : ILegacyExpressionParser
    {
        private readonly IResourceTypeManifestManager _resourceTypeManifestManager;
        private readonly ILegacySearchValueParser _searchValueParser;

        /// <summary>
        /// Initializes a new instance of the <see cref="LegacyExpressionParser"/> class.
        /// </summary>
        /// <param name="resourceTypeManifestManager">The resource type manifest manager instance.</param>
        /// <param name="searchValueParser">The parser used to parse search value.</param>
        public LegacyExpressionParser(
            IResourceTypeManifestManager resourceTypeManifestManager,
            ILegacySearchValueParser searchValueParser)
        {
            EnsureArg.IsNotNull(resourceTypeManifestManager, nameof(resourceTypeManifestManager));
            EnsureArg.IsNotNull(searchValueParser, nameof(searchValueParser));

            _resourceTypeManifestManager = resourceTypeManifestManager;
            _searchValueParser = searchValueParser;
        }

        /// <summary>
        /// Parses the input into a corresponding search expression.
        /// </summary>
        /// <param name="resourceTypeManifest">The resource type manifest which the search is being executed.</param>
        /// <param name="key">The query key.</param>
        /// <param name="value">The query value.</param>
        /// <returns>An instance of search expression representing the search.</returns>
        public Expression Parse(ResourceTypeManifest resourceTypeManifest, string key, string value)
        {
            EnsureArg.IsNotNull(resourceTypeManifest, nameof(resourceTypeManifest));
            EnsureArg.IsNotNullOrWhiteSpace(key, nameof(key));
            EnsureArg.IsNotNullOrWhiteSpace(value, nameof(value));

            // Split the key by the chain separator.
            PathSegment[] paths = key.Split(new[] { SearchParams.SEARCH_CHAINSEPARATOR }, StringSplitOptions.RemoveEmptyEntries)
                 .Select(path => PathSegment.Parse(path))
                 .ToArray();

            return Parse(resourceTypeManifest, paths, currentIndex: 0, value: value);
        }

        private Expression Parse(ResourceTypeManifest resourceTypeManifest, PathSegment[] paths, int currentIndex, string value)
        {
            Debug.Assert(
                currentIndex >= 0 && currentIndex < paths.Length,
                $"The {nameof(currentIndex)} is invalid.");

            // TODO: We should keep track of the recursive calls to make sure we won't end up in a loop.
            PathSegment currentPath = paths[currentIndex];

            // Check to see if the search parameter is supported for this type or not.
            SearchParam searchParam = resourceTypeManifest
                .GetSearchParam(currentPath.Path);

            if (currentIndex != paths.Length - 1)
            {
                return ParseChainedExpression(searchParam, paths, currentIndex, value);
            }
            else
            {
                return _searchValueParser.Parse(searchParam, currentPath.ModifierOrResourceType, value);
            }
        }

        private Expression ParseChainedExpression(SearchParam searchParam, PathSegment[] paths, int currentIndex, string value)
        {
            Debug.Assert(
                currentIndex >= 0 && currentIndex < paths.Length,
                $"The {nameof(currentIndex)} is invalid.");

            PathSegment path = paths[currentIndex];
            string currentPath = path.Path;
            string resourceType = path.ModifierOrResourceType;

            // We have more paths after this so this is a chained expression.
            var referenceSearchParam = searchParam as ReferenceSearchParam;

            // Since this is chained expression, the expression must be a reference type.
            if (referenceSearchParam == null)
            {
                // The search parameter is not a reference type, which is not allowed.
                throw new InvalidSearchOperationException(Core.Resources.ChainedParameterMustBeReferenceSearchParamType);
            }

            Type scopedTargetResourceType = null;

            // Check to see if the client has specifically specified the target resource type to scope to.
            if (resourceType != null)
            {
                // A target resource type is specified.
                scopedTargetResourceType = ModelInfo.GetTypeForFhirType(resourceType);

                if (scopedTargetResourceType == null)
                {
                    throw new InvalidSearchOperationException(string.Format(Core.Resources.ResourceNotSupported, resourceType));
                }
            }

            // If the scoped target resource type is specified, we will scope to that; otherwise, all target resource types are considered.
            ChainedExpression[] chainedExpressions = referenceSearchParam.TargetReferenceTypes
                .Where(targetType => (scopedTargetResourceType ?? targetType) == targetType)
                .Select(targetType =>
                {
                    try
                    {
                        return Expression.Chained(
                            (ResourceType)Enum.Parse(typeof(ResourceType), searchParam.ResourceType.Name),
                            currentPath,
                            (ResourceType)Enum.Parse(typeof(ResourceType), targetType.Name),
                            Parse(
                                _resourceTypeManifestManager.GetManifest(targetType),
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
