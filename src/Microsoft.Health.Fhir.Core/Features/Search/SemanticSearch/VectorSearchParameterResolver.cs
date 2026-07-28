// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Resolves configured canonical URIs through the server's FHIR SearchParameter registry.
    /// </summary>
    public sealed class VectorSearchParameterResolver : IVectorSearchParameterResolver
    {
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly IReadOnlyList<Uri> _enabledSearchParameters;
        private readonly HashSet<string> _enabledCanonicalUris;

        /// <summary>
        /// Initializes a new instance of the <see cref="VectorSearchParameterResolver"/> class.
        /// </summary>
        /// <param name="searchParameterDefinitionManager">The FHIR SearchParameter definition manager.</param>
        /// <param name="configuration">The vector-search configuration.</param>
        public VectorSearchParameterResolver(
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IOptions<VectorSearchConfiguration> configuration)
        {
            _searchParameterDefinitionManager = EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            VectorSearchConfiguration vectorSearchConfiguration = EnsureArg.IsNotNull(configuration, nameof(configuration)).Value;
            _enabledSearchParameters = vectorSearchConfiguration.Indexing.EnabledSearchParameters.ToList();
            _enabledCanonicalUris = _enabledSearchParameters
                .Select(uri => uri.OriginalString)
                .ToHashSet(StringComparer.Ordinal);
        }

        /// <inheritdoc />
        public IReadOnlyList<SearchParameterInfo> GetSearchParameters(string resourceType)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

            foreach (Uri canonicalUri in _enabledSearchParameters)
            {
                GetSearchParameter(canonicalUri);
            }

            return _searchParameterDefinitionManager
                .GetSearchParameters(resourceType)
                .Where(searchParameter => searchParameter.Url != null && _enabledCanonicalUris.Contains(searchParameter.Url.OriginalString))
                .OrderBy(searchParameter => searchParameter.Url.OriginalString, StringComparer.Ordinal)
                .ToList();
        }

        /// <inheritdoc />
        public SearchParameterInfo GetSearchParameter(Uri canonicalUri)
        {
            EnsureArg.IsNotNull(canonicalUri, nameof(canonicalUri));

            if (!_enabledCanonicalUris.Contains(canonicalUri.OriginalString))
            {
                throw new SearchParameterNotSupportedException(canonicalUri);
            }

            if (!_searchParameterDefinitionManager.TryGetSearchParameter(canonicalUri.OriginalString, excludePendingDelete: true, out SearchParameterInfo searchParameter))
            {
                throw new InvalidOperationException($"Enabled vector SearchParameter '{canonicalUri}' is not registered by the FHIR server.");
            }

            Validate(searchParameter);
            return searchParameter;
        }

        private static void Validate(SearchParameterInfo searchParameter)
        {
            if (searchParameter.Type != SearchParamType.Special)
            {
                throw new InvalidOperationException($"Vector SearchParameter '{searchParameter.Url}' must use FHIR type 'special'.");
            }

            if (!string.Equals(searchParameter.DefinitionStatus, "active", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Vector SearchParameter '{searchParameter.Url}' must have FHIR publication status 'active'.");
            }

            if (searchParameter.BaseResourceTypes == null || searchParameter.BaseResourceTypes.Count == 0)
            {
                throw new InvalidOperationException($"Vector SearchParameter '{searchParameter.Url}' must declare at least one FHIR base resource type.");
            }

            if (string.IsNullOrWhiteSpace(searchParameter.Expression))
            {
                throw new InvalidOperationException($"Vector SearchParameter '{searchParameter.Url}' must declare an expression.");
            }

            if (searchParameter.VectorConfig == null)
            {
                throw new InvalidOperationException($"Vector SearchParameter '{searchParameter.Url}' must declare the '{VectorSearchParameterConfig.ExtensionUrl}' extension.");
            }

            if (!searchParameter.IsSupported || !searchParameter.IsSearchable)
            {
                throw new InvalidOperationException($"Vector SearchParameter '{searchParameter.Url}' must be supported and searchable.");
            }
        }
    }
}
