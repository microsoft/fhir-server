// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Resolves active vector definitions through the server's FHIR SearchParameter registry.
    /// </summary>
    public sealed class VectorSearchParameterResolver : IVectorSearchParameterResolver
    {
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="VectorSearchParameterResolver"/> class.
        /// </summary>
        /// <param name="searchParameterDefinitionManager">The FHIR SearchParameter definition manager.</param>
        public VectorSearchParameterResolver(ISearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            _searchParameterDefinitionManager = EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
        }

        /// <inheritdoc />
        public IReadOnlyList<SearchParameterInfo> GetSearchParameters(string resourceType)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

            return _searchParameterDefinitionManager
                .GetSearchParameters(resourceType)
                .Where(searchParameter => TryValidate(searchParameter, requireSearchable: true, out _))
                .OrderBy(searchParameter => searchParameter.Url.OriginalString, StringComparer.Ordinal)
                .ToList();
        }

        /// <inheritdoc />
        public IReadOnlyList<SearchParameterInfo> GetIndexingSearchParameters(string resourceType)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

            return _searchParameterDefinitionManager
                .GetSearchParameters(resourceType)
                .Where(searchParameter => TryValidate(searchParameter, requireSearchable: false, out _))
                .OrderBy(searchParameter => searchParameter.Url.OriginalString, StringComparer.Ordinal)
                .ToList();
        }

        /// <inheritdoc />
        public SearchParameterInfo GetSearchParameter(Uri canonicalUri)
        {
            EnsureArg.IsNotNull(canonicalUri, nameof(canonicalUri));

            if (!_searchParameterDefinitionManager.TryGetSearchParameter(canonicalUri.OriginalString, excludePendingDelete: true, out SearchParameterInfo searchParameter))
            {
                throw new SearchParameterNotSupportedException(canonicalUri);
            }

            Validate(searchParameter);
            return searchParameter;
        }

        private static void Validate(SearchParameterInfo searchParameter)
        {
            if (!TryValidate(searchParameter, requireSearchable: true, out string errorMessage))
            {
                throw new InvalidOperationException(errorMessage);
            }
        }

        private static bool TryValidate(SearchParameterInfo searchParameter, bool requireSearchable, out string errorMessage)
        {
            if (searchParameter.Type != SearchParamType.Special)
            {
                errorMessage = $"Vector SearchParameter '{searchParameter.Url}' must use FHIR type 'special'.";
                return false;
            }

            if (!string.Equals(searchParameter.DefinitionStatus, "active", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = $"Vector SearchParameter '{searchParameter.Url}' must have FHIR publication status 'active'.";
                return false;
            }

            if (searchParameter.BaseResourceTypes == null || searchParameter.BaseResourceTypes.Count == 0)
            {
                errorMessage = $"Vector SearchParameter '{searchParameter.Url}' must declare at least one FHIR base resource type.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(searchParameter.Expression))
            {
                errorMessage = $"Vector SearchParameter '{searchParameter.Url}' must declare an expression.";
                return false;
            }

            if (searchParameter.VectorConfig == null)
            {
                errorMessage = $"Vector SearchParameter '{searchParameter.Url}' must declare the '{VectorSearchParameterConfig.ExtensionUrl}' extension.";
                return false;
            }

            bool isEligibleForIndexing = searchParameter.IsSearchable || searchParameter.SearchParameterStatus == SearchParameterStatus.Supported;
            if (!searchParameter.IsSupported || (requireSearchable ? !searchParameter.IsSearchable : !isEligibleForIndexing))
            {
                errorMessage = requireSearchable
                    ? $"Vector SearchParameter '{searchParameter.Url}' must be supported and searchable."
                    : $"Vector SearchParameter '{searchParameter.Url}' must be enabled or awaiting activation in the supported state.";
                return false;
            }

            errorMessage = null;
            return true;
        }
    }
}
